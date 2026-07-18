using Core.RuntimeObject;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Server.WebAppServices.WS
{
    public class WebSocketQueue
    {
        private static bool _start = false;
        private static readonly object _startLock = new();
        /// <summary>
        /// 推送消息队列（单消费者模式，业务线程入队后立即返回，避免序列化和发送阻塞业务）
        /// </summary>
        private static readonly Channel<Core.LogModule.OperationQueue.pack<RoomCardClass>> _queue = Channel.CreateBounded<Core.LogModule.OperationQueue.pack<RoomCardClass>>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        public static void Start()
        {
            lock (_startLock)
            {
                if (!_start)
                {
                    _start = true;
                    Core.LogModule.OperationQueue.AddOperationRecord += OperationQueue_AddOperationRecord;
                    Task.Factory.StartNew(ConsumerLoop, TaskCreationOptions.LongRunning);
                }
            }
        }

        /// <summary>
        /// 事件处理器只入队，不做序列化和发送，业务线程立即返回
        /// </summary>
        private static void OperationQueue_AddOperationRecord(object? sender, EventArgs e)
        {
            if (sender is Core.LogModule.OperationQueue.pack<RoomCardClass> pack)
            {
                _queue.Writer.TryWrite(pack);
            }
        }

        /// <summary>
        /// 推送消费者主循环：批量取出消息，合并同cmd同UID的重复推送后串行发送
        /// </summary>
        private static async Task ConsumerLoop()
        {
            List<Core.LogModule.OperationQueue.pack<RoomCardClass>> batch = new(256);
            while (await _queue.Reader.WaitToReadAsync())
            {
                try
                {
                    batch.Clear();
                    while (batch.Count < 256 && _queue.Reader.TryRead(out Core.LogModule.OperationQueue.pack<RoomCardClass>? pack))
                    {
                        if (pack != null)
                            batch.Add(pack);
                    }
                    //合并同cmd且同UID的重复消息，只保留最新一条（消息格式不变，仅削掉冗余）
                    //倒序遍历保留最后出现的消息，再反转回原始顺序
                    HashSet<string> seen = new();
                    List<Core.LogModule.OperationQueue.pack<RoomCardClass>> merged = new(batch.Count);
                    for (int i = batch.Count - 1; i >= 0; i--)
                    {
                        var pack = batch[i];
                        string key = $"{pack.cmd}_{pack.data?.UID ?? 0}";
                        if (seen.Add(key))
                            merged.Add(pack);
                    }
                    merged.Reverse();
                    foreach (var pack in merged)
                    {
                        try
                        {
                            string MessagePack = JsonConvert.SerializeObject(pack);
                            await MessageBase.WS_Send(MessagePack);
                        }
                        catch (Exception)
                        {
                            //单条消息序列化/发送异常不影响后续消息
                        }
                    }
                }
                catch (Exception)
                {
                    //整批处理异常时跳过该批，保证消费者线程不退出、推送不静默中断
                }
            }
        }
    }
}
