using System;
using System.IO;

namespace PeStubGen
{
    /// <summary>读文件 → PeInfo 的入口，界面和命令行共用。</summary>
    public static class PeImageReader
    {
        public static PeInfo Read(string path)
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("找不到文件：" + full);

            PeFile pe = PeFile.Load(full);
            PeInfo info = new PeInfo();
            info.FilePath = full;
            info.Is64Bit = pe.Is64;
            info.Machine = pe.Machine;
            info.ImageBase = pe.ImageBase;
            info.SizeOfImage = pe.SizeOfImage;
            info.FileSize = new FileInfo(full).Length;
            info.Modules = PeImports.Read(pe);
            return info;
        }
    }
}
