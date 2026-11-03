using System;
using System.Collections.Generic;

namespace PeStubGen
{
    /// <summary>导入表里的单个符号：要么有名字，要么只有序号。</summary>
    public class ImportedSymbol
    {
        public string ModuleName;
        public string Name;      // null 表示按序号导入
        public int Ordinal;      // 仅当 ByOrdinal 为 true 时有效
        public bool ByOrdinal;

        public string Display
        {
            get { return ByOrdinal ? ("#" + Ordinal.ToString()) : Name; }
        }
    }

    /// <summary>导入表里的一个模块（DLL）。</summary>
    public class ImportedModule
    {
        public string RawName;   // 导入表里写的原文，可能不带 .dll
        public string FileName;  // 规范化为 xxx.dll
        public bool DelayLoaded;
        public List<ImportedSymbol> Symbols = new List<ImportedSymbol>();

        public string BaseName
        {
            get
            {
                if (string.IsNullOrEmpty(FileName)) return RawName;
                int dot = FileName.LastIndexOf('.');
                if (dot <= 0) return FileName;
                return FileName.Substring(0, dot);
            }
        }

        public string Key
        {
            get { return (FileName == null ? RawName : FileName).ToLowerInvariant(); }
        }
    }

    /// <summary>一次 PE 解析的完整结果。</summary>
    public class PeInfo
    {
        public string FilePath;
        public bool Is64Bit;
        public ushort Machine;
        public ulong ImageBase;
        public uint SizeOfImage;
        public long FileSize;
        public List<ImportedModule> Modules = new List<ImportedModule>();

        public int SymbolCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Modules.Count; i++) n += Modules[i].Symbols.Count;
                return n;
            }
        }

        public string ArchName { get { return Is64Bit ? "x64" : "x86"; } }

        public string MachineText
        {
            get
            {
                switch (Machine)
                {
                    case 0x014C: return "i386";
                    case 0x8664: return "AMD64";
                    case 0x01C0: return "ARM";
                    case 0xaa64: return "ARM64";
                    default: return "0x" + Machine.ToString("X4");
                }
            }
        }
    }

    public enum DllSource
    {
        Whitelist,
        SystemDirectory,
        LocalDirectory,
        Default,
        Manual
    }

    /// <summary>一个模块“是系统 DLL 还是用户 DLL”的判定结果。</summary>
    public class DllVerdict
    {
        public bool IsSystem;
        public DllSource Source;
        public string Note;

        public string SourceText
        {
            get
            {
                switch (Source)
                {
                    case DllSource.Whitelist: return "系统白名单";
                    case DllSource.SystemDirectory: return "系统目录存在";
                    case DllSource.LocalDirectory: return "EXE 同目录存在";
                    case DllSource.Default: return "默认判定";
                    case DllSource.Manual: return "手动指定";
                    default: return "";
                }
            }
        }
    }

    /// <summary>一个用户 DLL 的桩计划：源码文本 + 预期导出。</summary>
    public class StubPlan
    {
        public ImportedModule Module;
        public List<StubExport> Exports = new List<StubExport>();
        public string DefText;
        public string CText;
    }

    public class StubExport
    {
        public string Name;      // null 表示只按序号导出
        public int Ordinal;
        public bool ByOrdinal;
        public string StubName;  // stub_1, stub_2 ...
    }

    public class GenerateOptions
    {
        public string ExePath;
        public string OutputDir;
        public bool Compile = true;
        public string GccPath;
        public string GccTriple;
        public List<string> DllFileNames = new List<string>();
    }
}
