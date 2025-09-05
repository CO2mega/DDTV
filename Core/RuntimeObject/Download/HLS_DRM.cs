using Core.LogModule;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Network;
using static Core.RuntimeObject.Download.Basics;

namespace Core.RuntimeObject.Download
{
    public class HLS_DRM
    {
        // 通过外部cswidevine.exe获取Widevine密钥
        public static DrmInfo GetWidevineKeyByExternal(string wvdFile, string pssh, string licenseServer)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "./Plugins/cswidevine/cswidevine.exe",
                    Arguments = $"license \"{wvdFile}\" \"{pssh}\" \"{licenseServer}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var lines = output.Split('\n');
            string keyIdHex = null, keyHex = null;
            foreach (var line in lines)
            {
                if (line.StartsWith("key_id:")) keyIdHex = line.Substring(7).Trim();
                if (line.StartsWith("key:")) keyHex = line.Substring(4).Trim();
            }
            if (!string.IsNullOrEmpty(keyIdHex) && !string.IsNullOrEmpty(keyHex))
            {
                return new DrmInfo { keyIdHex = keyIdHex, keyHex = keyHex };
            }
            return null;
        }

        // 写入shaka-packager命令
        public static void WriteShakaPackagerCommand(string filePath, string keyIdHex, string keyHex)
        {
            string txtPath = Path.Combine(Path.GetDirectoryName(filePath), "解密命令.txt");
            string cmd = $"packager input={Path.GetFileName(filePath)},stream=video,output=output.mp4 --enable_raw_key_decryption --keys key_id={keyIdHex}:key={keyHex}";
            System.IO.File.WriteAllText(txtPath, cmd, Encoding.UTF8);
            Log.Info("DRM", $"Shaka Packager Command: {cmd}");
        }

        // Widevine设备文件测试，返回所有[CONTENT] keyId:key
        public static Dictionary<string, string> TestWvdFile(string wvdFilePath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "plugins/cswidevine/cswidevine.exe",
                    Arguments = $"test \"{wvdFilePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Log.Info("WidevineTest", output);
            var result = new Dictionary<string, string>();
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("[CONTENT]"))
                {
                    var parts = line.Split("[CONTENT]");
                    if (parts.Length > 1)
                    {
                        var kv = parts[1].Trim().Split(':');
                        if (kv.Length == 2)
                        {
                            string keyId = kv[0].Trim();
                            string key = kv[1].Trim();
                            result[keyId] = key;
                        }
                    }
                }
            }
            return result;
        }

        // DRM相关处理结构体
        public class DrmInfo
        {
            public string keyIdHex;
            public string keyHex;
        }
    }
}
