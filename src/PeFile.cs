using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PeStubGen
{
    public class PeFormatException : Exception
    {
        public PeFormatException(string message) : base(message) { }
    }

    public class PeSection
    {
        public string Name;
        public uint VirtualAddress;
        public uint VirtualSize;
        public uint RawPointer;
        public uint RawSize;
    }

    /// <summary>
    /// 最小 PE 解析器：只做本项目需要的事——读头、把 RVA 映射成文件偏移、读字符串。
    /// 不依赖任何第三方库（本机无网络，装不了 PeNet）。
    /// </summary>
    public class PeFile
    {
        public byte[] Data;
        public bool Is64;
        public ushort Machine;
        public ulong ImageBase;
        public uint SizeOfImage;
        public uint SizeOfHeaders;
        public int DataDirectoryOffset;
        public string FilePath;
        public List<PeSection> Sections = new List<PeSection>();

        public static PeFile Load(string path)
        {
            PeFile f = Parse(File.ReadAllBytes(path));
            f.FilePath = path;
            return f;
        }

        public static PeFile Parse(byte[] data)
        {
            if (data == null || data.Length < 128)
                throw new PeFormatException("文件太小，放不下一个 PE 头。");
            if (data[0] != 0x4D || data[1] != 0x5A)
                throw new PeFormatException("缺少 MZ 头，这不是有效的 Windows 可执行文件。");

            int pe = (int)U32(data, 0x3C);
            if (pe < 0 || pe + 24 > data.Length)
                throw new PeFormatException("e_lfanew 指向文件外部，PE 头已损坏。");
            if (data[pe] != 0x50 || data[pe + 1] != 0x45 || data[pe + 2] != 0 || data[pe + 3] != 0)
                throw new PeFormatException("缺少 \"PE\\0\\0\" 签名，PE 头已损坏。");

            PeFile f = new PeFile();
            f.Data = data;
            f.Machine = U16(data, pe + 4);
            int sectionCount = U16(data, pe + 6);
            int optSize = U16(data, pe + 20);
            int opt = pe + 24;

            if (optSize <= 0 || opt + optSize > data.Length)
                throw new PeFormatException("可选头大小异常，PE 头已损坏。");

            ushort magic = U16(data, opt);
            if (magic == 0x20B) f.Is64 = true;
            else if (magic == 0x10B) f.Is64 = false;
            else throw new PeFormatException("未知的可选头类型 (Magic=0x" + magic.ToString("X4") + ")。");

            f.ImageBase = f.Is64 ? U64(data, opt + 24) : (ulong)U32(data, opt + 28);
            f.SizeOfImage = U32(data, opt + 56);
            f.SizeOfHeaders = U32(data, opt + 60);
            f.DataDirectoryOffset = f.Is64 ? opt + 112 : opt + 96;

            if (sectionCount <= 0 || sectionCount > 128)
                throw new PeFormatException("节表数量异常：" + sectionCount + "。");

            int st = opt + optSize;
            for (int i = 0; i < sectionCount; i++)
            {
                int s = st + i * 40;
                if (s + 40 > data.Length)
                    throw new PeFormatException("节表越界，PE 头已损坏。");
                PeSection sec = new PeSection();
                sec.Name = Encoding.ASCII.GetString(data, s, 8).TrimEnd('\0');
                sec.VirtualSize = U32(data, s + 8);
                sec.VirtualAddress = U32(data, s + 12);
                sec.RawSize = U32(data, s + 16);
                sec.RawPointer = U32(data, s + 20);
                f.Sections.Add(sec);
            }

            if (f.Sections.Count == 0)
                throw new PeFormatException("节表为空，PE 头已损坏。");

            return f;
        }

        public uint DirRva(int index)
        {
            int o = DataDirectoryOffset + index * 8;
            if (o + 8 > Data.Length) return 0;
            return U32(Data, o);
        }

        public uint DirSize(int index)
        {
            int o = DataDirectoryOffset + index * 8 + 4;
            if (o + 4 > Data.Length) return 0;
            return U32(Data, o);
        }

        /// <summary>RVA → 文件偏移；失败返回 -1。</summary>
        public int RvaToOffset(uint rva)
        {
            if (rva < SizeOfHeaders)
            {
                if (rva < (uint)Data.Length) return (int)rva;
                return -1;
            }
            for (int i = 0; i < Sections.Count; i++)
            {
                PeSection s = Sections[i];
                uint span = Math.Max(s.VirtualSize, s.RawSize);
                if (rva < s.VirtualAddress) continue;
                uint delta = rva - s.VirtualAddress;
                if (delta >= span) continue;
                if (delta >= s.RawSize) return -1;   // 落在未初始化数据里
                uint fo = s.RawPointer + delta;
                if (fo >= (uint)Data.Length) return -1;
                return (int)fo;
            }
            return -1;
        }

        /// <summary>读以 0 结尾的字符串。按字节直转 char，保持逐字节一致（不做编码转换）。</summary>
        public bool TryReadCString(uint rva, out string value)
        {
            value = null;
            int off = RvaToOffset(rva);
            if (off < 0) return false;
            int end = off;
            while (end < Data.Length && Data[end] != 0) end++;
            if (end >= Data.Length) return false;
            StringBuilder sb = new StringBuilder(end - off);
            for (int i = off; i < end; i++) sb.Append((char)Data[i]);
            value = sb.ToString();
            return true;
        }

        public static ushort U16(byte[] d, int o)
        {
            return (ushort)(d[o] | (d[o + 1] << 8));
        }

        public static uint U32(byte[] d, int o)
        {
            return (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
        }

        public static ulong U64(byte[] d, int o)
        {
            ulong lo = U32(d, o);
            ulong hi = U32(d, o + 4);
            return lo | (hi << 32);
        }
    }
}
