using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PeStubGen
{
    public class AppSettings
    {
        public string GccPath = "";
        public string LastExeDir = "";
        public string LastOutputDir = "";
        public bool Compile = true;

        public static string DirectoryPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "PeStubGen");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "settings.json"); }
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (File.Exists(FilePath))
                {
                    string text = File.ReadAllText(FilePath, Encoding.UTF8);
                    Dictionary<string, object> d = Json.ParseFlatObject(text);
                    s.GccPath = Json.GetString(d, "gccPath", "");
                    s.LastExeDir = Json.GetString(d, "lastExeDir", "");
                    s.LastOutputDir = Json.GetString(d, "lastOutputDir", "");
                    s.Compile = Json.GetBool(d, "compile", true);
                }
            }
            catch
            {
                // 设置文件坏了不该拦住主流程，用默认值继续
            }

            if (string.IsNullOrEmpty(s.GccPath))
            {
                string found = Toolchain.FindInPath();
                if (found != null) s.GccPath = found;
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"gccPath\": " + Json.Quote(GccPath) + ",");
                sb.AppendLine("  \"lastExeDir\": " + Json.Quote(LastExeDir) + ",");
                sb.AppendLine("  \"lastOutputDir\": " + Json.Quote(LastOutputDir) + ",");
                sb.AppendLine("  \"compile\": " + Json.Bool(Compile));
                sb.AppendLine("}");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
