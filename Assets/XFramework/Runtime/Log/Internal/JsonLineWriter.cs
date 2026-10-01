using System;
using System.Globalization;
using System.Text;

namespace XFramework.XLog.Internal
{
    /// <summary>
    /// JSONL 的底层写入原语：转义、时间戳、线程本地的行缓冲。
    /// <para><b>行内绝不出现裸换行</b>：换行、回车、制表符与全部 <c>&lt; 0x20</c> 的控制符一律转义——
    /// 这是「一条日志一行」的前提，也是 AI 侧能直接 <c>grep</c> / <c>jq</c> 的前提。</para>
    /// <para><b>非 ASCII（中文）原样保留</b>：文件是 UTF-8，转义成 <c>\uXXXX</c> 只会让人读不懂、AI 读更慢。</para>
    /// <para><b>为什么用线程本地的缓冲</b>：日志可能来自任意线程，不能共用一个 StringBuilder
    /// （那需要锁，而锁会把写入路径串行化）；每线程一个，用完不还、下次清空复用。</para>
    /// </summary>
    internal static class JsonLineWriter
    {
        #region Private Fields

        private static readonly char[] HexDigits = "0123456789abcdef".ToCharArray();

        [ThreadStatic]
        private static StringBuilder _builder;

        #endregion

        #region Internal API

        /// <summary>取本线程的行缓冲（已清空）。同一线程下一次 <see cref="Rent"/> 前必须已 <c>ToString()</c>。</summary>
        internal static StringBuilder Rent()
        {
            StringBuilder sb = _builder;
            if (sb == null)
            {
                sb = new StringBuilder(512);
                _builder = sb;
            }
            else
            {
                sb.Clear();
            }

            return sb;
        }

        /// <summary>写 ISO8601 UTC 毫秒时间戳（如 <c>2026-10-01T11:30:12.345Z</c>），<b>不含</b>两侧引号。</summary>
        /// <param name="sb">目标缓冲。</param>
        /// <param name="utc">UTC 时刻。</param>
        internal static void AppendIsoTimestamp(StringBuilder sb, DateTime utc)
        {
            sb.Append(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        }

        /// <summary>写一个 JSON 字符串字面量（<b>含</b>两侧引号）。null 写成空串。</summary>
        /// <param name="sb">目标缓冲。</param>
        /// <param name="value">原值。</param>
        internal static void AppendEscaped(StringBuilder sb, string value)
        {
            sb.Append('"');

            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        case '\b':
                            sb.Append("\\b");
                            break;
                        case '\f':
                            sb.Append("\\f");
                            break;
                        default:
                            if (c < 0x20)
                            {
                                sb.Append("\\u");
                                sb.Append(HexDigits[(c >> 12) & 0xF]);
                                sb.Append(HexDigits[(c >> 8) & 0xF]);
                                sb.Append(HexDigits[(c >> 4) & 0xF]);
                                sb.Append(HexDigits[c & 0xF]);
                            }
                            else
                            {
                                sb.Append(c);
                            }

                            break;
                    }
                }
            }

            sb.Append('"');
        }

        #endregion
    }
}
