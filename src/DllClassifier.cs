using System;
using System.Collections.Generic;
using System.IO;

namespace PeStubGen
{
    /// <summary>
    /// 判定一个导入模块是不是"系统 DLL"。
    /// 规则（优先级从高到低）：系统白名单 → 系统目录里存在该文件 → 用户 DLL。
    /// 手动指定由界面层覆盖，不在本类里处理。
    /// </summary>
    public static class DllClassifier
    {
        // 只写不带扩展名的小写名字。
        private static readonly string[] KnownSystemDlls = new string[]
        {
            // 核心 / 内核
            "kernel32", "kernelbase", "ntdll", "user32", "gdi32", "advapi32", "shell32", "shlwapi",
            "ole32", "oleaut32", "oleacc", "comctl32", "comdlg32", "combase", "rpcrt4", "sechost",
            "userenv", "psapi", "version", "dbghelp", "imagehlp", "powrprof", "setupapi", "cfgmgr32",
            "wintrust", "crypt32", "bcrypt", "ncrypt", "secur32", "sspicli", "netapi32", "wldap32",
            "iphlpapi", "ws2_32", "wsock32", "mswsock", "dnsapi", "winhttp", "wininet", "urlmon",
            "winmm", "imm32", "msimg32", "gdiplus", "uxtheme", "dwmapi", "propsys", "wtsapi32",
            "mpr", "rasapi32", "tapi32", "winspool.drv", "odbc32", "oledlg", "msacm32", "msvfw32",
            "avifil32", "vfw32", "dsound", "dinput", "dinput8", "dplayx", "dxgi", "d3d8", "d3d9",
            "d3d11", "d3d12", "ddraw", "opengl32", "glu32", "normaliz", "winsta", "authz", "fltlib",
            // 运行库（SysWOW64 里都有，不能当用户 DLL 生成）
            "msvcrt", "msvcr70", "msvcr71", "msvcr80", "msvcr90", "msvcr100", "msvcr110", "msvcr120",
            "msvcp60", "msvcp70", "msvcp71", "msvcp80", "msvcp90", "msvcp100", "msvcp110", "msvcp120",
            "ucrtbase", "api-ms-win-crt-runtime-l1-1-0", "mfc42", "mfc42u", "mfc70", "mfc71",
            "mfc80", "mfc90", "mfc100", "mfc110", "mfc120", "vcruntime140", "msvcp140", "concrt140"
        };

        private static readonly HashSet<string> Whitelist = BuildWhitelist();

        private static HashSet<string> BuildWhitelist()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < KnownSystemDlls.Length; i++) set.Add(KnownSystemDlls[i]);
            return set;
        }

        public static bool IsWhitelisted(string baseName)
        {
            return !string.IsNullOrEmpty(baseName) && Whitelist.Contains(baseName);
        }

        public static string SystemDirectory(bool is64Target)
        {
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(windir)) windir = Environment.GetEnvironmentVariable("SystemRoot");
            return Path.Combine(windir, is64Target ? "System32" : "SysWOW64");
        }

        public static DllVerdict Classify(ImportedModule module, bool is64Target, string exeDirectory)
        {
            DllVerdict v = new DllVerdict();

            if (IsWhitelisted(module.BaseName))
            {
                v.IsSystem = true;
                v.Source = DllSource.Whitelist;
                v.Note = "命中系统 DLL 白名单";
                return v;
            }

            string sysDir = SystemDirectory(is64Target);
            string candidate = Path.Combine(sysDir, module.FileName);
            if (File.Exists(candidate))
            {
                v.IsSystem = true;
                v.Source = DllSource.SystemDirectory;
                v.Note = sysDir;
                return v;
            }

            if (!string.IsNullOrEmpty(exeDirectory))
            {
                string local = Path.Combine(exeDirectory, module.FileName);
                if (File.Exists(local))
                {
                    v.IsSystem = false;
                    v.Source = DllSource.LocalDirectory;
                    v.Note = "EXE 同目录已存在同名文件：" + local;
                    return v;
                }
            }

            v.IsSystem = false;
            v.Source = DllSource.Default;
            v.Note = "系统和本地都找不到，按用户 DLL 处理";
            return v;
        }
    }
}
