namespace Desktop.Services
{
    /// <summary>
    /// UI前后台状态：主窗口隐藏到托盘时进入后台模式，
    /// 各UI轮询定时器据此跳过或降频，避免不可见的UI持续消耗CPU和网络请求。
    /// </summary>
    internal static class UiActivity
    {
        private static volatile bool _isBackground = false;

        /// <summary>
        /// 当前是否处于后台模式（主窗口已隐藏到托盘）
        /// </summary>
        public static bool IsBackground => _isBackground;

        /// <summary>
        /// 从后台恢复到前台时触发（在UI线程触发）
        /// </summary>
        public static event Action? ForegroundRestored;

        public static void SetBackground(bool value)
        {
            if (_isBackground == value)
            {
                return;
            }
            _isBackground = value;
            if (!value)
            {
                ForegroundRestored?.Invoke();
            }
        }
    }
}
