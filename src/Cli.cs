using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PeStubGen
{
    /// <summary>命令行入口：用于自动化自测（也方便你在没有界面的环境里跑）。</summary>
    public static class CliProgram
    {
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch { }

            try
            {
                if (args.Length == 0) { Usage(); return 2; }
                string cmd = args[0].ToLowerInvariant();
                switch (cmd)
                {
                    case "parse": return CmdParse(args);
                    case "classify": return CmdClassify(args);
                    case "exports": return CmdExports(args);
                    case "probe": return CmdProbe(args);
                    case "gen": return CmdGen(args);
                    default: Usage(); return 2;
                }
            }
            catch (PeFormatException ex)
            {
                Console.Error.WriteLine("PE 解析失败：" + ex.Message);
                return 3;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("错误：" + ex.Message);
                return 1;
            }
        }

        private static void Usage()
        {
            Console.WriteLine("PeStubGen.Cli 用法：");
            Console.WriteLine("  parse <exe>                     解析 PE 头与导入表");
            Console.WriteLine("  classify <exe>                  解析并判定每个模块是系统还是用户 DLL");
            Console.WriteLine("  exports <dll>                   讀出 DLL 的导出表");
            Console.WriteLine("  probe [--gcc <path>]            检测 gcc 及其目标位数");
            Console.WriteLine("  gen <exe> [--out <dir>] [--no-compile] [--gcc <path>] [--dll a.dll,b.dll]");
        }

        private static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int CmdParse(string[] args)
        {
            if (args.Length < 2) { Console.Error.WriteLine("缺少 exe 路径"); return 2; }
            PeInfo info = PeImageReader.Read(args[1]);
            PrintInfo(info);
            return 0;
        }

        private static int CmdClassify(string[] args)
        {
            if (args.Length < 2) { Console.Error.WriteLine("缺少 exe 路径"); return 2; }
            PeInfo info = PeImageReader.Read(args[1]);
            PrintInfo(info);
            string exeDir = Path.GetDirectoryName(Path.GetFullPath(args[1]));
            int user = 0;
            Console.WriteLine();
            Console.WriteLine("判定结果：");
            for (int i = 0; i < info.Modules.Count; i++)
            {
                ImportedModule m = info.Modules[i];
                DllVerdict v = DllClassifier.Classify(m, info.Is64Bit, exeDir);
                if (!v.IsSystem) user++;
                Console.WriteLine(string.Format("  [{0}] {1,-24} {2,3} 个符号  {3}  ({4})",
                    v.IsSystem ? "系统" : "用户", m.FileName, m.Symbols.Count, v.SourceText, v.Note));
            }
            Console.WriteLine();
            Console.WriteLine("用户 DLL：" + user + " 个");
            return 0;
        }

        private static int CmdExports(string[] args)
        {
            if (args.Length < 2) { Console.Error.WriteLine("缺少 dll 路径"); return 2; }
            PeFile pe = PeFile.Load(args[1]);
            List<ExportEntry> list = PeExports.Read(pe);
            Console.WriteLine(args[1] + "  (" + pe.Machine + ", " + (pe.Is64 ? "x64" : "x86") + ")");
            Console.WriteLine("导出数量：" + list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Console.WriteLine("  #" + list[i].Ordinal + "  " + (list[i].Name == null ? "(仅序号)" : list[i].Name));
            }
            return 0;
        }

        private static int CmdProbe(string[] args)
        {
            string gcc = ArgValue(args, "--gcc");
            if (string.IsNullOrEmpty(gcc)) gcc = Toolchain.FindInPath();
            if (string.IsNullOrEmpty(gcc))
            {
                Console.WriteLine("PATH 里没找到 gcc。");
                return 4;
            }
            GccProbe p = Toolchain.Probe(gcc);
            Console.WriteLine("路径：" + p.Path);
            Console.WriteLine("triple：" + (p.Triple == null ? "" : p.Triple));
            Console.WriteLine("位数：" + p.Bits);
            Console.WriteLine("版本：" + p.Version);
            Console.WriteLine("可用：" + (p.Ok ? "是" : "否 — " + p.Error));
            return p.Ok ? 0 : 4;
        }

        private static int CmdGen(string[] args)
        {
            if (args.Length < 2) { Console.Error.WriteLine("缺少 exe 路径"); return 2; }
            string exePath = Path.GetFullPath(args[1]);
            PeInfo info = PeImageReader.Read(exePath);
            PrintInfo(info);

            string exeDir = Path.GetDirectoryName(exePath);
            string outDir = ArgValue(args, "--out");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(exeDir, Path.GetFileNameWithoutExtension(exePath) + "环境");

            GenerateOptions opt = new GenerateOptions();
            opt.ExePath = exePath;
            opt.OutputDir = Path.GetFullPath(outDir);
            opt.Compile = !HasFlag(args, "--no-compile");
            opt.GccPath = ArgValue(args, "--gcc");
            if (string.IsNullOrEmpty(opt.GccPath)) opt.GccPath = Toolchain.FindInPath();

            string dlls = ArgValue(args, "--dll");
            if (!string.IsNullOrEmpty(dlls))
            {
                string[] parts = dlls.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length > 0) opt.DllFileNames.Add(PeImports.NormalizeFileName(p));
                }
            }
            else
            {
                for (int i = 0; i < info.Modules.Count; i++)
                {
                    ImportedModule m = info.Modules[i];
                    DllVerdict v = DllClassifier.Classify(m, info.Is64Bit, exeDir);
                    if (!v.IsSystem) opt.DllFileNames.Add(m.FileName);
                }
            }

            if (opt.Compile)
            {
                GccProbe probe = Toolchain.Probe(opt.GccPath);
                string problem = Toolchain.CheckForTarget(probe, info.Is64Bit);
                if (problem != null)
                {
                    Console.Error.WriteLine("编译器不可用：" + problem);
                    return 4;
                }
                opt.GccTriple = probe.Triple;
                opt.GccPath = probe.Path;
            }

            Generator gen = new Generator(delegate(string line) { Console.WriteLine(line); });
            GenerateResult r = gen.Run(info, opt);

            Console.WriteLine();
            Console.WriteLine("输出目录：" + r.OutputDir);
            Console.WriteLine("成功：" + r.OkCount + "  失败：" + r.FailCount + "  合计：" + r.Items.Count);
            if (!string.IsNullOrEmpty(r.Error)) Console.Error.WriteLine("错误：" + r.Error);
            return (r.FailCount == 0 && string.IsNullOrEmpty(r.Error)) ? 0 : 5;
        }

        private static void PrintInfo(PeInfo info)
        {
            Console.WriteLine("文件：" + info.FilePath);
            Console.WriteLine("位数：" + info.ArchName + "  Machine：" + info.MachineText
                + "  ImageBase：0x" + info.ImageBase.ToString("X")
                + "  SizeOfImage：0x" + info.SizeOfImage.ToString("X"));
            Console.WriteLine("导入模块：" + info.Modules.Count + "  导入符号：" + info.SymbolCount);
            for (int i = 0; i < info.Modules.Count; i++)
            {
                ImportedModule m = info.Modules[i];
                Console.WriteLine("  " + m.FileName + "  (" + m.Symbols.Count + ")" + (m.DelayLoaded ? " [延迟导入]" : ""));
                for (int k = 0; k < m.Symbols.Count; k++)
                {
                    Console.WriteLine("      " + m.Symbols[k].Display);
                }
            }
        }
    }
}
