using Core.LogModule;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Core.RuntimeObject.Download.Basics;

namespace Core.RuntimeObject.Download
{
    public class FLV
    {
        /// <summary>
        /// 录制flv_avc制式的flv文件
        /// </summary>
        /// <param name="card">房间卡片信息</param>
        /// <returns>[TaskStatus]任务状态；[FileName]下载成功的文件名</returns>
        public static async Task<(DownloadTaskState hlsState, string FileName)> DlwnloadHls_avc_flv(RoomCardClass card)
        {
            DownloadTaskState hlsState = DownloadTaskState.Default;
            string File = string.Empty;
            await Task.Run(async () =>
            {
                InitializeDownload(card, RoomCardClass.TaskType.FLV_AVC);
                long roomId = card.RoomId;
                File = $"{Config.Core_RunConfig._RecFileDirectory}{Core.Tools.KeyCharacterReplacement.ReplaceKeyword($"{Config.Core_RunConfig._DefaultLiverFolderName}/{Core.Config.Core_RunConfig._DefaultDataFolderName}{(string.IsNullOrEmpty(Core.Config.Core_RunConfig._DefaultDataFolderName) ? "" : "/")}{Config.Core_RunConfig._DefaultFileName}", DateTime.Now, card.UID)}_original.flv";
                card.DownInfo.DownloadFileList.CurrentOperationVideoFile = string.Empty;
                CreateDirectoryIfNotExists(File.Substring(0, File.LastIndexOf('/')));
                Thread.Sleep(5);
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();
                long DownloadFileSizeForThisTask = 0;
                long startLiveTime = card.live_time.Value;
                List<(long size, DateTime time)> speedValues = new();

                void UpdateSpeed(long downloadSizeForThisCycle)
                {
                    speedValues.Add((downloadSizeForThisCycle, DateTime.Now));
                    while (speedValues.Count >= 10)
                    {
                        speedValues.RemoveAt(0);
                    }
                    if (speedValues.Count > 1)
                    {
                        card.DownInfo.RealTimeDownloadSpe = (speedValues.Sum(x => x.size) / DateTime.Now.Subtract(speedValues[0].time).TotalMilliseconds) * 1000;
                    }
                    else
                    {
                        card.DownInfo.RealTimeDownloadSpe = 0;
                    }
                    card.DownInfo.DownloadSize += downloadSizeForThisCycle;
                }

                using var client = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                    AllowAutoRedirect = true,
                })
                {
                    Timeout = TimeSpan.FromSeconds(100),
                };
                client.DefaultRequestHeaders.Add("Accept", "*/*");
                client.DefaultRequestHeaders.Add("Referer", "https://www.bilibili.com/");
                client.DefaultRequestHeaders.Add("User-Agent", Config.Core_RunConfig._HTTP_UA);
                if (RuntimeObject.Account.AccountInformation.State)
                {
                    client.DefaultRequestHeaders.Add("Cookie", RuntimeObject.Account.AccountInformation.strCookies);
                }

                HostClass hostClass = GetFlvHost_avc(card);
                string DlwnloadURL = $"{hostClass.host}{hostClass.base_url}{hostClass.uri_name}{hostClass.extra}";
                string F_S = Config.Core_RunConfig._RecFileDirectory + (Config.Core_RunConfig._RecFileDirectory.EndsWith("/") || Config.Core_RunConfig._RecFileDirectory.EndsWith("\\") ? "" : "/") + File.Replace(Config.Core_RunConfig._RecFileDirectory, "").Replace("\\", "/");
                card.DownInfo.DownloadFileList.CurrentOperationVideoFile = F_S;
                LogDownloadStart(card, "FLV");

                int retryCount = 0;
                const int maxRetries = 3;

                using (FileStream fs = new FileStream(File, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    while (retryCount < maxRetries)
                    {
                        try
                        {
                            using var request = new HttpRequestMessage(HttpMethod.Get, DlwnloadURL);
                            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                            response.EnsureSuccessStatusCode();
                            using var stream = await response.Content.ReadAsStreamAsync();
                            byte[] buffer = new byte[81920];

                            while (true)
                            {
                                if (card.DownInfo.Unmark || card.DownInfo.IsCut || card.live_time.Value != startLiveTime)
                                {
                                    hlsState = CheckAndHandleFile(File, ref card, card.live_time.Value != startLiveTime);
                                    return;
                                }

                                if (card.RoomCutAccordingToSize > 0 && DownloadFileSizeForThisTask > card.RoomCutAccordingToSize)
                                {
                                    Log.Info(nameof(DlwnloadHls_avc_flv), $"{card.Name}({card.RoomId})触发房间文件大小分割");
                                    hlsState = DownloadTaskState.Success;
                                    return;
                                }
                                if (card.RoomCutAccordingToSize == 0 && Config.Core_RunConfig._CutAccordingToSize > 0 && DownloadFileSizeForThisTask > Config.Core_RunConfig._CutAccordingToSize)
                                {
                                    Log.Info(nameof(DlwnloadHls_avc_flv), $"{card.Name}({card.RoomId})触发全局文件大小分割");
                                    hlsState = DownloadTaskState.Success;
                                    return;
                                }
                                if (card.RoomCutAccordingToTime > 0 && stopWatch.Elapsed.TotalSeconds > card.RoomCutAccordingToTime)
                                {
                                    Log.Info(nameof(DlwnloadHls_avc_flv), $"{card.Name}({card.RoomId})触发房间时间分割");
                                    hlsState = DownloadTaskState.Success;
                                    return;
                                }
                                if (card.RoomCutAccordingToTime == 0 && Config.Core_RunConfig._CutAccordingToTime > 0 && stopWatch.Elapsed.TotalSeconds > Config.Core_RunConfig._CutAccordingToTime)
                                {
                                    Log.Info(nameof(DlwnloadHls_avc_flv), $"{card.Name}({card.RoomId})触发全局时间分割");
                                    hlsState = DownloadTaskState.Success;
                                    return;
                                }

                                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0)
                                {
                                    break;
                                }

                                await fs.WriteAsync(buffer, 0, read);
                                DownloadFileSizeForThisTask += read;
                                UpdateSpeed(read);
                            }

                            if (!RoomInfo.GetLiveStatus(card.RoomId))
                            {
                                hlsState = DownloadTaskState.StopLive;
                                return;
                            }

                            retryCount++;
                            if (retryCount < maxRetries)
                            {
                                int delayMs = (int)Math.Pow(2, retryCount) * 1000;
                                Log.Info(nameof(DlwnloadHls_avc_flv), $"[{card.Name}({card.RoomId})]FLV流意外中断，{delayMs}ms后第{retryCount}次重试");
                                Thread.Sleep(delayMs);
                                hostClass = GetFlvHost_avc(card);
                                DlwnloadURL = $"{hostClass.host}{hostClass.base_url}{hostClass.uri_name}{hostClass.extra}";
                                continue;
                            }
                        }
                        catch (HttpRequestException ex)
                        {
                            retryCount++;
                            if (retryCount < maxRetries)
                            {
                                int delayMs = (int)Math.Pow(2, retryCount) * 1000;
                                Log.Warn(nameof(DlwnloadHls_avc_flv), $"[{card.Name}({card.RoomId})]FLV下载HTTP错误，{delayMs}ms后第{retryCount}次重试：{ex.Message}");
                                Thread.Sleep(delayMs);
                                hostClass = GetFlvHost_avc(card);
                                DlwnloadURL = $"{hostClass.host}{hostClass.base_url}{hostClass.uri_name}{hostClass.extra}";
                                continue;
                            }
                            Log.Error(nameof(DlwnloadHls_avc_flv), $"[{card.Name}({card.RoomId})]FLV下载HTTP错误，重试耗尽", ex);
                            break;
                        }
                        catch (IOException ex)
                        {
                            retryCount++;
                            if (retryCount < maxRetries)
                            {
                                int delayMs = (int)Math.Pow(2, retryCount) * 1000;
                                Log.Warn(nameof(DlwnloadHls_avc_flv), $"[{card.Name}({card.RoomId})]FLV下载IO错误，{delayMs}ms后第{retryCount}次重试：{ex.Message}");
                                Thread.Sleep(delayMs);
                                hostClass = GetFlvHost_avc(card);
                                DlwnloadURL = $"{hostClass.host}{hostClass.base_url}{hostClass.uri_name}{hostClass.extra}";
                                continue;
                            }
                            Log.Error(nameof(DlwnloadHls_avc_flv), $"[{card.Name}({card.RoomId})]FLV下载IO错误，重试耗尽", ex);
                            break;
                        }
                        catch (TaskCanceledException ex) when (ex.CancellationToken == default || !ex.CancellationToken.IsCancellationRequested)
                        {
                            retryCount++;
                            if (retryCount < maxRetries)
                            {
                                int delayMs = (int)Math.Pow(2, retryCount) * 1000;
                                Log.Warn(nameof(DlwnloadHls_avc_flv), $"[{card.Name}({card.RoomId})]FLV下载超时，{delayMs}ms后第{retryCount}次重试");
                                Thread.Sleep(delayMs);
                                hostClass = GetFlvHost_avc(card);
                                DlwnloadURL = $"{hostClass.host}{hostClass.base_url}{hostClass.uri_name}{hostClass.extra}";
                                continue;
                            }
                            Log.Error(nameof(DlwnloadHls_avc_flv), $"[{card.Name}({card.RoomId})]FLV下载超时，重试耗尽");
                            break;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(nameof(DlwnloadHls_avc_flv), $"[{card.Name}({card.RoomId})]FLV下载发生未预料错误", ex);
                            break;
                        }
                    }
                }

                hlsState = CheckAndHandleFile(File, ref card);
                try
                {
                    stopWatch.Stop();
                }
                catch (Exception)
                { }
            });
            return (hlsState, File);
        }


        /// <summary>
        /// 获取avc编码FLV的文件URL
        /// </summary>
        /// <param name="roomCard"></param>
        /// <param name="Url"></param>
        /// <returns></returns>
        public static bool GetFlvAvcUrl(RoomCardClass roomCard,int Definition, out string Url)
        {
            Url = "";
            if (!RoomInfo.GetLiveStatus(roomCard.RoomId))
            {
                return false;
            }
            HostClass hostClass = _GetHost(roomCard.RoomId, "http_stream", "flv", "avc", Definition);
            if (hostClass.Effective)
            {
                Url = $"{hostClass.host}{hostClass.base_url}{hostClass.uri_name}{hostClass.extra}";
                return true;
            }
            return false;
        }

    }
}
