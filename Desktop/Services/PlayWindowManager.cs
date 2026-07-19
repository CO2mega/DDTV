using System.Windows;
using System.Windows.Interop;
using Core.LogModule;
using Desktop.Models;
using Desktop.Views.Windows;

namespace Desktop.Services
{
    public static class PlayWindowManager
    {
        private static readonly List<Window> _windows = new();
        private static readonly object _lock = new();

        public static void Register(Window window)
        {
            lock (_lock)
            {
                if (!_windows.Contains(window))
                {
                    _windows.Add(window);
                }
            }
        }

        public static void Unregister(Window window)
        {
            lock (_lock)
            {
                _windows.Remove(window);
            }
        }

        /// <summary>
        /// 当前已登记的VLC播放窗口数量
        /// </summary>
        public static int WindowCount
        {
            get
            {
                lock (_lock)
                {
                    return _windows.Count(w => w is VlcPlayWindow);
                }
            }
        }

        /// <summary>
        /// 关闭所有VLC播放窗口。只负责调用各窗口的Close()，
        /// 资源释放（MediaPlayer/Media/弹幕退订）由每个窗口自己的Closing事件完成，
        /// 避免这里重复实现一套释放逻辑导致两边不同步
        /// </summary>
        public static void CloseAll()
        {
            List<Window> targets;
            lock (_lock)
            {
                targets = _windows.Where(w => w is VlcPlayWindow).ToList();
            }

            foreach (var w in targets)
            {
                //关闭过程中各窗口的Closing会Unregister自己（修改_windows），所以上面先取快照再遍历
                w.Dispatcher.Invoke(() => w.Close());
            }
        }

        public static void Arrange(Window source, WindowLayoutMode mode)
        {
            List<Window> targets;
            lock (_lock)
            {
                targets = _windows
                    .Where(w => w is VlcPlayWindow && w.IsVisible && w.IsLoaded)
                    .ToList();
            }

            if (targets.Count == 0)
                return;

            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(source).Handle);
            var area = screen.WorkingArea;

            foreach (var w in targets)
            {
                if (w.WindowState == WindowState.Maximized || w.WindowState == WindowState.Minimized)
                {
                    w.Dispatcher.Invoke(() => w.WindowState = WindowState.Normal);
                }
            }

            switch (mode)
            {
                case WindowLayoutMode.Grid3x3:
                    ArrangeGrid(targets, area, 3, 3);
                    break;
                case WindowLayoutMode.Grid3x4:
                    ArrangeGrid(targets, area, 4, 3);
                    break;
                case WindowLayoutMode.MainAndSub:
                    ArrangeMainAndSub(targets, area);
                    break;
            }
        }

        private static void ArrangeGrid(List<Window> windows, System.Drawing.Rectangle area, int maxCols, int maxRows)
        {
            int count = windows.Count;
            if (count == 0) return;

            if (count == 1)
            {
                SetWindowBounds(windows[0], area.Left, area.Top, area.Width, area.Height);
                return;
            }

            int cols = Math.Min(count, maxCols);
            int rows = Math.Min((int)Math.Ceiling((double)count / cols), maxRows);

            if (rows > maxRows)
            {
                rows = maxRows;
            }

            int effectiveCount = Math.Min(count, cols * rows);

            int cellW = area.Width / cols;
            int cellH = area.Height / rows;

            int usedCols = Math.Min(cols, effectiveCount);
            int usedRows = (int)Math.Ceiling((double)effectiveCount / usedCols);

            int startX = area.Left;
            int startY = area.Top;
            int totalGridW = usedCols * cellW;
            int totalGridH = usedRows * cellH;

            int offsetX = startX + (area.Width - totalGridW) / 2;
            int offsetY = startY + (area.Height - totalGridH) / 2;

            for (int i = 0; i < effectiveCount; i++)
            {
                int row = i / cols;
                int col = i % cols;

                int x = offsetX + col * cellW;
                int y = offsetY + row * cellH;
                int w = cellW;
                int h = cellH;

                if (col == cols - 1)
                {
                    w = area.Width - (x - area.Left);
                }
                if (row == rows - 1 && i >= effectiveCount - usedCols)
                {
                    h = area.Height - (y - area.Top);
                }

                SetWindowBounds(windows[i], x, y, w, h);
            }
        }

        private static void ArrangeMainAndSub(List<Window> windows, System.Drawing.Rectangle area)
        {
            int count = windows.Count;
            if (count == 0) return;

            if (count == 1)
            {
                SetWindowBounds(windows[0], area.Left, area.Top, area.Width, area.Height);
                return;
            }

            int cellW = area.Width / 3;
            int cellH = area.Height / 3;

            // 主窗口：左上 2×2
            SetWindowBounds(windows[0], area.Left, area.Top, cellW * 2, cellH * 2);

            if (count >= 2)
            {
                // 右上
                SetWindowBounds(windows[1], area.Left + cellW * 2, area.Top, cellW, cellH);
            }
            if (count >= 3)
            {
                // 右中
                SetWindowBounds(windows[2], area.Left + cellW * 2, area.Top + cellH, cellW, cellH);
            }
            if (count >= 4)
            {
                // 左下
                SetWindowBounds(windows[3], area.Left, area.Top + cellH * 2, cellW, cellH);
            }
            if (count >= 5)
            {
                // 中下
                SetWindowBounds(windows[4], area.Left + cellW, area.Top + cellH * 2, cellW, cellH);
            }
            if (count >= 6)
            {
                // 右下
                SetWindowBounds(windows[5], area.Left + cellW * 2, area.Top + cellH * 2, cellW, cellH);
            }
        }

        private static void SetWindowBounds(Window window, int x, int y, int width, int height)
        {
            window.Dispatcher.Invoke(() =>
            {
                try
                {
                    window.Left = x;
                    window.Top = y;
                    window.Width = width;
                    window.Height = height;
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(PlayWindowManager), $"设置窗口位置失败: {window.Title}", ex, false);
                }
            });
        }
    }
}
