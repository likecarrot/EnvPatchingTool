using System;
using System.Collections.Generic;
using System.Text;

namespace PeStubGen
{
    /// <summary>解析导入表（普通导入 + 延迟导入）。</summary>
    public static class PeImports
    {
        private const int MaxDescriptors = 4096;
        private const int MaxThunks = 65536;

        public static List<ImportedModule> Read(PeFile pe)
        {
            List<ImportedModule> modules = new List<ImportedModule>();
            ReadStandard(pe, modules);
            ReadDelay(pe, modules);
            return Merge(modules);
        }

        private static void ReadStandard(PeFile pe, List<ImportedModule> modules)
        {
            uint rva = pe.DirRva(1);   // IMAGE_DIRECTORY_ENTRY_IMPORT
            if (rva == 0) return;
            int off = pe.RvaToOffset(rva);
            if (off < 0)
                throw new PeFormatException("导入表 RVA 无法映射到文件，PE 文件可能被截断。");

            for (int i = 0; i < MaxDescriptors; i++)
            {
                int d = off + i * 20;
                if (d + 20 > pe.Data.Length) break;
                uint oft = PeFile.U32(pe.Data, d + 0);
                uint nameRva = PeFile.U32(pe.Data, d + 12);
                uint ft = PeFile.U32(pe.Data, d + 16);
                if (oft == 0 && nameRva == 0 && ft == 0) break;

                string name;
                if (!pe.TryReadCString(nameRva, out name) || string.IsNullOrEmpty(name)) continue;

                ImportedModule m = new ImportedModule();
                m.RawName = name;
                m.FileName = NormalizeFileName(name);
                m.DelayLoaded = false;
                ReadThunks(pe, oft != 0 ? oft : ft, m);
                modules.Add(m);
            }
        }

        private static void ReadDelay(PeFile pe, List<ImportedModule> modules)
        {
            uint rva = pe.DirRva(13);  // IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT
            if (rva == 0) return;
            int off = pe.RvaToOffset(rva);
            if (off < 0) return;

            for (int i = 0; i < MaxDescriptors; i++)
            {
                int d = off + i * 32;
                if (d + 32 > pe.Data.Length) break;
                uint attr = PeFile.U32(pe.Data, d + 0);
                uint nameRva = PeFile.U32(pe.Data, d + 4);
                uint intRva = PeFile.U32(pe.Data, d + 16);
                if (nameRva == 0 && intRva == 0) break;

                // Attributes 的第 0 位若为 0，说明字段是 VA 而不是 RVA（老式 VC6 产物）。
                if ((attr & 1) == 0)
                {
                    nameRva = VaToRva(pe, nameRva);
                    intRva = VaToRva(pe, intRva);
                }

                string name;
                if (!pe.TryReadCString(nameRva, out name) || string.IsNullOrEmpty(name)) continue;

                ImportedModule m = new ImportedModule();
                m.RawName = name;
                m.FileName = NormalizeFileName(name);
                m.DelayLoaded = true;
                if (intRva != 0) ReadThunks(pe, intRva, m);
                if (m.Symbols.Count > 0) modules.Add(m);
            }
        }

        private static uint VaToRva(PeFile pe, uint va)
        {
            if (va > pe.ImageBase) return (uint)(va - pe.ImageBase);
            return va;
        }

        private static void ReadThunks(PeFile pe, uint thunkRva, ImportedModule m)
        {
            int off = pe.RvaToOffset(thunkRva);
            if (off < 0) return;
            int size = pe.Is64 ? 8 : 4;

            for (int i = 0; i < MaxThunks; i++)
            {
                int o = off + i * size;
                if (o + size > pe.Data.Length) break;
                ulong v = pe.Is64 ? PeFile.U64(pe.Data, o) : (ulong)PeFile.U32(pe.Data, o);
                if (v == 0) break;

                ulong flag = pe.Is64 ? 0x8000000000000000UL : 0x80000000UL;
                ImportedSymbol sym = new ImportedSymbol();
                sym.ModuleName = m.RawName;

                if ((v & flag) != 0)
                {
                    sym.ByOrdinal = true;
                    sym.Ordinal = (int)(v & 0xFFFFUL);
                    m.Symbols.Add(sym);
                }
                else
                {
                    uint nameRva = (uint)(v & 0x7FFFFFFFUL);
                    string name;
                    if (pe.TryReadCString(nameRva + 2, out name) && name.Length > 0)
                    {
                        sym.Name = name;
                        m.Symbols.Add(sym);
                    }
                }
            }
        }

        public static string NormalizeFileName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string s = raw.Trim();
            s = s.Replace('/', '\\');
            int slash = s.LastIndexOf('\\');
            if (slash >= 0) s = s.Substring(slash + 1);
            if (s.Length == 0) return raw;
            if (s.IndexOf('.') < 0) s += ".dll";
            return s;
        }

        /// <summary>同名模块（大小写不敏感）合并，符号去重。</summary>
        private static List<ImportedModule> Merge(List<ImportedModule> modules)
        {
            List<ImportedModule> result = new List<ImportedModule>();
            Dictionary<string, ImportedModule> map = new Dictionary<string, ImportedModule>();

            for (int i = 0; i < modules.Count; i++)
            {
                ImportedModule m = modules[i];
                ImportedModule exist;
                if (map.TryGetValue(m.Key, out exist))
                {
                    if (!m.DelayLoaded) exist.DelayLoaded = false;
                    HashSet<string> seen = new HashSet<string>();
                    for (int k = 0; k < exist.Symbols.Count; k++) seen.Add(SymKey(exist.Symbols[k]));
                    for (int k = 0; k < m.Symbols.Count; k++)
                    {
                        if (seen.Add(SymKey(m.Symbols[k]))) exist.Symbols.Add(m.Symbols[k]);
                    }
                }
                else
                {
                    map[m.Key] = m;
                    result.Add(m);
                }
            }
            return result;
        }

        private static string SymKey(ImportedSymbol s)
        {
            return s.ByOrdinal ? ("#" + s.Ordinal) : ("n:" + s.Name);
        }
    }
}
