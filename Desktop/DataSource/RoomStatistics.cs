using Core;
using Core.LogModule;

namespace Desktop.DataSource
{
    /// <summary>
    /// 房间统计共享缓存：MainWindow标题栏和DefaultPage统计面板共用同一份统计数据。
    /// 带最小获取间隔与重入保护，多个调用方高频调用时共享同一次获取结果，
    /// 只有数值真正变化时才触发Updated事件通知UI更新。
    /// </summary>
    internal static class RoomStatistics
    {
        /// <summary>
        /// 最近一次获取到的统计数据
        /// </summary>
        public static (int MonitoringCount, int LiveCount, int RecCount) Current { get; private set; }

        /// <summary>
        /// 统计数据发生变化时触发（在后台线程触发，UI更新需自行封送到UI线程）
        /// </summary>
        public static event Action? Updated;

        private static DateTime _lastFetch = DateTime.MinValue;
        private static int _fetching = 0;
        /// <summary>
        /// 最小获取间隔，间隔内的重复调用直接复用缓存
        /// </summary>
        private static readonly TimeSpan _minInterval = TimeSpan.FromSeconds(2.5);

        /// <summary>
        /// 刷新统计数据（带缓存与重入保护）
        /// </summary>
        /// <param name="force">跳过缓存间隔与后台模式检查，强制获取一次（用于恢复到前台时立即刷新）</param>
        public static async Task RefreshAsync(bool force = false)
        {
            //托盘隐藏状态下不刷新（强制刷新除外，例如刚恢复到前台需要立即拿到最新值）
            if (Services.UiActivity.IsBackground && !force)
            {
                return;
            }
            if (!force && DateTime.Now - _lastFetch < _minInterval)
            {
                return;
            }
            if (Interlocked.Exchange(ref _fetching, 1) == 1)
            {
                return;
            }
            try
            {
                (int MonitoringCount, int LiveCount, int RecCount) count;
                if (Core.Config.Core_RunConfig._DesktopRemoteServer || Core.Config.Core_RunConfig._LocalHTTPMode)
                {
                    count = await NetWork.Post.PostBody<(int MonitoringCount, int LiveCount, int RecCount)>(
                        $"{Config.Core_RunConfig._DesktopIP}:{Config.Core_RunConfig._DesktopPort}/api/get_rooms/room_statistics");
                }
                else
                {
                    count = Core.RuntimeObject._Room.Overview.GetRoomStatisticsOverview();
                }
                _lastFetch = DateTime.Now;
                //只有数值变化才通知UI，避免统计数字静止时每秒触发无意义的绑定更新和标题重绘
                if (count != Current)
                {
                    Current = count;
                    Updated?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log.Warn(nameof(RoomStatistics), "更新房间统计出现错误", ex, false);
            }
            finally
            {
                Interlocked.Exchange(ref _fetching, 0);
            }
        }
    }
}
