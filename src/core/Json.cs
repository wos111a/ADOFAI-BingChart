// 极简 JSON 解析器 —— 纯 C#，零依赖。
// 只为读 .adofai 谱面而写，容忍尾随逗号、注释、UTF-8 BOM、\uXXXX 转义。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BingChart
{
    public class JVal
    {
        public enum Kind { Null, Bool, Num, Str, Arr, Obj }

        public Kind K;
        public bool B;
        public double N;
        public string S;
        public List<JVal> A;
        public Dictionary<string, JVal> O;

        public bool Has(string key)
        {
            return K == Kind.Obj && O != null && O.ContainsKey(key);
        }

        public JVal Get(string key)
        {
            JVal v;
            if (K == Kind.Obj && O != null && O.TryGetValue(key, out v)) return v;
            return null;
        }

        public string StrOr(string def)
        {
            if (K == Kind.Str) return S;
            if (K == Kind.Num) return N.ToString(CultureInfo.InvariantCulture);
            return def;
        }

        public double NumOr(double def)
        {
            if (K == Kind.Num) return N;
            if (K == Kind.Str)
            {
                double d;
                if (double.TryParse(S, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            }
            if (K == Kind.Bool) return B ? 1 : 0;
            return def;
        }

        public int IntOr(int def) { return (int)Math.Round(NumOr(def)); }

        public List<JVal> ArrOrEmpty()
        {
            return K == Kind.Arr && A != null ? A : new List<JVal>();
        }
    }

    public static class Json
    {
        public static JVal Parse(string text)
        {
            if (text == null) return null;
            int i = 0;
            if (text.Length >= 3 && text[0] == '\uFEFF') i = 1;
            SkipWs(text, ref i);
            JVal v = ParseValue(text, ref i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                // 容错：跳过 // 与 /* */ 注释
                if (c == '/' && i + 1 < s.Length)
                {
                    if (s[i + 1] == '/')
                    {
                        while (i < s.Length && s[i] != '\n') i++;
                        continue;
                    }
                    if (s[i + 1] == '*')
                    {
                        i += 2;
                        while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                        i = Math.Min(s.Length, i + 2);
                        continue;
                    }
                }
                break;
            }
        }

        private static JVal ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObj(s, ref i);
                case '[': return ParseArr(s, ref i);
                case '"': return new JVal { K = JVal.Kind.Str, S = ParseString(s, ref i) };
                case 't':
                    if (Match(s, i, "true")) { i += 4; return new JVal { K = JVal.Kind.Bool, B = true }; }
                    return null;
                case 'f':
                    if (Match(s, i, "false")) { i += 5; return new JVal { K = JVal.Kind.Bool, B = false }; }
                    return null;
                case 'n':
                    if (Match(s, i, "null")) { i += 4; return new JVal { K = JVal.Kind.Null }; }
                    return null;
                default: return ParseNum(s, ref i);
            }
        }

        private static bool Match(string s, int i, string w)
        {
            if (i + w.Length > s.Length) return false;
            for (int k = 0; k < w.Length; k++) if (s[i + k] != w[k]) return false;
            return true;
        }

        private static JVal ParseObj(string s, ref int i)
        {
            var o = new Dictionary<string, JVal>(StringComparer.Ordinal);
            var v = new JVal { K = JVal.Kind.Obj, O = o };
            i++; // {
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }   // 容错尾随逗号
                if (s[i] != '"') { i++; continue; }  // 跳过一个无法识别的 token
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                JVal val = ParseValue(s, ref i);
                o[key] = val;
            }
            return v;
        }

        private static JVal ParseArr(string s, ref int i)
        {
            var a = new List<JVal>();
            var v = new JVal { K = JVal.Kind.Arr, A = a };
            i++; // [
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ']') { i++; break; }
                if (s[i] == ',') { i++; continue; }   // 容错尾随逗号
                JVal e = ParseValue(s, ref i);
                if (e == null) { i++; continue; }
                a.Add(e);
            }
            return v;
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // 开头的 "
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                             CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            else i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        private static JVal ParseNum(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' ||
                                    s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+')) i++;
            if (i == start) { i++; return null; }
            double d;
            if (double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out d))
                return new JVal { K = JVal.Kind.Num, N = d };
            return null;
        }
    }
}
