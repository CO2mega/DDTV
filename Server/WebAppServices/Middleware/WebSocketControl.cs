using Core.LogModule;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace Server.WebAppServices.Middleware
{
    public class WebSocketControl
    {
        private readonly RequestDelegate _next;

        private static readonly List<WebSocket> webSockets = new List<WebSocket>();
        private static readonly object _webSocketsLock = new object();
        /// <summary>
        /// 每个连接的发送锁（一个WebSocket同一时间只允许一个未完成的发送，广播和Echo回显必须串行）
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<WebSocket, SemaphoreSlim> _sendLocks = new();

        public WebSocketControl(RequestDelegate next)
        {
            _next = next;
        }

        /// <summary>
        /// 添加WebSocket连接
        /// </summary>
        public static void AddWebSocket(WebSocket webSocket)
        {
            lock (_webSocketsLock)
            {
                webSockets.Add(webSocket);
            }
        }

        /// <summary>
        /// 移除WebSocket连接
        /// </summary>
        public static void RemoveWebSocket(WebSocket webSocket)
        {
            lock (_webSocketsLock)
            {
                webSockets.Remove(webSocket);
            }
            //只从字典移除即可，不主动Dispose：可能仍有正在使用它的发送任务
            _sendLocks.TryRemove(webSocket, out _);
        }

        /// <summary>
        /// 获取当前连接列表快照（遍历时不受增删影响）
        /// </summary>
        public static WebSocket[] GetWebSocketSnapshot()
        {
            lock (_webSocketsLock)
            {
                return webSockets.ToArray();
            }
        }

        /// <summary>
        /// 串行化发送（同一连接上的广播与Echo回显互斥，避免并发SendAsync导致发送失败）
        /// </summary>
        public static async Task SendAsyncSafe(WebSocket webSocket, ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            SemaphoreSlim sem = _sendLocks.GetOrAdd(webSocket, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(cancellationToken);
            try
            {
                await webSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path == "/ws")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    AddWebSocket(webSocket);
                    try
                    {
                        await Echo(context, webSocket);
                    }
                    catch (Exception)
                    {
                        Log.Info(nameof(WebSocketControl), "WS握手/连接出现错误", false);
                    }
                    finally
                    {
                        //无论正常关闭还是异常断开，都要从推送列表移除，避免死连接堆积
                        RemoveWebSocket(webSocket);
                        try
                        {
                            webSocket.Dispose();
                        }
                        catch (Exception) { }
                    }

                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            }
            else
            {
                await _next(context);
            }
        }

        private async Task Echo(HttpContext context, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 8];

            if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.Connecting)
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                while (!result.CloseStatus.HasValue)
                {
                    await SendAsyncSafe(webSocket, new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }
            }

        }
    }

}
