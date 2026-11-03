using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeStubGen
{
    /// <summary>够用就好的 JSON 工具：写（任意嵌套）+ 读（扁平对象，仅用于设置文件）。</summary>
    public static class Json
    {
        public static string Quote(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Bool(bool b) { return b ? "true" : "false"; }

        public static string Number(long n) { return n.ToString(CultureInfo.InvariantCulture); }

        /// <summary>解析扁平 JSON 对象，值只支持字符串/布尔/数字/null。</summary>
        public static Dictionary<string, object> ParseFlatObject(string text)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            SkipWs(text, ref i);
            Expect(text, ref i, '{');
            SkipWs(text, ref i);
            if (i < text.Length && text[i] == '}') return result;

            while (i < text.Length)
            {
                SkipWs(text, ref i);
                string key = ReadString(text, ref i);
                SkipWs(text, ref i);
                Expect(text, ref i, ':');
                SkipWs(text, ref i);
                object value = ReadValue(text, ref i);
                result[key] = value;
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == ',') { i++; continue; }
                if (i < text.Length && text[i] == '}') break;
                throw new FormatException("JSON 格式错误：位置 " + i);
            }
            return result;
        }

        private static object ReadValue(string t, ref int i)
        {
            if (i >= t.Length) throw new FormatException("JSON 提前结束");
            char c = t[i];
            if (c == '"') return ReadString(t, ref i);
            if (c == 't' || c == 'f')
            {
                string word = c == 't' ? "true" : "false";
                if (string.CompareOrdinal(t, i, word, 0, word.Length) != 0) throw new FormatException("非法字面量");
                i += word.Length;
                return c == 't';
            }
            if (c == 'n')
            {
                if (string.CompareOrdinal(t, i, "null", 0, 4) != 0) throw new FormatException("非法字面量");
                i += 4;
                return null;
            }
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                int start = i;
                while (i < t.Length && ("-+.eE0123456789".IndexOf(t[i]) >= 0)) i++;
                double d;
                if (!double.TryParse(t.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new FormatException("非法数字");
                return d;
            }
            throw new FormatException("无法识别的值：位置 " + i);
        }

        private static string ReadString(string t, ref int i)
        {
            Expect(t, ref i, '"');
            StringBuilder sb = new StringBuilder();
            while (i < t.Length)
            {
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) break;
                char e = t[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= t.Length)
                        {
                            int code = int.Parse(t.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("字符串没有结束");
        }

        private static void SkipWs(string t, ref int i)
        {
            while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\r' || t[i] == '\n')) i++;
        }

        private static void Expect(string t, ref int i, char c)
        {
            if (i >= t.Length || t[i] != c) throw new FormatException("期望 '" + c + "'，位置 " + i);
            i++;
        }

        public static string GetString(Dictionary<string, object> d, string key, string fallback)
        {
            object o;
            if (d != null && d.TryGetValue(key, out o) && o is string) return (string)o;
            return fallback;
        }

        public static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
        {
            object o;
            if (d != null && d.TryGetValue(key, out o) && o is bool) return (bool)o;
            return fallback;
        }
    }
}
