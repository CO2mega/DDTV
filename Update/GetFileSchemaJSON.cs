using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Update
{
    public class GetFileSchemaJSON
    {
        public static void GetFileList(string FilePath, out List<FileInfo> fileInfos)
        {
            fileInfos = new List<FileInfo>();
            // 检查路径是否存在，避免抛出 DirectoryNotFoundException
            if (!Directory.Exists(FilePath))
            {
                return;
            }
            
            DirectoryInfo root = new DirectoryInfo(FilePath);
            foreach (var item in root.GetDirectories())
            {
                GetFileList(item.FullName, out List<FileInfo> _T);
                fileInfos.AddRange(_T);
            }
            foreach (FileInfo item in root.GetFiles())
            {
                fileInfos.Add(item);
            }
            
        }
        public class FileInfoClass
        {

            public string Ver { get; set; }
            public List<Files> files { set; get; } = new List<Files>();
            public string Bucket { get; set; }
            public string Type { get; set; }
            public class Files
            {
                public string FileName { get; set; }
                public long Size { get; set; }
                public string FileMd5 { get; set; }
                public string FilePath { get; set; }
            }
        }
    }
}
