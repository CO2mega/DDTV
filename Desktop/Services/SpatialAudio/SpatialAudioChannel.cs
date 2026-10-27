using System;

namespace Desktop.Services.SpatialAudio
{
    /// <summary>
    /// 单个播放器/直播间音频空间化处理通道
    /// 包含独立环形缓冲区、ITD（双耳时间差）、ILD（双耳声级差）、头影滤波与后方耳廓模拟
    /// </summary>
    public sealed class SpatialAudioChannel
    {
        public long RoomId { get; set; }
        public long UID { get; set; }
        public string Title { get; set; } = string.Empty;

        // 空间极坐标位置
        // 方位角 Azimuth: -180° ~ +180° (0° 为正前方，+90° 为正右方，-90° 为正左方，±180° 为正后方)
        public float Azimuth { get; set; } = 0f;

        // 虚拟距离 Distance: 0.2m ~ 3.0m (标准距离为 1.0m)
        public float Distance { get; set; } = 1.0f;

        // 通道音量与静音
        public float Volume { get; set; } = 1.0f;
        public bool IsMuted { get; set; } = false;

        // 音频数据环形缓冲
        public CircularAudioBuffer Buffer { get; } = new(capacity: 192000, maxLagSamples: 14400);

        // DSP 延迟线（最大支持 64 个采样点延迟，48kHz 下约为 1.33ms，足够模拟成人头部半径）
        private const int DelayBufferSize = 128;
        private readonly float[] _delayL = new float[DelayBufferSize];
        private readonly float[] _delayR = new float[DelayBufferSize];
        private int _delayIndex = 0;

        // 头影滤波器状态（1 阶 IIR 低通）
        private float _filterStateL = 0f;
        private float _filterStateR = 0f;

        // 平滑过渡变量，消除参数快速拖动时的拉链音和爆音
        private float _currentGainL = 0.707f;
        private float _currentGainR = 0.707f;
        private float _currentDelayL = 0f;
        private float _currentDelayR = 0f;
        private float _currentFilterAlphaL = 1.0f;
        private float _currentFilterAlphaR = 1.0f;

        // 临时读取缓存
        private readonly float[] _readChunk = new float[2048];

        public SpatialAudioChannel(long roomId, string title, long uid)
        {
            RoomId = roomId;
            Title = title;
            UID = uid;
        }

