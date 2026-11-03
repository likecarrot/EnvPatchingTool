using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PeStubGen
{
    public class ProcessResult
    {
        public int ExitCode;
        public bool TimedOut;
        public string StdOut = "";
        public string StdErr = "";

        public string AllText
        {
            get
            {
                string s = "";
                if (!string.IsNullOrEmpty(StdOut)) s += StdOut;
                if (!string.IsNullOrEmpty(StdErr)) s += (s.Length > 0 ? "\r\n" : "") + StdErr;
                return s;
            }
        }
    }

    public static class ProcessRunner
    {
        public static ProcessResult Run(string exe, string args, string workDir, int timeoutMs)
        {
            return Run(exe, args, workDir, timeoutMs, null);
        }

        public static ProcessResult Run(string exe, string args, string workDir, int timeoutMs, Dictionary<string, string> environment)
        {
            ProcessResult r = new ProcessResult();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = Encoding.Default;
            psi.StandardErrorEncoding = Encoding.Default;
            if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
            // 注意：不要碰 psi.EnvironmentVariables —— .NET Framework 会把进程环境变量
            // 合并成一个大小写敏感的表，遇到同时存在 NO_PROXY / no_proxy 这类键就会抛异常。
            // 改用进程级环境变量：子进程会继承，跑完还原。
            Dictionary<string, string> saved = null;
            if (environment != null && environment.Count > 0)
            {
                saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, string> kv in environment)
                {
                    saved[kv.Key] = Environment.GetEnvironmentVariable(kv.Key);
                    Environment.SetEnvironmentVariable(kv.Key, kv.Value);
                }
            }

            try
            {
                using (Process p = new Process())
                {
                    StringBuilder so = new StringBuilder();
                    StringBuilder se = new StringBuilder();
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) so.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) se.AppendLine(e.Data);
                    };

                    try
                    {
                        p.Start();
                    }
                    catch (Exception ex)
                    {
                        r.ExitCode = -1;
                        r.StdErr = "无法启动进程：" + ex.Message;
                        return r;
                    }

                    p.StandardInput.Close();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        r.TimedOut = true;
                        try { p.Kill(); } catch { }
                    }
                    else
                    {
                        p.WaitForExit();   // 确保异步输出读完
                    }

                    r.StdOut = so.ToString();
                    r.StdErr = se.ToString();
                    if (!r.TimedOut)
                    {
                        try { r.ExitCode = p.ExitCode; } catch { r.ExitCode = -1; }
                    }
                    else
                    {
                        r.ExitCode = -1;
                    }
                }
                return r;
            }
            finally
            {
                if (saved != null)
                {
                    foreach (KeyValuePair<string, string> kv in saved)
                    {
                        Environment.SetEnvironmentVariable(kv.Key, kv.Value);
                    }
                }
            }
        }

        /// <summary>
        /// net48 没有 ProcessStartInfo.ArgumentList，必须自己拼命令行并正确转义。
        /// 规则遵循 CommandLineToArgvW：含空格/制表符/引号才加引号；
        /// 引号内的 \ 只在"后面紧跟引号"或"位于末尾"时才成对翻倍。
        /// </summary>
        public static string QuoteArg(string arg)
        {
            if (arg == null) return "\"\"";
            if (arg.Length == 0) return "\"\"";
            bool need = false;
            for (int i = 0; i < arg.Length; i++)
            {
                char c = arg[i];
                if (c == ' ' || c == '\t' || c == '"') { need = true; break; }
            }
            if (!need) return arg;

            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            for (int i = 0; i < arg.Length; i++)
            {
                char c = arg[i];
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                }
                else
                {
                    if (backslashes > 0) sb.Append('\\', backslashes);
                    backslashes = 0;
                    sb.Append(c);
                }
            }
            if (backslashes > 0) sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string JoinArgs(IEnumerable<string> args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(QuoteArg(a));
            }
            return sb.ToString();
        }
    }

    public class GccProbe
    {
        public bool Ok;
        public string Path;
        public string Triple;
        public int Bits;          // 32 / 64 / 0=未知
        public string Version = "";
        public string Error = "";

        public string Summary
        {
            get
            {
                if (!Ok) return "不可用：" + Error;
                return Path + "  [" + Triple + ", " + Bits + " 位]  " + Version;
            }
        }
    }

    public static class Toolchain
    {
        private static readonly string[] CandidateNames = new string[]
        {
            "i686-w64-mingw32-gcc", "mingw32-gcc", "gcc", "x86_64-w64-mingw32-gcc", "clang"
        };

        /// <summary>在 PATH 里找一个能用的 gcc。</summary>
        public static string FindInPath()
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar)) return null;
            string[] dirs = pathVar.Split(';');
            for (int i = 0; i < CandidateNames.Length; i++)
            {
                for (int d = 0; d < dirs.Length; d++)
                {
                    string dir = dirs[d];
                    if (string.IsNullOrEmpty(dir)) continue;
                    string candidate;
                    try { candidate = Path.Combine(dir.Trim(), CandidateNames[i] + ".exe"); }
                    catch { continue; }
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        public static GccProbe Probe(string gccPath)
        {
            GccProbe p = new GccProbe();
            p.Path = gccPath;
            if (string.IsNullOrEmpty(gccPath))
            {
                p.Error = "未指定 gcc 路径";
                return p;
            }
            if (!File.Exists(gccPath))
            {
                string full = FindInPath();
                if (full == null || !string.Equals(full, gccPath, StringComparison.OrdinalIgnoreCase))
                {
                    p.Error = "文件不存在：" + gccPath;
                    return p;
                }
            }

            ProcessResult r = ProcessRunner.Run(gccPath, "-dumpmachine", null, 15000);
            if (r.TimedOut)
            {
                p.Error = "执行 -dumpmachine 超时";
                return p;
            }
            if (r.ExitCode != 0)
            {
                p.Error = "执行 -dumpmachine 失败（退出码 " + r.ExitCode + "）：" + Trim(r.AllText, 200);
                return p;
            }

            p.Triple = r.StdOut.Trim();
            string t = p.Triple.ToLowerInvariant();
            if (t.IndexOf("x86_64") >= 0 || t.IndexOf("amd64") >= 0) p.Bits = 64;
            else if (t.IndexOf("i686") >= 0 || t.IndexOf("i586") >= 0 || t.IndexOf("mingw32") >= 0) p.Bits = 32;
            else p.Bits = 0;

            ProcessResult v = ProcessRunner.Run(gccPath, "--version", null, 15000);
            if (v.ExitCode == 0)
            {
                string first = v.StdOut.Replace("\r", "").Split('\n')[0].Trim();
                p.Version = first;
            }

            if (p.Bits == 0)
            {
                p.Error = "无法从 triple \"" + p.Triple + "\" 判断目标位数";
                return p;
            }

            p.Ok = true;
            return p;
        }

        public static string CheckForTarget(GccProbe probe, bool is64Target)
        {
            if (probe == null) return "尚未检测编译器。";
            if (!probe.Ok) return probe.Error;
            int want = is64Target ? 64 : 32;
            if (probe.Bits != want)
                return "编译器目标为 " + probe.Bits + " 位，但目标程序是 " + want + " 位，无法使用：" + probe.Path;
            return null;
        }

        public static ProcessResult CompileShared(string gccPath, string workDir, string baseName, int timeoutMs)
        {
            List<string> args = new List<string>();
            args.Add("-shared");
            args.Add("-s");
            args.Add("-o");
            args.Add(baseName + ".dll");
            args.Add(baseName + ".c");
            args.Add(baseName + ".def");
            // gcc 自己也要写临时文件；把它指到工作目录，避免 %TEMP% 不可写时整个编译失败。
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["TMP"] = workDir;
            env["TEMP"] = workDir;
            env["TMPDIR"] = workDir;
            return ProcessRunner.Run(gccPath, ProcessRunner.JoinArgs(args), workDir, timeoutMs, env);
        }

        public static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }
    }
}
