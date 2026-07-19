namespace Desktop.Services
{
    /// <summary>
    /// 弹幕屏蔽词缓存：屏蔽词配置字符串按"|"切分的结果缓存复用，配置内容变化时才重新切分。
    /// 弹幕接收是高频路径（每条消息至少匹配一次），原来每条消息都Split一次会产生大量临时数组和字符串分配。
    /// </summary>
    internal static class BarrageBlockWords
    {
        private static string _cachedConfig = null;
        private static string[] _cachedWords = System.Array.Empty<string>();

        /// <summary>
        /// 当前生效的屏蔽词数组（已去除空项）。读多写少，重建时直接整体替换引用，旧数组被正在使用的线程读完后由GC回收
        /// </summary>
        public static string[] Words
        {
            get
            {
                string config = Core.Config.Core_RunConfig._BlockBarrageList ?? string.Empty;
                if (config != _cachedConfig)
                {
                    _cachedConfig = config;
                    _cachedWords = config.Split('|', System.StringSplitOptions.RemoveEmptyEntries);
                }
                return _cachedWords;
            }
        }

        /// <summary>
        /// 判断文本是否命中任一屏蔽词（普通循环，避免LINQ闭包分配）
        /// </summary>
        public static bool IsBlocked(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            foreach (string word in Words)
            {
                if (text.Contains(word))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
