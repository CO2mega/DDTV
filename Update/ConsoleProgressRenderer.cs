using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Update
{
    /// <summary>
    /// 高级终端进度渲染器：上下分区（下载中/已完成）+ 速度大小显示 + 动态槽位
    /// </summary>
    public class ConsoleProgressRenderer
    {
        private readonly int _maxSlots;
        private readonly object _consoleLock = new();
        private int _topRow;
        private bool _initialized;
        private readonly Dictionary<int, FileProgress> _slotStates = new();
        private readonly List<CompletedFile> _completedList = new();
        private int _completedFiles;
        private int _totalFiles;
        private long _totalBytes;
        private long _downloadedBytes;
        private string _type = "";
        private string _channel = "";
        private string _currentVersion = "";
        private string _targetVersion = "";
        private readonly bool _useColor;

        // 固定列宽（下载中区域）
        // [N](4) + [bar](18) + percent(6) + speed(9) + size(16) + space(1) + status(8)
        private const int FixedColumns = 4 + 18 + 6 + 9 + 16 + 1 + 8;

        // 行号计算（基于 _maxSlots 动态）
        private int TitleSepRow => _topRow + 2;
        private int VerLineRow => _topRow + 3;
        private int VerSepRow => _topRow + 4;
        private int WarnLineRow => _topRow + 5;
        private int WarnSepRow => _topRow + 6;
        private int Stats1Row => _topRow + 7;
        private int Stats2Row => _topRow + 8;
        private int ActiveSepRow => _topRow + 9;
        private int ActiveFirstRow => _topRow + 10;
        private int DoneTitleSepRow => _topRow + 10 + _maxSlots + 1;
        private int DoneTitleTextRow => _topRow + 10 + _maxSlots + 2;
        private int DoneSepRow => _topRow + 10 + _maxSlots + 3;
        private int DoneFirstRow => _topRow + 10 + _maxSlots + 4;
        private int BottomBorderRow => _topRow + 10 + _maxSlots + 4 + _maxSlots;

        public ConsoleProgressRenderer(int maxSlots)
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
                _completedList.Clear();

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
                    BytesPerSecondSpeed = 0,
                    Status = "WAITING "
                };
            }
            RefreshSlot(slot);
            RefreshStats();
        }

        public void UpdateSlotProgress(int slot, double percent, long downloadedBytes, double bytesPerSecond)
        {
            if (slot < 0 || slot >= _maxSlots) return;
            lock (_slotStates)
            {
                if (!_slotStates.TryGetValue(slot, out var prog)) return;
                prog.Percent = percent;
                prog.DownloadedBytes = downloadedBytes;
                prog.BytesPerSecondSpeed = bytesPerSecond;
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
                    _completedList.Insert(0, new CompletedFile
                    {
                        FileName = prog.FileName,
                        TotalBytes = prog.TotalBytes,
                        Status = prog.Status
                    });
                    if (_completedList.Count > _maxSlots)
                        _completedList.RemoveAt(_completedList.Count - 1);
                }
                // 清空该槽位
                _slotStates[slot] = new FileProgress { Status = "WAITING " };
            }
            RefreshSlot(slot);
            RefreshDoneList();
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
                Console.SetCursorPosition(0, BottomBorderRow);
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
            WriteCenteredLine(topTitle, innerWidth);

            // 标题分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 版本信息行
            string verInfo = $"  客户端: {_type}  |  通道: {_channel}  |  {_currentVersion}  →  {_targetVersion}";
            WriteRawLine(verInfo, innerWidth);

            // 版本分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 提示语行
            string warning = "[!] 更新程序正在运行中，请勿关闭此窗口";
            WriteRawLine("  " + warning, innerWidth);

            // 提示语分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 统计行占位（2行）
            for (int i = 0; i < 2; i++)
                Console.WriteLine("║" + new string(' ', innerWidth) + "║");

            // 下载中分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 下载中槽位占位行
            for (int i = 0; i < _maxSlots; i++)
            {
                Console.WriteLine("║" + new string(' ', innerWidth) + "║");
                _slotStates[i] = new FileProgress { Status = "WAITING " };
            }

            // 已完成列表标题分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 已完成列表标题
            WriteCenteredLine("──── 已下载文件列表 ────", innerWidth);

            // 已完成列表分隔线
            Console.WriteLine("╠" + new string('═', innerWidth) + "╣");

            // 已完成槽位占位行
            for (int i = 0; i < _maxSlots; i++)
                Console.WriteLine("║" + new string(' ', innerWidth) + "║");

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

                WriteLineContent(Stats1Row, line1, innerWidth);
                WriteLineContent(Stats2Row, line2, innerWidth);
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
                    string speedStr = FormatSpeed(prog.BytesPerSecondSpeed).PadLeft(9);
                    string sizeStr = $"{FormatBytesCompact(prog.DownloadedBytes)}/{FormatBytesCompact(prog.TotalBytes)}".PadLeft(16);
                    linePrefix = $" [{slot + 1}] [{bar}] {prog.Percent,5:F1}%{speedStr}{sizeStr} {name} ";
                    status = prog.Status;
                }

                int row = ActiveFirstRow + slot;
                WriteLineContent(row, linePrefix + status, innerWidth);

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

        private void RefreshDoneList()
        {
            lock (_consoleLock)
            {
                if (!_initialized) return;
                int innerWidth = GetInnerWidth();
                int fileNameWidth = Math.Max(10, innerWidth - 21);

                for (int i = 0; i < _maxSlots; i++)
                {
                    int row = DoneFirstRow + i;
                    string line;
                    string status = "        ";
                    if (i < _completedList.Count)
                    {
                        var done = _completedList[i];
                        string name = FormatFileName(done.FileName, fileNameWidth);
                        string sizeStr = FormatBytesCompact(done.TotalBytes).PadLeft(10);
                        line = $"  {name} {sizeStr} {done.Status}";
                        status = done.Status;
                    }
                    else
                    {
                        line = "";
                    }
                    WriteLineContent(row, line, innerWidth);

                    if (_useColor && i < _completedList.Count)
                    {
                        int statusCol = 1 + innerWidth - 8;
                        Console.SetCursorPosition(statusCol, row);
                        Console.ForegroundColor = GetStatusColor(status);
                        Console.Write(status);
                        Console.ResetColor();
                    }
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

        private void WriteCenteredLine(string text, int innerWidth)
        {
            int pad = Math.Max(0, (innerWidth - GetDisplayWidth(text)) / 2);
            string line = new string(' ', pad) + text;
            WriteRawLine(line, innerWidth);
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

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "-";
            return FormatBytesCompact((long)bytesPerSecond) + "/s";
        }

        private static string FormatBytesCompact(long bytes)
        {
            return bytes >= 1L << 30 ? $"{(double)bytes / (1L << 30):F1}GB"
                : bytes >= 1L << 20 ? $"{(double)bytes / (1L << 20):F1}MB"
                : bytes >= 1L << 10 ? $"{(double)bytes / (1L << 10):F1}KB"
                : $"{bytes}B";
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
            public double BytesPerSecondSpeed { get; set; }
            public string Status { get; set; } = "";
        }

        private class CompletedFile
        {
            public string FileName { get; set; } = "";
            public long TotalBytes { get; set; }
            public string Status { get; set; } = "";
        }
    }
}
