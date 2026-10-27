using System;
using System.Collections.Concurrent;
using System.Linq;
using Core.LogModule;
using LibVLCSharp.Shared;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Desktop.Services.SpatialAudio
{
    /// <summary>
    /// 全局空间音频引擎与 WASAPI 统一混音输出调度器
    /// </summary>
    public sealed class SpatialAudioEngine : ISampleProvider
    {
        private static readonly Lazy<SpatialAudioEngine> _instance = new(() => new SpatialAudioEngine());
        public static SpatialAudioEngine Instance => _instance.Value;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        private readonly ConcurrentDictionary<long, SpatialAudioChannel> _channels = new();
        public ConcurrentDictionary<long, SpatialAudioChannel> Channels => _channels;

        // 保存各通道的 LibVLC 回调委托引用，防止被 GC 提前回收导致 native crash
        private readonly ConcurrentDictionary<long, (MediaPlayer.LibVLCAudioPlayCb Play, MediaPlayer.LibVLCAudioPauseCb Pause, MediaPlayer.LibVLCAudioResumeCb Resume, MediaPlayer.LibVLCAudioFlushCb Flush, MediaPlayer.LibVLCAudioDrainCb Drain)> _vlcCallbacks = new();

        private WasapiOut? _wasapiOut;
        private readonly object _wasapiLock = new();
        private bool _isOutputRunning = false;

        private float _listenerHeadYaw = 0f;
        /// <summary>
        /// 听者人头朝向角（-180° ~ +180°，0° 为正前方，+90° 为正右方，-90° 为正左方）
        /// 支持通过在 3D 雷达界面中键拖拽实时平滑旋转
        /// </summary>
        public float ListenerHeadYaw
        {
            get => _listenerHeadYaw;
            set
            {
                float normalized = NormalizeAngle(value);
                if (Math.Abs(_listenerHeadYaw - normalized) > 0.01f)
                {
                    _listenerHeadYaw = normalized;
                    ListenerHeadYawChanged?.Invoke(_listenerHeadYaw);
                }
            }
        }

        private bool _isEnabled = true;
        /// <summary>
        /// 是否启用空间音频渲染。关闭时直通普通立体声混音输出
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                SpatialEnabledChanged?.Invoke(_isEnabled);
            }
        }

        public event Action<float>? ListenerHeadYawChanged;
        public event Action<bool>? SpatialEnabledChanged;
        public event Action<SpatialAudioChannel>? ChannelAdded;
        public event Action<long>? ChannelRemoved;

        private SpatialAudioEngine()
        {
        }

        /// <summary>
        /// 为指定的 LibVLC MediaPlayer 注册空间音频截获通道
        /// </summary>
        public SpatialAudioChannel RegisterPlayer(MediaPlayer player, long roomId, string title, long uid)
        {
            EnsureWasapiRunning();

            var channel = _channels.GetOrAdd(roomId, id =>
            {
                var newChan = new SpatialAudioChannel(id, title, uid);
                // 初始位置自适应分布：根据当前已开通道数量在四周合理排布
                AutoAssignInitialPosition(newChan);
                return newChan;
            });

            // 保持最新标题与 UID
            channel.Title = title;
            channel.UID = uid;

            // 构造 LibVLC 回调并强引用持有
            MediaPlayer.LibVLCAudioPlayCb playCb = (data, samples, count, pts) =>
            {
                // LibVLC 在 FL32 格式下，count 为音频采样帧数，每帧含左右 2 个 float
                int floatCount = (int)(count * 2);
                channel.Buffer.Write(samples, floatCount);
            };

            MediaPlayer.LibVLCAudioPauseCb pauseCb = (data, pts) => { };
            MediaPlayer.LibVLCAudioResumeCb resumeCb = (data, pts) => { };
            MediaPlayer.LibVLCAudioFlushCb flushCb = (data, pts) =>
            {
                channel.Buffer.Clear();
            };
            MediaPlayer.LibVLCAudioDrainCb drainCb = (data) => { };

            _vlcCallbacks[roomId] = (playCb, pauseCb, resumeCb, flushCb, drainCb);

            // 配置 LibVLC 输出格式为 48kHz 32位浮点立体声并挂载回调
            player.SetAudioFormat("FL32", 48000, 2);
            player.SetAudioCallbacks(playCb, pauseCb, resumeCb, flushCb, drainCb);

            ChannelAdded?.Invoke(channel);
            Log.Info(nameof(SpatialAudioEngine), $"已为房间 [{roomId}] 启用空间音频采集通道，初始方位角: {channel.Azimuth:F1}°");

            return channel;
        }

        /// <summary>
        /// 播放窗口关闭时注销对应通道
        /// </summary>
        public void UnregisterPlayer(long roomId)
        {
            _vlcCallbacks.TryRemove(roomId, out _);
            if (_channels.TryRemove(roomId, out var chan))
            {
                chan.Buffer.Clear();
                ChannelRemoved?.Invoke(roomId);
                Log.Info(nameof(SpatialAudioEngine), $"已注销房间 [{roomId}] 空间音频通道");
            }

            // 若所有通道均已关闭，可暂停 WASAPI 输出以彻底节约 CPU
            if (_channels.IsEmpty)
            {
                StopWasapiIfIdle();
            }
        }

        /// <summary>
        /// 根据已存在通道数量，将新加入的直播间音源均衡散布在前方扇区中
        /// </summary>
        private void AutoAssignInitialPosition(SpatialAudioChannel newChan)
        {
            int count = _channels.Count;
            if (count == 0)
            {
                newChan.Azimuth = 0f; // 第一个居中
                newChan.Distance = 1.0f;
            }
            else
            {
                // 在 -60° ~ +60° 前方扇形区域内交替均匀分布
                float[] presets = { -35f, 35f, -60f, 60f, -15f, 15f, -80f, 80f, 0f };
                int idx = (count - 1) % presets.Length;
                newChan.Azimuth = presets[idx];
                newChan.Distance = 1.0f + (idx * 0.05f);
            }
        }

        private void EnsureWasapiRunning()
        {
            lock (_wasapiLock)
            {
                if (_isOutputRunning && _wasapiOut != null) return;

                try
                {
                    _wasapiOut = new WasapiOut(AudioClientShareMode.Shared, latency: 40);
                    _wasapiOut.Init(this);
                    _wasapiOut.Play();
                    _isOutputRunning = true;
                    Log.Info(nameof(SpatialAudioEngine), "WASAPI 空间混音输出引擎启动成功 (共享模式，40ms 延迟)");
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(SpatialAudioEngine), "WASAPI 初始化失败，尝试降低延迟配置", ex);
                }
            }
        }

        private void StopWasapiIfIdle()
        {
            lock (_wasapiLock)
            {
                if (_wasapiOut != null && _channels.IsEmpty)
                {
                    try
                    {
                        _wasapiOut.Stop();
                        _wasapiOut.Dispose();
                        _wasapiOut = null;
                        _isOutputRunning = false;
                        Log.Info(nameof(SpatialAudioEngine), "所有空间音频通道已关闭，WASAPI 混音引擎已休眠");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(nameof(SpatialAudioEngine), "关闭 WASAPI 出现异常", ex);
                    }
                }
            }
        }

        /// <summary>
        /// WASAPI 音频数据消费回调（运行在高优先级音频线程）
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            // 1. 清空主混音缓冲区
            Array.Clear(buffer, offset, count);

            // 2. 遍历各通道进行空间化计算与混音叠加
            float currentHeadYaw = _listenerHeadYaw;
            bool enabled = _isEnabled;

            foreach (var kv in _channels)
            {
                var channel = kv.Value;
                channel.MixTo(buffer, offset, count, currentHeadYaw, enabled);
            }

            // 3. 软削波限制器 (Soft Limiter / Polynomial Saturator)
            // 保证多房间同时发声叠加时不会产生爆音和数字破音
            for (int i = 0; i < count; i++)
            {
                int idx = offset + i;
                float s = buffer[idx];

                if (s > 0.92f)
                {
                    buffer[idx] = 0.92f + MathF.Tanh((s - 0.92f) / 0.08f) * 0.08f;
                }
                else if (s < -0.92f)
                {
                    buffer[idx] = -0.92f + MathF.Tanh((s + 0.92f) / 0.08f) * 0.08f;
                }
            }

            return count;
        }

        public int Read(Span<float> buffer)
        {
            float[] temp = new float[buffer.Length];
            int read = Read(temp, 0, temp.Length);
            temp.AsSpan(0, read).CopyTo(buffer);
            return read;
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
