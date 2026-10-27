using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Desktop.Services.SpatialAudio
{
    /// <summary>
    /// 高性能单生产者单消费者（SPSC）音频浮点环形缓冲区
    /// 用于解耦 LibVLC 解码推流线程与 WASAPI 音频渲染线程
    /// </summary>
    public sealed class CircularAudioBuffer
    {
        private readonly float[] _buffer;
        private readonly int _capacity;
        private int _writePos;
        private int _readPos;

        // 最大允许堆积的采样点数（48000Hz 立体声下，150ms 约为 14400 个 float）
        // 超过此阈值自动快进丢弃旧帧，保证音视频严格同步，防止直播流累积延迟
        private readonly int _maxLagSamples;

        public CircularAudioBuffer(int capacity = 192000, int maxLagSamples = 14400)
        {
            _capacity = capacity;
            _maxLagSamples = maxLagSamples;
            _buffer = new float[capacity];
            _writePos = 0;
            _readPos = 0;
        }

        public int AvailableRead
        {
            get
            {
                int w = Volatile.Read(ref _writePos);
                int r = Volatile.Read(ref _readPos);
                int count = w - r;
                if (count < 0) count += _capacity;
                return count;
            }
        }

        public int AvailableWrite => _capacity - 1 - AvailableRead;

        /// <summary>
        /// 从 LibVLC 的 native 内存指针直接写入 PCM float 数据
        /// </summary>
        public void Write(IntPtr pSamples, int floatCount)
        {
            if (pSamples == IntPtr.Zero || floatCount <= 0) return;

            // 检查当前累积延迟，若超过最大允许延迟，则跳过部分旧数据进行追帧
            int currentAvailable = AvailableRead;
            if (currentAvailable > _maxLagSamples)
            {
                int dropCount = currentAvailable - (_maxLagSamples / 2);
                // 确保按立体声帧对齐 (2 个 float 一帧)
                dropCount &= ~1;
                AdvanceRead(dropCount);
            }

            int space = AvailableWrite;
            if (floatCount > space)
            {
                // 空间不足，只写入能容纳的最大帧数（按 2 对齐）
                floatCount = space & ~1;
            }

            if (floatCount <= 0) return;

            int w = _writePos;
            int firstPart = Math.Min(floatCount, _capacity - w);
            int secondPart = floatCount - firstPart;

            Marshal.Copy(pSamples, _buffer, w, firstPart);

            if (secondPart > 0)
            {
                IntPtr secondPtr = IntPtr.Add(pSamples, firstPart * sizeof(float));
                Marshal.Copy(secondPtr, _buffer, 0, secondPart);
                _writePos = secondPart;
            }
            else
            {
                _writePos = (w + firstPart) % _capacity;
            }
        }

        /// <summary>
        /// 读取 PCM float 数据至目标数组，不足部分补零
        /// </summary>
        public int Read(float[] destination, int offset, int count)
        {
            if (destination == null || count <= 0) return 0;

            int available = AvailableRead;
            int toRead = Math.Min(available, count);

            if (toRead > 0)
            {
                int r = _readPos;
                int firstPart = Math.Min(toRead, _capacity - r);
                int secondPart = toRead - firstPart;

                Array.Copy(_buffer, r, destination, offset, firstPart);

                if (secondPart > 0)
                {
                    Array.Copy(_buffer, 0, destination, offset + firstPart, secondPart);
                    _readPos = secondPart;
                }
                else
                {
                    _readPos = (r + firstPart) % _capacity;
                }
            }

            // 若可用数据不足，剩余部分补 0 静音，防止产生爆音
            if (toRead < count)
            {
                Array.Clear(destination, offset + toRead, count - toRead);
            }

            return toRead;
        }

        public void Clear()
        {
            _writePos = 0;
            _readPos = 0;
        }

        private void AdvanceRead(int count)
        {
            int r = _readPos;
            _readPos = (r + count) % _capacity;
        }
    }
}
