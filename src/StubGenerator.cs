using System;
using System.Collections.Generic;
using System.Text;

namespace PeStubGen
{
    /// <summary>把一个模块的导入符号翻译成 .def + .c 的文本。</summary>
    public static class StubGenerator
    {
        public static StubPlan Build(ImportedModule module)
        {
            StubPlan plan = new StubPlan();
            plan.Module = module;

            // 去重并保持导入顺序
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < module.Symbols.Count; i++)
            {
                ImportedSymbol s = module.Symbols[i];
                string key = s.ByOrdinal ? ("#" + s.Ordinal) : ("n:" + s.Name);
                if (!seen.Add(key)) continue;

                StubExport e = new StubExport();
                e.ByOrdinal = s.ByOrdinal;
                e.Name = s.Name;
                e.Ordinal = s.Ordinal;
                e.StubName = "stub_" + (plan.Exports.Count + 1).ToString();
                plan.Exports.Add(e);
            }

            plan.DefText = BuildDef(module, plan);
            plan.CText = BuildC(module, plan);
            return plan;
        }

        private static string BuildDef(ImportedModule module, StubPlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; 自动生成，请勿手工修改：仅为满足 Windows 加载器解析导入表。");
            sb.AppendLine("; 源模块：" + module.RawName);
            sb.AppendLine("LIBRARY " + module.BaseName + ".dll");
            sb.AppendLine("EXPORTS");
            for (int i = 0; i < plan.Exports.Count; i++)
            {
                StubExport e = plan.Exports[i];
                if (e.ByOrdinal)
                {
                    // 只按序号导出，没有名字
                    sb.AppendLine("    " + e.StubName + " @" + e.Ordinal + " NONAME");
                }
                else
                {
                    sb.AppendLine("    " + DefName(e.Name) + "=" + e.StubName);
                }
            }
            return sb.ToString();
        }

        private static string BuildC(ImportedModule module, StubPlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("/* 自动生成，请勿手工修改。");
            sb.AppendLine(" * 源模块：" + module.RawName);
            sb.AppendLine(" * 函数体为空：这些桩只负责让加载器能解析导入表，被真正调用会崩溃。");
            sb.AppendLine(" */");
            sb.AppendLine();
            for (int i = 0; i < plan.Exports.Count; i++)
            {
                sb.AppendLine("void " + plan.Exports[i].StubName + "(void) {}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// .def 里的导出名。GNU ld 把行内的 @ 当作"序号"语法，
        /// 所以 C++ 修饰名（含 ? @）和任何非常规字符一律加双引号。
        /// </summary>
        public static string DefName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "\"\"";
            bool simple = true;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9') || c == '_' || c == '$' || c == '.';
                if (!ok) { simple = false; break; }
            }
            if (simple) return name;

            StringBuilder sb = new StringBuilder(name.Length + 2);
            sb.Append('"');
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
