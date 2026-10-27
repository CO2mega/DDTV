using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Desktop.Services.SpatialAudio
{
    /// <summary>
    /// 高性能单生产者单消费者（SPSC）音频环形缓冲区
    /// 将 LibVLC 解码出的 S16N 16位整数 PCM 转换为浮点音频并解耦推流线程与渲染线程
    /// </summary>
    public sealed class CircularAudioBuffer
    {
        private readonly float[] _buffer;
        private readonly int _capacity;
        private int _writePos;
        private int _readPos;

        // 复用中转缓存，避免 GC 内存分配
        private short[] _shortChunk = new short[8192];

        // 最大允许延迟采样数（44100Hz 立体声下，300ms 约为 26460 个浮点采样）
        // 超过此阈值自动平滑追帧，保证音视频严格同步
        private readonly int _maxLagSamples;

        public CircularAudioBuffer(int capacity = 176400, int maxLagSamples = 26460)
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
        /// 从 LibVLC 的 native S16N (16-bit PCM short) 指针读取并转换为标准 [-1.0, 1.0] 浮点写入缓冲
        /// </summary>
        public void WriteS16(IntPtr pSamples, int sampleCount)
        {
            if (pSamples == IntPtr.Zero || sampleCount <= 0) return;

            // 追帧保护：若累积延迟过大，快进追帧
            int currentAvailable = AvailableRead;
            if (currentAvailable > _maxLagSamples)
            {
                int dropCount = currentAvailable - (_maxLagSamples / 2);
                dropCount &= ~1;
                AdvanceRead(dropCount);
            }

            int space = AvailableWrite;
            if (sampleCount > space)
            {
                sampleCount = space & ~1;
            }
            if (sampleCount <= 0) return;

            if (_shortChunk.Length < sampleCount)
            {
                _shortChunk = new short[sampleCount];
            }

            Marshal.Copy(pSamples, _shortChunk, 0, sampleCount);

            int w = Volatile.Read(ref _writePos);
            const float scale = 1.0f / 32768.0f;

            for (int i = 0; i < sampleCount; i++)
            {
                _buffer[w] = _shortChunk[i] * scale;
                w++;
                if (w >= _capacity) w = 0;
            }

            Volatile.Write(ref _writePos, w);
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
                int r = Volatile.Read(ref _readPos);
                int firstPart = Math.Min(toRead, _capacity - r);
                int secondPart = toRead - firstPart;

                Array.Copy(_buffer, r, destination, offset, firstPart);

                if (secondPart > 0)
                {
                    Array.Copy(_buffer, 0, destination, offset + firstPart, secondPart);
                    Volatile.Write(ref _readPos, secondPart);
                }
                else
                {
                    Volatile.Write(ref _readPos, (r + firstPart) % _capacity);
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
            Volatile.Write(ref _writePos, 0);
            Volatile.Write(ref _readPos, 0);
        }

        private void AdvanceRead(int count)
        {
            int r = Volatile.Read(ref _readPos);
            Volatile.Write(ref _readPos, (r + count) % _capacity);
        }
    }
}
