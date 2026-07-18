using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Core.Tools.FileOperations;

namespace Core.RuntimeObject
{
    public class RecordingFiles
    {
        #region Private Properties
        /// <summary>
        /// 目录结构缓存（避免前端轮询时每次都全量递归扫盘，与录制写盘抢IO）
        /// </summary>
        private static readonly object _dirStructCacheLock = new();
        private static string _dirStructCachePath = null;
        private static DirectoryNode _dirStructCache = null;
        private static DateTime _dirStructCacheTime = DateTime.MinValue;
        /// <summary>
        /// 目录结构缓存有效期（秒）
        /// </summary>
        private const int _dirStructCacheSeconds = 5;
        #endregion

        #region Public Method
        /// <summary>
        /// 获取目标路径下所有文件的结构以json格式返回
        /// </summary>
        /// <param name="Path">目标路径</param>
        /// <param name="DirectoryStructureJson">out值，返回的json内容</param>
        /// <returns>路径是否存在</returns>
        public static bool GetDirectoryStructure(string Path, out DirectoryNode DirectoryStructureJson)
        {
            if (!Directory.Exists(Path))
            {
                DirectoryStructureJson = new();
                return false;
            }
            lock (_dirStructCacheLock)
            {
                //缓存有效且路径一致时直接返回缓存快照（返回对象只被调用方立即序列化，共享只读是安全的）
                if (_dirStructCache != null
                    && _dirStructCachePath == Path
                    && (DateTime.Now - _dirStructCacheTime).TotalSeconds < _dirStructCacheSeconds)
                {
                    DirectoryStructureJson = _dirStructCache;
                    return true;
                }
                DirectoryStructureJson = DirectoryHelper.GetDirectoryStructure(Path);
                _dirStructCachePath = Path;
                _dirStructCache = DirectoryStructureJson;
                _dirStructCacheTime = DateTime.Now;
                return true;
            }
        }
        #endregion

        #region Private Method

        #endregion

        #region Public Class

        #endregion

        #region Public Enmu

        #endregion
    }
}
