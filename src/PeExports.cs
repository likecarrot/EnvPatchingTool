using System;
using System.Collections.Generic;

namespace PeStubGen
{
    public class ExportEntry
    {
        public string Name;
        public int Ordinal;
        public uint Rva;

        public string Display
        {
            get { return Name == null ? ("#" + Ordinal) : Name; }
        }
    }

    /// <summary>读导出表。用于自检：编译完的桩 DLL 导出的名字，必须和计划完全一致。</summary>
    public static class PeExports
    {
        private const int MaxExports = 65536;

        public static List<ExportEntry> Read(PeFile pe)
        {
            List<ExportEntry> list = new List<ExportEntry>();
            uint rva = pe.DirRva(0);   // IMAGE_DIRECTORY_ENTRY_EXPORT
            if (rva == 0) return list;
            int off = pe.RvaToOffset(rva);
            if (off < 0) return list;
            if (off + 40 > pe.Data.Length) return list;

            uint baseOrdinal = PeFile.U32(pe.Data, off + 16);
            uint numFunctions = PeFile.U32(pe.Data, off + 20);
            uint numNames = PeFile.U32(pe.Data, off + 24);
            uint addrFunctions = PeFile.U32(pe.Data, off + 28);
            uint addrNames = PeFile.U32(pe.Data, off + 32);
            uint addrOrdinals = PeFile.U32(pe.Data, off + 36);

            if (numFunctions > MaxExports) numFunctions = MaxExports;
            if (numNames > MaxExports) numNames = MaxExports;

            Dictionary<int, string> nameByIndex = new Dictionary<int, string>();
            int nameOff = pe.RvaToOffset(addrNames);
            int ordOff = pe.RvaToOffset(addrOrdinals);
            if (nameOff >= 0 && ordOff >= 0)
            {
                for (uint i = 0; i < numNames; i++)
                {
                    int no = nameOff + (int)i * 4;
                    int oo = ordOff + (int)i * 2;
                    if (no + 4 > pe.Data.Length || oo + 2 > pe.Data.Length) break;
                    uint nrva = PeFile.U32(pe.Data, no);
                    ushort idx = PeFile.U16(pe.Data, oo);
                    string nm;
                    if (pe.TryReadCString(nrva, out nm)) nameByIndex[idx] = nm;
                }
            }

            int funcOff = pe.RvaToOffset(addrFunctions);
            if (funcOff < 0) return list;

            for (uint i = 0; i < numFunctions; i++)
            {
                int fo = funcOff + (int)i * 4;
                if (fo + 4 > pe.Data.Length) break;
                uint frva = PeFile.U32(pe.Data, fo);
                if (frva == 0) continue;

                ExportEntry e = new ExportEntry();
                e.Ordinal = (int)baseOrdinal + (int)i;
                e.Rva = frva;
                string nm;
                if (nameByIndex.TryGetValue((int)i, out nm)) e.Name = nm;
                list.Add(e);
            }

            return list;
        }
    }
}
