using AngleSharp.Dom;
using Core.LogModule;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using static Server.WebAppServices.MessageCode;

namespace Server.WebAppServices
{
    /// <summary>
    /// 整个API核心的核心返参打包类
    /// </summary>
    public class MessageBase
    {
        /// <summary>
        /// 打包返回数据
        /// </summary>
        /// <typeparam name="T">data主体的，泛型入参</typeparam>
        /// <param name="cmd">命令名称，默认应该操作类的nameof</param>
        /// <param name="data">返回的数据主体内容</param>
        /// <param name="message">提示文本消息</param>
        /// <param name="code">状态码</param>
        /// <returns></returns>
        public static string MessagePack<T>(string cmd, T data, string message = "", code code = code.ok)
        {
            if (!Core.RuntimeObject.Account.AccountInformation.State)
            {
                code = code.LoginInfoFailure;
            }
            OperationQueue.pack<T> pack = new OperationQueue.pack<T>()
            {
                cmd = cmd,
                code = (int)code,
                data = data,
                message = message
            };
            string MessagePack = JsonConvert.SerializeObject(pack);
            return MessagePack;
        }

        /// <summary>
        /// WebHook推送用的共享HttpClient，避免每条消息新建连接导致socket耗尽
        /// </summary>
        private static readonly HttpClient _webHookClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        /// <summary>
        /// WebSocket数据发送（仅由WebSocketQueue的单消费者线程调用，逐客户端串行await发送，
        /// 避免同一socket并发SendAsync导致消息丢失；发送失败的连接直接移除）
        /// </summary>
        /// <param name="message">要推送的文本内容</param>
        public static async Task WS_Send(string message)
        {
            WebHookSend(message);
            ArraySegment<byte> buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
            foreach (WebSocket item in Middleware.WebSocketControl.GetWebSocketSnapshot())
            {
                try
                {
                    if (item.State == WebSocketState.Open)
                        await Middleware.WebSocketControl.SendAsyncSafe(item, buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                    else if (item.State != WebSocketState.Connecting)
                        Middleware.WebSocketControl.RemoveWebSocket(item);
                }
                catch (System.Exception ex)
                {
                    Log.Warn(nameof(WS_Send), "WebSocket信息推送失败，已移除该连接", ex, false);
                    Middleware.WebSocketControl.RemoveWebSocket(item);
                }
            }
        }

        /// <summary>
        /// WebHook数据推送（后台异步发送，不阻塞调用方）
        /// </summary>
        /// <param name="message">要推送的文本内容</param>
        private static void WebHookSend(string message)
        {
            if (Core.Config.Core_RunConfig._WebHookSwitch && !string.IsNullOrEmpty(Core.Config.Core_RunConfig._WebHookAddress))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var data = new StringContent(message, Encoding.UTF8, "application/json");
                        var response = await _webHookClient.PostAsync(Core.Config.Core_RunConfig._WebHookAddress, data);
                        string result = await response.Content.ReadAsStringAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(nameof(WebHookSend), "WebHook信息推送失败", ex, false);
                    }
                });
            }
        }


        public class pack<T>
        {
            /// <summary>
            /// 状态码
            /// </summary>
            public code code { get; set; }
            /// <summary>
            /// 接口名称
            /// </summary>
            public string cmd { get; set; }
            /// <summary>
            /// 信息
            /// </summary>
            public string message { get; set; }
            /// <summary>
            /// 对应的接口数据
            /// </summary>
            public T data { get; set; }
        }

    }
}
