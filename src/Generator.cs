using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PeStubGen
{
    public class DllBuildResult
    {
        public string FileName;      // RoleView.dll
        public string BaseName;      // RoleView
        public int SymbolCount;
        public bool SourceWritten;
        public bool Compiled;
        public bool Verified;
        public string Error;
        public List<string> Missing = new List<string>();
        public List<string> Extra = new List<string>();
        public string DllPath;
        public string GccOutput;

        public string Status
        {
            get
            {
                if (!string.IsNullOrEmpty(Error)) return "失败";
                if (Compiled && Verified) return "成功";
                if (Compiled) return "已编译（未校验）";
                return "仅源码";
            }
        }
    }

    public class GenerateResult
    {
        public string OutputDir;
        public string SrcDir;
        public string LogPath;
        public string ManifestPath;
        public string Error;
        public bool CompileAttempted;
        public List<DllBuildResult> Items = new List<DllBuildResult>();

        public int OkCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Items.Count; i++)
                    if (Items[i].Compiled && Items[i].Verified && string.IsNullOrEmpty(Items[i].Error)) n++;
                return n;
            }
        }

        public int FailCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Items.Count; i++)
                    if (!string.IsNullOrEmpty(Items[i].Error)) n++;
                return n;
            }
        }
    }

    public class Generator
    {
        private readonly Action<string> _sink;
        private readonly StringBuilder _buffer = new StringBuilder();

        public Generator(Action<string> log)
        {
            _sink = log;
        }

        private void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            _buffer.AppendLine(line);
            if (_sink != null)
            {
                try { _sink(line); }
                catch { }
            }
        }

        public GenerateResult Run(PeInfo info, GenerateOptions opt)
        {
            GenerateResult result = new GenerateResult();
            result.OutputDir = opt.OutputDir;
            result.SrcDir = Path.Combine(opt.OutputDir, "src");
            result.CompileAttempted = opt.Compile;

            if (info == null) { result.Error = "没有解析结果。"; return result; }
            if (string.IsNullOrEmpty(opt.OutputDir)) { result.Error = "输出目录为空。"; return result; }

            List<ImportedModule> targets = new List<ImportedModule>();
            for (int i = 0; i < info.Modules.Count; i++)
            {
                ImportedModule m = info.Modules[i];
                if (opt.DllFileNames != null && opt.DllFileNames.Count > 0)
                {
                    bool hit = false;
                    for (int k = 0; k < opt.DllFileNames.Count; k++)
                    {
                        if (string.Equals(opt.DllFileNames[k], m.FileName, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                    }
                    if (!hit) continue;
                }
                targets.Add(m);
            }

            if (targets.Count == 0)
            {
                result.Error = "没有选中任何用户 DLL，未生成任何文件。";
                Log(result.Error);
                return result;
            }

            UTF8Encoding utf8 = new UTF8Encoding(false);
            string tempRoot = null;
            string sessionDir = null;

            try
            {
                Directory.CreateDirectory(result.OutputDir);
                Directory.CreateDirectory(result.SrcDir);
                Log("输出目录：" + result.OutputDir);

                // 1) 生成源码
                List<StubPlan> plans = new List<StubPlan>();
                for (int i = 0; i < targets.Count; i++)
                {
                    ImportedModule m = targets[i];
                    StubPlan plan = StubGenerator.Build(m);
                    plans.Add(plan);

                    DllBuildResult item = new DllBuildResult();
                    item.FileName = m.FileName;
                    item.BaseName = m.BaseName;
                    item.SymbolCount = plan.Exports.Count;
                    result.Items.Add(item);

                    string cPath = Path.Combine(result.SrcDir, m.BaseName + ".c");
                    string defPath = Path.Combine(result.SrcDir, m.BaseName + ".def");
                    File.WriteAllText(cPath, plan.CText, utf8);
                    File.WriteAllText(defPath, plan.DefText, utf8);
                    item.SourceWritten = true;

                    Log("生成源码 " + m.FileName + " → " + plan.Exports.Count + " 个导出");
                }

                // 2) 编译
                if (opt.Compile)
                {
                    GccProbe probe = Toolchain.Probe(opt.GccPath);
                    string problem = Toolchain.CheckForTarget(probe, info.Is64Bit);
                    if (problem != null)
                    {
                        throw new InvalidOperationException("编译器不可用：" + problem);
                    }
                    Log("编译器：" + probe.Summary);

                    tempRoot = AsciiTempRoot(result.OutputDir);
                    sessionDir = Path.Combine(tempRoot, "build_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                    Directory.CreateDirectory(sessionDir);
                    Log("编译临时目录：" + sessionDir);

                    for (int i = 0; i < plans.Count; i++)
                    {
                        StubPlan plan = plans[i];
                        DllBuildResult item = result.Items[i];
                        CompileOne(probe, plan, item, sessionDir, result.OutputDir, utf8);
                        Log("  " + item.FileName + " → " + item.Status
                            + (string.IsNullOrEmpty(item.Error) ? "" : "：" + item.Error));
                    }
                }

                // 3) 写日志和清单
                result.LogPath = Path.Combine(result.OutputDir, "build.log");
                result.ManifestPath = Path.Combine(result.OutputDir, "manifest.json");
                File.WriteAllText(result.LogPath, _buffer.ToString(), utf8);
                File.WriteAllText(result.ManifestPath, BuildManifest(info, opt, result), utf8);
                Log("已写出：" + Path.GetFileName(result.LogPath) + " / " + Path.GetFileName(result.ManifestPath));
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Log("错误：" + ex.Message);
                if (!string.IsNullOrEmpty(result.OutputDir))
                {
                    try
                    {
                        Directory.CreateDirectory(result.OutputDir);
                        result.LogPath = Path.Combine(result.OutputDir, "build.log");
                        File.WriteAllText(result.LogPath, _buffer.ToString(), new UTF8Encoding(false));
                    }
                    catch { }
                }
            }
            finally
            {
                if (sessionDir != null)
                {
                    try { Directory.Delete(sessionDir, true); } catch { }
                }
            }

            return result;
        }

        private void CompileOne(GccProbe probe, StubPlan plan, DllBuildResult item, string sessionDir, string outputDir, Encoding utf8)
        {
            string work = Path.Combine(sessionDir, plan.Module.BaseName);
            try
            {
                Directory.CreateDirectory(work);
                File.WriteAllText(Path.Combine(work, plan.Module.BaseName + ".c"), plan.CText, utf8);
                File.WriteAllText(Path.Combine(work, plan.Module.BaseName + ".def"), plan.DefText, utf8);

                ProcessResult pr = Toolchain.CompileShared(probe.Path, work, plan.Module.BaseName, 120000);
                item.GccOutput = pr.AllText;

                string produced = Path.Combine(work, plan.Module.BaseName + ".dll");
                if (pr.TimedOut)
                {
                    item.Error = "gcc 超时（120 秒）";
                    return;
                }
                if (pr.ExitCode != 0 || !File.Exists(produced))
                {
                    item.Error = "gcc 退出码 " + pr.ExitCode + "：" + Toolchain.Trim(pr.AllText, 400);
                    return;
                }

                item.Compiled = true;

                // 自校验：读回导出表，和计划逐项比对
                List<ExportEntry> actual = PeExports.Read(PeFile.Load(produced));
                CompareExports(plan, actual, item);

                string dest = Path.Combine(outputDir, plan.Module.FileName);
                File.Copy(produced, dest, true);
                item.DllPath = dest;

                long size = new FileInfo(dest).Length;
                Log("  已写出 " + dest + "（" + size + " 字节）");
            }
            catch (Exception ex)
            {
                item.Error = ex.Message;
            }
        }

        private static void CompareExports(StubPlan plan, List<ExportEntry> actual, DllBuildResult item)
        {
            HashSet<string> expected = new HashSet<string>();
            for (int i = 0; i < plan.Exports.Count; i++)
            {
                StubExport e = plan.Exports[i];
                expected.Add(e.ByOrdinal ? ("#" + e.Ordinal) : ("n:" + e.Name));
            }

            HashSet<string> got = new HashSet<string>();
            for (int i = 0; i < actual.Count; i++)
            {
                ExportEntry e = actual[i];
                got.Add(e.Name == null ? ("#" + e.Ordinal) : ("n:" + e.Name));
            }

            List<string> missing = new List<string>();
            foreach (string s in expected) if (!got.Contains(s)) missing.Add(s);
            List<string> extra = new List<string>();
            foreach (string s in got) if (!expected.Contains(s)) extra.Add(s);

            item.Missing = missing;
            item.Extra = extra;
            item.Verified = missing.Count == 0 && extra.Count == 0;
            if (!item.Verified)
            {
                item.Error = "导出表与导入表不一致（缺 " + missing.Count + "，多 " + extra.Count + "）";
            }
        }

        private string BuildManifest(PeInfo info, GenerateOptions opt, GenerateResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"tool\": \"PeStubGen\",");
            sb.AppendLine("  \"generatedAt\": " + Json.Quote(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + ",");
            sb.AppendLine("  \"sourceExe\": " + Json.Quote(opt.ExePath) + ",");
            sb.AppendLine("  \"sha256\": " + Json.Quote(Sha256OfFile(opt.ExePath)) + ",");
            sb.AppendLine("  \"arch\": " + Json.Quote(info.ArchName) + ",");
            sb.AppendLine("  \"imageBase\": " + Json.Quote("0x" + info.ImageBase.ToString("X")) + ",");
            sb.AppendLine("  \"importModuleCount\": " + Json.Number(info.Modules.Count) + ",");
            sb.AppendLine("  \"importSymbolCount\": " + Json.Number(info.SymbolCount) + ",");
            sb.AppendLine("  \"gcc\": " + Json.Quote(opt.GccTriple == null ? opt.GccPath : (opt.GccPath + " [" + opt.GccTriple + "]")) + ",");
            sb.AppendLine("  \"stubs\": [");

            for (int i = 0; i < result.Items.Count; i++)
            {
                DllBuildResult it = result.Items[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"dll\": " + Json.Quote(it.FileName) + ",");
                sb.AppendLine("      \"exports\": " + Json.Number(it.SymbolCount) + ",");
                sb.AppendLine("      \"status\": " + Json.Quote(it.Status) + ",");
                sb.AppendLine("      \"verified\": " + Json.Bool(it.Verified) + (string.IsNullOrEmpty(it.Error) ? "" : ","));
                if (!string.IsNullOrEmpty(it.Error))
                    sb.AppendLine("      \"error\": " + Json.Quote(it.Error));
                sb.AppendLine("    }" + (i == result.Items.Count - 1 ? "" : ","));
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string Sha256OfFile(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 找一个"能写"的临时根目录，并且尽量是纯 ASCII 路径（老 gcc/ld 对中文路径不友好）。
        /// 顺序：%TEMP% → 系统盘 \PeStubGenTemp → 输出目录\.buildtmp。
        /// 每个候选都实际探测过可写性，受限环境里也不会一开始就失败。
        /// </summary>
        private static string AsciiTempRoot(string outputDir)
        {
            List<string> candidates = new List<string>();

            string temp = Path.GetTempPath();
            if (!string.IsNullOrEmpty(temp) && IsAscii(temp)) candidates.Add(temp);

            string drive = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(drive)) drive = "C:\\";
            candidates.Add(Path.Combine(drive, "PeStubGenTemp"));

            if (!string.IsNullOrEmpty(outputDir)) candidates.Add(Path.Combine(outputDir, ".buildtmp"));

            for (int i = 0; i < candidates.Count; i++)
            {
                string c = candidates[i];
                if (TryWritable(c)) return c;
            }

            // 实在都不行，就把问题明确抛出去
            throw new IOException("找不到可写的临时目录（试过 %TEMP%、系统盘和输出目录）。");
        }

        private static bool IsAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > 127) return false;
            }
            return true;
        }

        private static bool TryWritable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, "probe_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(probe);
                Directory.Delete(probe, true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
