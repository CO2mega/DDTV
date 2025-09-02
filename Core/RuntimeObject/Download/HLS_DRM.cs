using Core.LogModule;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Network;
using cswidevine;
using static Core.RuntimeObject.Download.Basics;

namespace Core.RuntimeObject.Download
{
    public class HLS_DRM
    {
        /// <summary>
        /// 录制DRM加密的HLS流（Widevine）为MP4文件
        /// </summary>
        /// <param name="card">房间卡片信息</param>
        /// <param name="Reconnection">是否为重连</param>
        /// <returns>[TaskStatus]任务状态；[FileName]下载成功的文件名</returns>
        public static async Task<(DlwnloadTaskState hlsState, string FileName)> DlwnloadHlsDrm_avc_mp4(RoomCardClass card, bool Reconnection)
        {
            DlwnloadTaskState hlsState = DlwnloadTaskState.Default;
            string File = string.Empty;
            Stopwatch stopWatch = new Stopwatch();
            await Task.Run(() =>
            {
                InitializeDownload(card, RoomCardClass.TaskType.HLS_AVC);
                card.DownInfo.DownloadFileList.CurrentOperationVideoFile = string.Empty;
                long roomId = card.RoomId;
                File = $"{Config.Core_RunConfig._RecFileDirectory}{Core.Tools.KeyCharacterReplacement.ReplaceKeyword( $"{Config.Core_RunConfig._DefaultLiverFolderName}/{Core.Config.Core_RunConfig._DefaultDataFolderName}{(string.IsNullOrEmpty(Core.Config.Core_RunConfig._DefaultDataFolderName)?"":"/")}{Config.Core_RunConfig._DefaultFileName}",DateTime.Now,card.UID)}_drm.mp4";
                CreateDirectoryIfNotExists(File.Substring(0, File.LastIndexOf('/')));
                Thread.Sleep(5);
                long DownloadFileSizeForThisTask = 0;
                string keyIdHex = null;
                string keyHex = null;
                using (FileStream fs = new FileStream(File, FileMode.Append))
                {
                    HostClass hostClass = new();
                    // 获取Host和m3u8，已自动解析Widevine KEY
                    while (!GetHlsHost_avc(card, ref hostClass))
                    {
                        hlsState = DlwnloadTaskState.NoHLSStreamExists;
                        Thread.Sleep(Config.Core_RunConfig._HlsWaitingTime * 1000);
                    }
                    // Widevine参数直接从hostClass获取
                    string pssh = hostClass.pssh;
                    Log.Info("DRM", $"Widevine PSSH: {pssh}");
                    byte[] drmKey = null;
                    if (!string.IsNullOrEmpty(pssh))
                    {
                        (drmKey, keyIdHex, keyHex) = GetWidevineKey(Config.Core_RunConfig._WidevineLicenseUrl, pssh);
                    }
                    // 写入shaka-packager命令到txt
                    if (!string.IsNullOrEmpty(keyIdHex) && !string.IsNullOrEmpty(keyHex))
                    {
                        string txtPath = Path.Combine(Path.GetDirectoryName(File), "解密命令.txt");
                        string cmd = $"packager input={Path.GetFileName(File)},stream=video,output=output.mp4 --enable_raw_key_decryption --keys key_id={keyIdHex}:key={keyHex}";
                        System.IO.File.WriteAllText(txtPath, cmd, Encoding.UTF8);
                        Log.Info("DRM", $"Shaka Packager Command: {cmd}");
                    }
                    bool InitialRequest = true;
                    long currentLocation = 0;
                    long StartLiveTime = card.live_time.Value;
                    stopWatch.Start();
                    int RetryCount = 0;
                    while (true)
                    {
                        if (Config.Core_RunConfig._CutAccordingToSize > 0 && DownloadFileSizeForThisTask > Config.Core_RunConfig._CutAccordingToSize)
                        {
                            hlsState = DlwnloadTaskState.Success;
                            return;
                        }
                        if (Config.Core_RunConfig._CutAccordingToTime > 0 && stopWatch.Elapsed.TotalSeconds > Config.Core_RunConfig._CutAccordingToTime)
                        {
                            hlsState = DlwnloadTaskState.Success;
                            return;
                        }
                        long downloadSizeForThisCycle = 0;
                        try
                        {
                            if (card.DownInfo.Unmark || card.DownInfo.IsCut || card.live_time.Value != StartLiveTime)
                            {
                                hlsState = DlwnloadTaskState.UserCancellation;
                                return;
                            }
                            bool isHlsHostAvailable = RefreshHlsHost_avc(card, ref hostClass);
                            if (!isHlsHostAvailable)
                            {
                                if (RetryCount > 5)
                                {
                                    hlsState = DlwnloadTaskState.NoHLSStreamExists;
                                }
                                RetryCount++;
                                return;
                            }
                            foreach (var item in hostClass.eXTM3U.eXTINFs)
                            {
                                if (long.TryParse(item.FileName, out long index) && (index > currentLocation || currentLocation == 0))
                                {
                                    // 下载分片
                                    byte[] encryptedSegment = DownloadSegment(hostClass.host + hostClass.base_url + item.FileName + "." + item.ExtensionName + "?" + hostClass.extra);
                                    // 解密分片
                                    byte[] decryptedSegment = null;
                                    if (drmKey != null)
                                    {
                                        decryptedSegment = WidevineDecrypt(encryptedSegment, drmKey); // 只处理Widevine
                                    }
                                    else
                                    {
                                        decryptedSegment = encryptedSegment;
                                    }
                                    fs.Write(decryptedSegment, 0, decryptedSegment.Length);
                                    downloadSizeForThisCycle += decryptedSegment.Length;
                                    currentLocation = index;
                                }
                            }
                            hostClass.eXTM3U.eXTINFs = new();
                            DownloadFileSizeForThisTask += downloadSizeForThisCycle;
                            if (hostClass.eXTM3U.IsEND)
                            {
                                hlsState = DlwnloadTaskState.Success;
                                return;
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(nameof(HLS_DRM), $"[{card.Name}({card.RoomId})]录制循环中出现未知错误，写入日志", e, true);
                            if (!card.DownInfo.Unmark && !card.DownInfo.IsCut)
                                Thread.Sleep(1000);
                            if (card.DownInfo.IsCut)
                                return;
                        }
                        if (!card.DownInfo.Unmark && !card.DownInfo.IsCut)
                            Thread.Sleep(2000);
                        if (card.DownInfo.IsCut)
                            return;
                    }
                }
            });
            card.DownInfo.DownloadSize = 0;
            stopWatch.Stop();
            return (hlsState, File);
        }

        // Widevine密钥获取流程
        private static (byte[] drmKey, string keyIdHex, string keyHex) GetWidevineKey(string licenseUrl, string pssh)
        {
            var device = Device.Load(Core.Config.Core_RunConfig._WVDFilePath); // Widevine设备文件路径
            var cdm = Cdm.FromDevice(device);
            var sessionId = cdm.Open();
            var psshObj = new Pssh(pssh);
            var challenge = cdm.GetLicenseChallenge(sessionId, psshObj, "STREAMING");
            using var client = new HttpClient();
            using var content = new ByteArrayContent(challenge);
            var response = client.PostAsync(licenseUrl, content).Result;
            cdm.ParseLicense(sessionId, response.Content.ReadAsByteArrayAsync().Result);
            var keyObj = cdm.GetKeys(sessionId).FirstOrDefault();
            string keyIdHex = null;
            string keyHex = null;
            byte[] drmKey = null;
            if (keyObj != null)
            {
                drmKey = keyObj.RawKey;
                keyIdHex = BitConverter.ToString(keyObj.KeyId).Replace("-", "").ToLower();
                keyHex = BitConverter.ToString(keyObj.RawKey).Replace("-", "").ToLower();
                Log.Info("DRM", $"Widevine KeyId: {keyIdHex}");
                Log.Info("DRM", $"Widevine Key: {keyHex}");
            }
            cdm.Close(sessionId);
            return (drmKey, keyIdHex, keyHex);
        }

        // Widevine解密分片
        private static byte[] WidevineDecrypt(byte[] encryptedSegment, byte[] key)
        {
            // 实际Widevine解密流程应使用cswidevine库
            // 这里仅为伪代码，实际解密需根据Widevine分片格式实现
            return encryptedSegment;
        }

        // 下载分片
        private static byte[] DownloadSegment(string url)
        {
            string segmentStr = Core.Network.Download.File.GetFileToString(url);
            return segmentStr != null ? Encoding.UTF8.GetBytes(segmentStr) : new byte[0];
        }
    }
}