        /// <summary>
        /// 处理并累加混音至主输出缓冲区
        /// </summary>
        /// <param name="masterBuffer">主混音缓冲区（交错立体声 L, R, L, R...）</param>
        /// <param name="offset">写入起始偏移</param>
        /// <param name="count">浮点采样总数 (需为 2 的倍数)</param>
        /// <param name="listenerHeadYaw">人头当前朝向角（-180° ~ +180°）</param>
        /// <param name="spatialEnabled">是否启用空间音频</param>
        public void MixTo(float[] masterBuffer, int offset, int count, float listenerHeadYaw, bool spatialEnabled)
        {
            if (IsMuted || Volume <= 0.001f)
            {
                // 静音时仍需从环形缓冲消耗数据，避免积压延迟
                DiscardSamples(count);
                return;
            }

            int framesToProcess = count / 2;
            int processedFrames = 0;

            while (processedFrames < framesToProcess)
            {
                int chunkFrames = Math.Min(framesToProcess - processedFrames, _readChunk.Length / 2);
                int chunkCount = chunkFrames * 2;

                Buffer.Read(_readChunk, 0, chunkCount);

                if (!spatialEnabled)
                {
                    // 未开启空间音频时直通立体声输出
                    float vol = Volume;
                    int outIdx = offset + (processedFrames * 2);
                    for (int i = 0; i < chunkCount; i += 2)
                    {
                        masterBuffer[outIdx + i] += _readChunk[i] * vol;
                        masterBuffer[outIdx + i + 1] += _readChunk[i + 1] * vol;
                    }
                }
                else
                {
                    // 开启空间音频：计算相对于听者朝向的相对方位角
                    // relativeAngle: 0° 为正前方，+90° 为听者右耳方向，-90° 为听者左耳方向
                    float relativeAngleDeg = NormalizeAngle(Azimuth - listenerHeadYaw);
                    float rad = relativeAngleDeg * (MathF.PI / 180f);

                    // 1. 距离衰减 (Inverse Distance Law 优化模型)
                    float distClamped = Math.Clamp(Distance, 0.2f, 3.0f);
                    float distGain = 1.0f / (1.0f + 0.6f * (distClamped - 0.5f));

                    // 2. 双耳声级差 (ILD - Sine Pan Law)
                    float sinAngle = Math.Clamp(MathF.Sin(rad), -1.0f, 1.0f);
                    float cosAngle = Math.Clamp(MathF.Cos(rad), -1.0f, 1.0f);

                    float targetGainL = MathF.Sqrt(Math.Max(0f, 0.5f * (1.0f - sinAngle))) * distGain * Volume;
                    float targetGainR = MathF.Sqrt(Math.Max(0f, 0.5f * (1.0f + sinAngle))) * distGain * Volume;

                    // 后方耳廓衰减：若声音在后半球 (cosAngle < 0)，进行轻微衰减（最大衰减约 15%）
                    if (cosAngle < 0f)
                    {
                        float rearDamp = 1.0f + 0.15f * cosAngle;
                        targetGainL *= rearDamp;
                        targetGainR *= rearDamp;
                    }

                    // 3. 双耳时间差 (ITD - Woodworth 球形头部模型近似)
                    // 48kHz 下最大头部跨越延迟约 28 个采样点 (约 0.6ms)
                    const float maxDelaySamples = 28f;
                    float targetDelayL = sinAngle > 0f ? Math.Clamp(sinAngle * maxDelaySamples, 0f, maxDelaySamples) : 0f;
                    float targetDelayR = sinAngle < 0f ? Math.Clamp(-sinAngle * maxDelaySamples, 0f, maxDelaySamples) : 0f;

                    // 4. 头影低通滤波系数 (被头部遮挡的远端耳朵高频被吸收)
                    // alpha = 1.0 表示不过滤；alpha 降低表示截止频率降低
                    float targetAlphaL = sinAngle > 0f ? Math.Max(0.25f, 1.0f - 0.7f * sinAngle) : 1.0f;
                    float targetAlphaR = sinAngle < 0f ? Math.Max(0.25f, 1.0f + 0.7f * sinAngle) : 1.0f;

                    if (cosAngle < 0f)
                    {
                        // 声音在正后方时，耳廓遮蔽导致双耳高频都有轻微吸收
                        targetAlphaL *= 0.85f;
                        targetAlphaR *= 0.85f;
                    }

                    // 防御性数值校验，防止任何 NaN 污染 DSP 内部状态
                    if (float.IsNaN(targetGainL)) targetGainL = 0.707f;
                    if (float.IsNaN(targetGainR)) targetGainR = 0.707f;
                    if (float.IsNaN(targetDelayL)) targetDelayL = 0f;
                    if (float.IsNaN(targetDelayR)) targetDelayR = 0f;
                    if (float.IsNaN(targetAlphaL)) targetAlphaL = 1.0f;
                    if (float.IsNaN(targetAlphaR)) targetAlphaR = 1.0f;

                    int outIdx = offset + (processedFrames * 2);

                    for (int i = 0; i < chunkCount; i += 2)
                    {
                        // 参数平滑插值 (防止拖动小球/旋转人头时出现破音)
                        _currentGainL += (targetGainL - _currentGainL) * 0.015f;
                        _currentGainR += (targetGainR - _currentGainR) * 0.015f;
                        _currentDelayL += (targetDelayL - _currentDelayL) * 0.015f;
                        _currentDelayR += (targetDelayR - _currentDelayR) * 0.015f;
                        _currentFilterAlphaL += (targetAlphaL - _currentFilterAlphaL) * 0.015f;
                        _currentFilterAlphaR += (targetAlphaR - _currentFilterAlphaR) * 0.015f;

                        // 提取左右输入并保留原有部分立体声宽度
                        float inL = _readChunk[i];
                        float inR = _readChunk[i + 1];
                        float srcL = 0.75f * inL + 0.25f * inR;
                        float srcR = 0.25f * inL + 0.75f * inR;

                        // 存入 ITD 循环延迟线
                        _delayL[_delayIndex] = srcL;
                        _delayR[_delayIndex] = srcR;

                        // 双线性插值读取带延迟的声音
                        float delayedL = ReadInterpolatedDelay(_delayL, _delayIndex, _currentDelayL);
                        float delayedR = ReadInterpolatedDelay(_delayR, _delayIndex, _currentDelayR);

                        _delayIndex = (_delayIndex + 1) % DelayBufferSize;

                        // 头影低通滤波 (1-pole IIR: y[n] = y[n-1] + alpha * (x[n] - y[n-1]))
                        _filterStateL += _currentFilterAlphaL * (delayedL - _filterStateL);
                        _filterStateR += _currentFilterAlphaR * (delayedR - _filterStateR);

                        if (float.IsNaN(_filterStateL) || float.IsInfinity(_filterStateL)) _filterStateL = 0f;
                        if (float.IsNaN(_filterStateR) || float.IsInfinity(_filterStateR)) _filterStateR = 0f;

                        // 应用最终 ILD 声级增益并累加至主输出
                        masterBuffer[outIdx + i] += _filterStateL * _currentGainL;
                        masterBuffer[outIdx + i + 1] += _filterStateR * _currentGainR;
                    }
                }

                processedFrames += chunkFrames;
            }
        }

        private static float ReadInterpolatedDelay(float[] buffer, int currentIndex, float delay)
        {
            if (float.IsNaN(delay) || float.IsInfinity(delay) || delay < 0f) delay = 0f;
            else if (delay > DelayBufferSize - 2) delay = DelayBufferSize - 2;

            float readPos = (float)currentIndex - delay;
            while (readPos < 0f) readPos += DelayBufferSize;
            while (readPos >= DelayBufferSize) readPos -= DelayBufferSize;

            int index0 = (int)readPos;
            if ((uint)index0 >= (uint)DelayBufferSize) index0 = 0;
            int index1 = (index0 + 1) % DelayBufferSize;
            float frac = Math.Clamp(readPos - index0, 0f, 1f);

            return buffer[index0] + frac * (buffer[index1] - buffer[index0]);
        }

        private void DiscardSamples(int count)
        {
            int remaining = count;
            while (remaining > 0)
            {
                int toRead = Math.Min(remaining, _readChunk.Length);
                int read = Buffer.Read(_readChunk, 0, toRead);
                if (read <= 0) break;
                remaining -= read;
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
