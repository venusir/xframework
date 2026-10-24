using System;
using System.Text;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// 有上限的进程输出缓冲：<c>--verbose</c> 的大工程不把内存撑大。
    /// 超过上限后追加截断标记，后续写入直接忽略。
    /// </summary>
    internal sealed class LubanProcessOutputBuffer
    {
        internal const int DefaultMaxChars = 1_000_000;
        private const string TruncationMarker = "\n... [output truncated]";

        private readonly object _gate = new();
        private readonly StringBuilder _builder = new();
        private readonly int _maxChars;
        private bool _truncated;

        internal LubanProcessOutputBuffer(int maxChars = DefaultMaxChars)
        {
            _maxChars = Math.Max(1, maxChars);
        }

        internal bool IsTruncated
        {
            get
            {
                lock (_gate)
                {
                    return _truncated;
                }
            }
        }

        internal void AppendLine(string value)
        {
            lock (_gate)
            {
                if (_truncated)
                {
                    return;
                }

                value ??= string.Empty;
                int required = value.Length + Environment.NewLine.Length;
                if (_builder.Length + required > _maxChars)
                {
                    int remaining = _maxChars - _builder.Length;
                    if (remaining > 0)
                    {
                        _builder.Append(value, 0, Math.Min(remaining, value.Length));
                    }

                    _builder.Append(TruncationMarker);
                    _truncated = true;
                    return;
                }

                _builder.AppendLine(value);
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _builder.ToString();
            }
        }
    }
}
