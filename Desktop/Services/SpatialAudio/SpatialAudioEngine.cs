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
    /// 全局空间音频引擎与集中式混音输出调度器
    /// </summary>
    public sealed class SpatialAudioEngine : ISampleProvider
    {
        private static readonly Lazy<SpatialAudioEngine> _instance = new(() => new SpatialAudioEngine());
        public static SpatialAudioEngine Instance => _instance.Value;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        private readonly ConcurrentDictionary<long, SpatialAudioChannel> _channels = new();
        public ConcurrentDictionary<long, SpatialAudioChannel> Channels => _channels;

        // 保存各通道的 LibVLC 回调委托引用，防止被 GC 提前回收导致 native crash
        private readonly ConcurrentDictionary<long, (MediaPlayer.LibVLCAudioPlayCb Play, MediaPlayer.LibVLCAudioPauseCb Pause, MediaPlayer.LibVLCAudioResumeCb Resume, MediaPlayer.LibVLCAudioFlushCb Flush, MediaPlayer.LibVLCAudioDrainCb Drain)> _vlcCallbacks = new();

        private IWavePlayer? _outputDevice;
        private readonly object _outputLock = new();
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
            EnsureOutputRunning();

            var channel = _channels.GetOrAdd(roomId, id =>
            {
                var newChan = new SpatialAudioChannel(id, title, uid);
                AutoAssignInitialPosition(newChan);
                return newChan;
            });

            channel.Title = title;
            channel.UID = uid;

            // 构造 LibVLC S16N 回调并强引用持有
            MediaPlayer.LibVLCAudioPlayCb playCb = (data, samples, count, pts) =>
            {
                int sampleCount = (int)(count * 2);
                channel.Buffer.WriteS16(samples, sampleCount);
            };

            MediaPlayer.LibVLCAudioPauseCb pauseCb = (data, pts) => { };
            MediaPlayer.LibVLCAudioResumeCb resumeCb = (data, pts) => { };
            MediaPlayer.LibVLCAudioFlushCb flushCb = (data, pts) =>
            {
                channel.Buffer.Clear();
            };
            MediaPlayer.LibVLCAudioDrainCb drainCb = (data) => { };

            _vlcCallbacks[roomId] = (playCb, pauseCb, resumeCb, flushCb, drainCb);

            // 重要：LibVLC 规范中必须先挂载回调 SetAudioCallbacks，再调用 SetAudioFormat 指定 S16N
            player.SetAudioCallbacks(playCb, pauseCb, resumeCb, flushCb, drainCb);
            player.SetAudioFormat("S16N", 44100, 2);

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

            if (_channels.IsEmpty)
            {
                StopOutputIfIdle();
            }
        }

        private void AutoAssignInitialPosition(SpatialAudioChannel newChan)
        {
            int count = _channels.Count;
            if (count == 0)
            {
                newChan.Azimuth = 0f;
                newChan.Distance = 1.0f;
            }
            else
            {
                float[] presets = { -35f, 35f, -60f, 60f, -15f, 15f, -80f, 80f, 0f };
                int idx = (count - 1) % presets.Length;
                newChan.Azimuth = presets[idx];
                newChan.Distance = 1.0f + (idx * 0.05f);
            }
        }

        private void EnsureOutputRunning()
        {
            lock (_outputLock)
            {
                if (_isOutputRunning && _outputDevice != null) return;

                // 优先尝试 WASAPI 输出，若系统声卡或驱动不支持则平滑降级到 100% 兼容的 WaveOut
                try
                {
                    var wasapi = new WasapiOut(AudioClientShareMode.Shared, 50);
                    wasapi.Init(this);
                    wasapi.Play();
                    _outputDevice = wasapi;
                    _isOutputRunning = true;
                    Log.Info(nameof(SpatialAudioEngine), "WASAPI 空间混音输出引擎启动成功 (共享模式，50ms 延迟)");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warn(nameof(SpatialAudioEngine), "WASAPI 模式启动未成功，尝试回退至 WaveOut 输出引擎", ex);
                }

                try
                {
                    var waveOut = new WaveOut();
                    waveOut.Init(this);
                    waveOut.Play();
                    _outputDevice = waveOut;
                    _isOutputRunning = true;
                    Log.Info(nameof(SpatialAudioEngine), "WaveOut 混音输出引擎启动成功 (Windows 通用模式)");
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(SpatialAudioEngine), "音频输出引擎全部初始化失败，请检查默认音频输出设备", ex);
                }
            }
        }

        private void StopOutputIfIdle()
        {
            lock (_outputLock)
            {
                if (_outputDevice != null && _channels.IsEmpty)
                {
                    try
                    {
                        _outputDevice.Stop();
                        _outputDevice.Dispose();
                        _outputDevice = null;
                        _isOutputRunning = false;
                        Log.Info(nameof(SpatialAudioEngine), "所有空间音频通道已关闭，混音输出引擎已休眠");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(nameof(SpatialAudioEngine), "关闭音频输出设备异常", ex);
                    }
                }
            }
        }

        /// <summary>
        /// 音频数据消费混音主逻辑（运行在高优先级系统音频渲染线程）
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);

            float currentHeadYaw = _listenerHeadYaw;
            bool enabled = _isEnabled;

            foreach (var kv in _channels)
            {
                var channel = kv.Value;
                channel.MixTo(buffer, offset, count, currentHeadYaw, enabled);
            }

            // 软限制器 (防止多路混音累加导致破音削波)
            for (int i = 0; i < count; i++)
            {
                int idx = offset + i;
                float s = buffer[idx];

                if (float.IsNaN(s) || float.IsInfinity(s))
                {
                    buffer[idx] = 0f;
                    continue;
                }

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
            float[] temp = System.Buffers.ArrayPool<float>.Shared.Rent(buffer.Length);
            try
            {
                int read = Read(temp, 0, buffer.Length);
                temp.AsSpan(0, read).CopyTo(buffer);
                return read;
            }
            finally
            {
                System.Buffers.ArrayPool<float>.Shared.Return(temp);
            }
        }

        private static float NormalizeAngle(float angle)
        {
            if (float.IsNaN(angle) || float.IsInfinity(angle)) return 0f;
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
