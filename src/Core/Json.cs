using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CLWallpaper
{
    /// <summary>Tiny dependency-free JSON reader/writer (objects, arrays, strings, numbers, bools, null).</summary>
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (i < s.Length)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ':') i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; break; }
                    break;
                }
                return d;
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (i < s.Length)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; break; }
                    break;
                }
                return l;
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double d2;
            double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d2);
            if (i == st) i++;
            return d2;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            if (i < s.Length && s[i] == '"') i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Write(object o)
        {
            var sb = new StringBuilder();
            W(sb, o, 0);
            return sb.ToString();
        }

        static void W(StringBuilder sb, object o, int ind)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { Q(sb, (string)o); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is double || o is float || o is int || o is long || o is decimal)
            {
                sb.Append(Convert.ToDouble(o).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = o as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int n = 0;
                foreach (DictionaryEntry kv in dict)
                {
                    sb.Append(' ', ind + 2); Q(sb, kv.Key.ToString()); sb.Append(": ");
                    W(sb, kv.Value, ind + 2);
                    if (++n < dict.Count) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(' ', ind).Append('}');
                return;
            }
            var list = o as IList;
            if (list != null)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(", "); W(sb, list[i], ind + 2); }
                sb.Append(']');
                return;
            }
            Q(sb, o.ToString());
        }

        static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // --- typed getters ---
        public static double D(IDictionary<string, object> d, string k, double def)
        {
            object v; if (d != null && d.TryGetValue(k, out v) && v is double) return (double)v; return def;
        }
        public static int I(IDictionary<string, object> d, string k, int def) { return (int)Math.Round(D(d, k, def)); }
        public static bool B(IDictionary<string, object> d, string k, bool def)
        {
            object v; if (d != null && d.TryGetValue(k, out v) && v is bool) return (bool)v; return def;
        }
        public static string S(IDictionary<string, object> d, string k, string def)
        {
            object v; if (d != null && d.TryGetValue(k, out v) && v is string) return (string)v; return def;
        }
        public static IDictionary<string, object> O(IDictionary<string, object> d, string k)
        {
            object v; if (d != null && d.TryGetValue(k, out v)) return v as IDictionary<string, object>; return null;
        }
    }
}
