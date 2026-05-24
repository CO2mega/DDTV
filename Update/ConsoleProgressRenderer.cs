using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Update
{
    /// <summary>
    /// 高级终端进度渲染器：简洁框线风格 + 状态着色 + 8文件槽位
    /// </summary>
    public class ConsoleProgressRenderer
    {
        private readonly int _maxSlots;
        private readonly object _consoleLock = new();
        private int _topRow;
        private bool _initialized;
        private readonly Dictionary<int, FileProgress> _slotStates = new();
        private int _completedFiles;
        private int _totalFiles;
        private long _totalBytes;
        private long _downloadedBytes;
        private string _type = "";
        private string _channel = "";
        private string _currentVersion = "";
        private string _targetVersion = "";
        private readonly bool _useColor;

        // 布局常量（相对于 _topRow 的行偏移）
        private const int TopTitleOffset = 1;       // 大标题
        private const int TitleSepOffset = 2;       // 标题分隔线
        private const int VerLineOffset = 3;        // 版本信息行
        private const int VerSepOffset = 4;         // 版本分隔线
        private const int WarnLineOffset = 5;       // 提示语行
        private const int WarnSepOffset = 6;        // 提示语分隔线
        private const int StatsLine1Offset = 7;     // 统计行1
        private const int StatsLine2Offset = 8;     // 统计行2
        private const int FileSepOffset = 9;        // 文件列表分隔线
        private const int FirstSlotOffset = 10;     // 第一个文件槽位
        private const int FixedColumns = 4 + 18 + 5 + 1 + 8; // [N] + progressBar + percent + space + status

        public ConsoleProgressRenderer(int maxSlots = 8)
        {
            _maxSlots = maxSlots;
            _useColor = !Console.IsOutputRedirected && OperatingSystem.IsWindows();
        }

        public void Initialize(int totalFiles, long totalBytes, string type, string channel, string currentVer, string targetVer)
        {
            lock (_consoleLock)
            {
                _totalFiles = totalFiles;
                _totalBytes = totalBytes;
                _completedFiles = 0;
                _downloadedBytes = 0;
                _type = type;
                _channel = channel;
                _currentVersion = currentVer;
                _targetVersion = targetVer;
                _topRow = Console.CursorTop;

                Console.Clear();
                DrawFrame();
                _initialized = true;
            }
        }

        public void SetSlotFile(int slot, string fileName, long totalBytes)
        {
            if (slot < 0 || slot >= _maxSlots) return;
            lock (_slotStates)
            {
                _slotStates[slot] = new FileProgress
                {
                    FileName = fileName,
                    TotalBytes = totalBytes,
                    DownloadedBytes = 0,
                    Percent = 0,
                    Speed = "",
                    Status = "WAITING "
                };
            }
            RefreshSlot(slot);
            RefreshStats();
        }

        public void UpdateSlotProgress(int slot, double percent, long downloadedBytes, string speed)
        {
            if (slot < 0 || slot >= _maxSlots) return;
            lock (_slotStates)
            {
                if (!_slotStates.TryGetValue(slot, out var prog)) return;
                prog.Percent = percent;
                prog.DownloadedBytes = downloadedBytes;
                prog.Speed = speed;
                if (prog.Status == "WAITING ")
                    prog.Status = "DOWNLOAD";
            }
            RefreshSlot(slot);
        }

        public void SetSlotCompleted(int slot, bool success, string? statusOverride = null)
        {
            if (slot < 0 || slot >= _maxSlots) return;
            lock (_slotStates)
            {
                if (!_slotStates.TryGetValue(slot, out var prog)) return;
                prog.Percent = success ? 100 : prog.Percent;
                prog.Status = statusOverride ?? (success ? "DONE    " : "FAILED  ");
                if (success)
                {
                    Interlocked.Increment(ref _completedFiles);
                    Interlocked.Add(ref _downloadedBytes, prog.TotalBytes);
                }
            }
            RefreshSlot(slot);
            RefreshStats();
        }

        public void SetOverallProgress(int completed, long downloadedBytes)
        {
            _completedFiles = completed;
            _downloadedBytes = downloadedBytes;
            RefreshStats();
        }

        public void Finish()
        {
            lock (_consoleLock)
            {
                if (!_initialized) return;
                int lastRow = _topRow + FirstSlotOffset + _maxSlots;
                Console.SetCursorPosition(0, lastRow);
            }
        }

        // ==================== 私有绘制方法 ====================

        private void DrawFrame()
        {
            int innerWidth = GetInnerWidth();

            // 顶部边框
            Console.WriteLine("╔" + new string('═', innerWidth) + "╗");

            // 大标题
            string topTitle = "DDTV  自动更新程序";
            int titlePad = Math.Max(0, (innerWidth - GetDisplayWidth(topTitle)) / 2);
            string titleLine = new string(' ', titlePad) + topTitle;
            WriteRawLine(titleLine, innerWidth);

            // 标题分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 版本信息行
            string verInfo = $"  客户端: {_type}  |  通道: {_channel}  |  {_currentVersion}  →  {_targetVersion}";
            WriteRawLine(verInfo, innerWidth);

            // 版本分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 提示语行（黄色高亮）
            string warning = "[!] 更新程序正在运行中，请勿关闭此窗口";
            string warnLine = "  " + warning;
            WriteRawLine(warnLine, innerWidth);

            // 提示语分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 统计行占位（2行）
            for (int i = 0; i < 2; i++)
                Console.WriteLine("║" + new string(' ', innerWidth) + "║");

            // 文件列表分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 文件槽位占位行
            for (int i = 0; i < _maxSlots; i++)
            {
                Console.WriteLine("║" + new string(' ', innerWidth) + "║");
                _slotStates[i] = new FileProgress { Status = "WAITING " };
            }

            // 底部边框
            Console.WriteLine("╚" + new string('═', innerWidth) + "╝");
        }

        private void RefreshStats()
        {
            lock (_consoleLock)
            {
                if (!_initialized) return;
                int innerWidth = GetInnerWidth();

                double overallPercent = _totalBytes > 0 ? (double)_downloadedBytes / _totalBytes * 100 : 0;
                string bar = RenderBar(overallPercent, 28);

                int activeDownloads = GetActiveDownloadCount();
                int remaining = _totalFiles - _completedFiles - activeDownloads;
                if (remaining < 0) remaining = 0;

                string line1 = $"  总体进度 [{bar}] {overallPercent,6:F1}%";
                string line2 = $"  文件  {_completedFiles}/{_totalFiles} 完成  |  并行 {activeDownloads}/{_maxSlots}  |  剩余 {remaining}  |  {FormatBytes(_downloadedBytes)} / {FormatBytes(_totalBytes)}";

                int row1 = _topRow + StatsLine1Offset;
                int row2 = _topRow + StatsLine2Offset;

                WriteLineContent(row1, line1, innerWidth);
                WriteLineContent(row2, line2, innerWidth);
            }
        }

        private void RefreshSlot(int slot)
        {
            lock (_consoleLock)
            {
                if (!_initialized) return;
                string linePrefix;
                string status;
                int innerWidth = GetInnerWidth();
                int fileNameWidth = Math.Max(10, innerWidth - FixedColumns);
                lock (_slotStates)
                {
                    if (!_slotStates.TryGetValue(slot, out var prog)) return;
                    string bar = RenderBar(prog.Percent, 16);
                    string name = FormatFileName(prog.FileName, fileNameWidth);
                    linePrefix = $" [{slot + 1}] [{bar}] {prog.Percent,5:F1}% {name} ";
                    status = prog.Status;
                }

                int row = _topRow + FirstSlotOffset + slot;
                WriteLineContent(row, linePrefix + status, innerWidth);

                // 状态着色重写
                if (_useColor)
                {
                    int statusCol = 1 + innerWidth - 8;
                    Console.SetCursorPosition(statusCol, row);
                    Console.ForegroundColor = GetStatusColor(status);
                    Console.Write(status);
                    Console.ResetColor();
                }
            }
        }

        private void WriteLineContent(int row, string content, int innerWidth)
        {
            Console.SetCursorPosition(0, row);
            Console.Write("║");
            int cw = GetDisplayWidth(content);
            if (cw > innerWidth)
            {
                content = TruncateToWidth(content, innerWidth);
                cw = GetDisplayWidth(content);
            }
            Console.Write(content);
            int spaces = innerWidth - cw;
            if (spaces > 0)
                Console.Write(new string(' ', spaces));
            Console.Write("║");
        }

        private void WriteRawLine(string content, int innerWidth)
        {
            int cw = GetDisplayWidth(content);
            if (cw > innerWidth)
            {
                content = TruncateToWidth(content, innerWidth);
                cw = GetDisplayWidth(content);
            }
            Console.Write("║" + content);
            Console.Write(new string(' ', innerWidth - cw));
            Console.WriteLine("║");
        }

        private static string RenderBar(double percent, int width)
        {
            int filled = (int)(width * percent / 100.0);
            if (filled > width) filled = width;
            return new string('#', filled) + new string('-', width - filled);
        }

        private static string FormatFileName(string name, int maxDisplayWidth)
        {
            int w = GetDisplayWidth(name);
            if (w <= maxDisplayWidth)
            {
                int pad = maxDisplayWidth - w;
                return name + new string(' ', pad);
            }
            var sb = new StringBuilder();
            int cw = 0;
            foreach (char c in name)
            {
                int charW = c > 127 ? 2 : 1;
                if (cw + charW + 3 > maxDisplayWidth)
                    break;
                sb.Append(c);
                cw += charW;
            }
            sb.Append("...");
            int pad2 = maxDisplayWidth - GetDisplayWidth(sb.ToString());
            if (pad2 > 0) sb.Append(' ', pad2);
            return sb.ToString();
        }

        private int GetActiveDownloadCount()
        {
            lock (_slotStates)
            {
                int count = 0;
                foreach (var kv in _slotStates)
                {
                    if (kv.Value.Status == "DOWNLOAD")
                        count++;
                }
                return count;
            }
        }

        private static ConsoleColor GetStatusColor(string status)
        {
            return status switch
            {
                "DOWNLOAD" => ConsoleColor.Cyan,
                "DONE    " => ConsoleColor.Green,
                "WAITING " => ConsoleColor.DarkGray,
                "FAILED  " => ConsoleColor.Red,
                "CANCELED" => ConsoleColor.Red,
                _ => ConsoleColor.Gray,
            };
        }

        private static int GetInnerWidth()
        {
            int w = Console.WindowWidth;
            return w > 4 ? w - 4 : 76;
        }

        private static int GetDisplayWidth(string text)
        {
            int w = 0;
            foreach (char c in text)
                w += c > 127 ? 2 : 1;
            return w;
        }

        private static string TruncateToWidth(string text, int maxWidth)
        {
            var sb = new StringBuilder();
            int w = 0;
            foreach (char c in text)
            {
                int cw = c > 127 ? 2 : 1;
                if (w + cw > maxWidth) break;
                sb.Append(c);
                w += cw;
            }
            return sb.ToString();
        }

        private static string FormatBytes(long bytes)
        {
            return bytes >= 1L << 30 ? $"{(double)bytes / (1L << 30):F2} GB"
                : bytes >= 1L << 20 ? $"{(double)bytes / (1L << 20):F2} MB"
                : bytes >= 1L << 10 ? $"{(double)bytes / (1L << 10):F2} KB"
                : $"{bytes} B";
        }

        private class FileProgress
        {
            public string FileName { get; set; } = "";
            public long TotalBytes { get; set; }
            public long DownloadedBytes { get; set; }
            public double Percent { get; set; }
            public string Speed { get; set; } = "";
            public string Status { get; set; } = "";
        }
    }
}
