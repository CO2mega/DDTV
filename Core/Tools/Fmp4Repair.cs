using Core.LogModule;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Core.Tools
{
    /// <summary>
    /// fMP4 文件结构检测与修复工具
    /// 用于修复 DDTV HLS 录制过程中产生的结构断裂问题
    /// </summary>
    public static class Fmp4Repair
    {
        /// <summary>
        /// 检测 fMP4 文件是否存在结构断裂
        /// </summary>
        /// <param name="filePath">待检测的文件路径</param>
        /// <returns>true 表示存在断裂，false 表示结构完整</returns>
        public static bool DetectCorruption(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    long fileLen = fs.Length;
                    long offset = 0;

                    while (offset < fileLen - 8)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                        byte[] header = new byte[8];
                        int read = fs.Read(header, 0, 8);
                        if (read < 8) break;

                        uint size = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
                        string type = Encoding.ASCII.GetString(header, 4, 4);

                        if (size == 0)
                        {
                            size = (uint)(fileLen - offset);
                        }
                        else if (size == 1)
                        {
                            if (offset + 16 <= fileLen)
                            {
                                fs.Seek(offset + 8, SeekOrigin.Begin);
                                byte[] extSize = new byte[8];
                                fs.Read(extSize, 0, 8);
                                size = (uint)((extSize[0] << 24) | (extSize[1] << 16) | (extSize[2] << 8) | extSize[3]);
                            }
                            else break;
                        }

                        if (size < 8 || size > fileLen - offset)
                        {
                            // 尝试在后续 5MB 范围内恢复
                            long searchEnd = Math.Min(offset + 5000000, fileLen - 8);
                            for (long searchPos = offset + 1; searchPos < searchEnd; searchPos++)
                            {
                                fs.Seek(searchPos, SeekOrigin.Begin);
                                byte[] sig = new byte[4];
                                fs.Read(sig, 0, 4);
                                if (sig[0] == 'm' && sig[1] == 'o' && sig[2] == 'o' && sig[3] == 'f' ||
                                    sig[0] == 'm' && sig[1] == 'd' && sig[2] == 'a' && sig[3] == 't')
                                {
                                    long candidateOffset = searchPos - 4;
                                    if (candidateOffset >= offset && candidateOffset + 8 <= fileLen)
                                    {
                                        fs.Seek(candidateOffset, SeekOrigin.Begin);
                                        byte[] sizeBytes = new byte[4];
                                        fs.Read(sizeBytes, 0, 4);
                                        uint candidateSize = (uint)((sizeBytes[0] << 24) | (sizeBytes[1] << 16) | (sizeBytes[2] << 8) | sizeBytes[3]);
                                        if (candidateSize >= 8 && candidateSize <= fileLen - candidateOffset)
                                        {
                                            fs.Seek(candidateOffset + 4, SeekOrigin.Begin);
                                            byte[] typeBytes = new byte[4];
                                            fs.Read(typeBytes, 0, 4);
                                            if (typeBytes[0] == sig[0] && typeBytes[1] == sig[1] && typeBytes[2] == sig[2] && typeBytes[3] == sig[3])
                                            {
                                                Log.Info(nameof(DetectCorruption), $"检测到 fMP4 结构断裂: 文件={filePath}, 断裂偏移={offset}, 恢复偏移={candidateOffset}, 垃圾数据大小={candidateOffset - offset}");
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                            // 无法恢复，结构结束
                            break;
                        }

                        offset += (long)size;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(nameof(DetectCorruption), $"检测 fMP4 文件时发生错误: {filePath}", ex);
            }

            return false;
        }

        /// <summary>
        /// 修复 fMP4 文件结构断裂，提取所有有效的 moof+mdat 对重新封装
        /// </summary>
        /// <param name="inputPath">输入的损坏文件路径</param>
        /// <param name="outputPath">输出的修复后文件路径</param>
        /// <returns>true 表示修复成功，false 表示修复失败</returns>
        public static bool RepairStructure(string inputPath, string outputPath)
        {
            try
            {
                using (var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
                using (var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    long fileLen = fs.Length;
                    long offset = 0;
                    var boxes = new List<(long offset, string type, long size)>();
                    int recoveryCount = 0;

                    // 第一步：解析所有 top-level box
                    while (offset < fileLen - 8)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                        byte[] header = new byte[8];
                        int read = fs.Read(header, 0, 8);
                        if (read < 8) break;

                        uint size = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
                        string type = Encoding.ASCII.GetString(header, 4, 4);

                        if (size == 0)
                        {
                            size = (uint)(fileLen - offset);
                        }
                        else if (size == 1)
                        {
                            if (offset + 16 <= fileLen)
                            {
                                fs.Seek(offset + 8, SeekOrigin.Begin);
                                byte[] extSize = new byte[8];
                                fs.Read(extSize, 0, 8);
                                size = (uint)((extSize[0] << 24) | (extSize[1] << 16) | (extSize[2] << 8) | extSize[3]);
                            }
                            else break;
                        }

                        if (size < 8 || size > fileLen - offset)
                        {
                            // 尝试恢复
                            bool found = false;
                            long searchEnd = Math.Min(offset + 5000000, fileLen - 8);
                            for (long searchPos = offset + 1; searchPos < searchEnd; searchPos++)
                            {
                                fs.Seek(searchPos, SeekOrigin.Begin);
                                byte[] sig = new byte[4];
                                fs.Read(sig, 0, 4);
                                if ((sig[0] == 'm' && sig[1] == 'o' && sig[2] == 'o' && sig[3] == 'f') ||
                                    (sig[0] == 'm' && sig[1] == 'd' && sig[2] == 'a' && sig[3] == 't'))
                                {
                                    long candidateOffset = searchPos - 4;
                                    if (candidateOffset >= offset && candidateOffset + 8 <= fileLen)
                                    {
                                        fs.Seek(candidateOffset, SeekOrigin.Begin);
                                        byte[] sizeBytes = new byte[4];
                                        fs.Read(sizeBytes, 0, 4);
                                        uint candidateSize = (uint)((sizeBytes[0] << 24) | (sizeBytes[1] << 16) | (sizeBytes[2] << 8) | sizeBytes[3]);
                                        if (candidateSize >= 8 && candidateSize <= fileLen - candidateOffset)
                                        {
                                            fs.Seek(candidateOffset + 4, SeekOrigin.Begin);
                                            byte[] typeBytes = new byte[4];
                                            fs.Read(typeBytes, 0, 4);
                                            if (typeBytes[0] == sig[0] && typeBytes[1] == sig[1] && typeBytes[2] == sig[2] && typeBytes[3] == sig[3])
                                            {
                                                recoveryCount++;
                                                offset = candidateOffset;
                                                size = candidateSize;
                                                type = Encoding.ASCII.GetString(sig, 0, 4);
                                                found = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            if (!found) break;
                        }

                        boxes.Add((offset, type, (long)size));
                        offset += (long)size;
                    }

                    // 第二步：提取 init segment (ftyp + moov)
                    byte[] initData = new byte[0];
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        if (boxes[i].type == "ftyp" || boxes[i].type == "moov")
                        {
                            byte[] boxData = new byte[boxes[i].size];
                            fs.Seek(boxes[i].offset, SeekOrigin.Begin);
                            fs.Read(boxData, 0, boxData.Length);
                            byte[] newInit = new byte[initData.Length + boxData.Length];
                            Buffer.BlockCopy(initData, 0, newInit, 0, initData.Length);
                            Buffer.BlockCopy(boxData, 0, newInit, initData.Length, boxData.Length);
                            initData = newInit;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (initData.Length == 0)
                    {
                        Log.Warn(nameof(RepairStructure), $"未找到 ftyp/moov init segment: {inputPath}");
                        return false;
                    }

                    outFs.Write(initData, 0, initData.Length);

                    // 第三步：提取所有 moof+mdat 对
                    int pairCount = 0;
                    for (int i = 0; i < boxes.Count - 1; i++)
                    {
                        if (boxes[i].type == "moof" && boxes[i + 1].type == "mdat")
                        {
                            // 写入 moof
                            byte[] moofData = new byte[boxes[i].size];
                            fs.Seek(boxes[i].offset, SeekOrigin.Begin);
                            fs.Read(moofData, 0, moofData.Length);
                            outFs.Write(moofData, 0, moofData.Length);

                            // 写入 mdat
                            byte[] mdatData = new byte[boxes[i + 1].size];
                            fs.Seek(boxes[i + 1].offset, SeekOrigin.Begin);
                            fs.Read(mdatData, 0, mdatData.Length);
                            outFs.Write(mdatData, 0, mdatData.Length);

                            pairCount++;
                            i++; // 跳过 mdat，因为已经处理了
                        }
                    }

                    Log.Info(nameof(RepairStructure), $"fMP4 结构修复完成: 输入={inputPath}, 输出={outputPath}, 跳过断裂点={recoveryCount}, 有效片段对={pairCount}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(nameof(RepairStructure), $"修复 fMP4 文件时发生错误: {inputPath}", ex);
                return false;
            }
        }
    }
}
