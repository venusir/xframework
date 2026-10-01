using System;
using System.Globalization;

namespace XFramework.XLog.Internal
{
    /// <summary>
    /// 模板格式化。
    /// <para><b>刻意走 <c>string.Format(CurrentCulture, …)</c> 而不是自研 formatter</b>：<c>{{</c> 转义、
    /// <c>null</c> 参数、<c>IFormattable</c> 格式说明符、culture 语义<b>与字符串插值同源</b>——这是「迁移后
    /// 控制台文本逐字不变」的免费正确性证明。自研 formatter 是性能优化，四项语义任何一处差一点就是
    /// 30+ 处 <c>LogAssert</c> 变红，已评估未采纳。</para>
    /// <para><b>永不抛</b>：模板里出现裸花括号（合法 JSON 之类的消息）会触发 <see cref="FormatException"/>，
    /// 此时退回原文——日志宁可少格式化一次，也不能因为「打印失败」而抛异常。</para>
    /// </summary>
    internal static class LogFormatter
    {
        /// <summary>取现成字符串（null 视为空串）。</summary>
        internal static string Format(string message)
        {
            return message ?? string.Empty;
        }

        /// <summary>模板 + 1 参。走 <c>string.Format(provider, format, arg0)</c> 重载，不分配 <c>object[]</c>。</summary>
        internal static string Format<T1>(string template, T1 arg1)
        {
            if (template == null)
                return string.Empty;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, arg1);
            }
            catch (Exception)
            {
                return template; // 裸花括号 / 参数的 ToString 抛异常：退回原文，绝不向上抛
            }
        }

        /// <summary>模板 + 2 参。走 <c>string.Format(provider, format, arg0, arg1)</c> 重载，不分配 <c>object[]</c>。</summary>
        internal static string Format<T1, T2>(string template, T1 arg1, T2 arg2)
        {
            if (template == null)
                return string.Empty;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, arg1, arg2);
            }
            catch (Exception)
            {
                return template;
            }
        }

        /// <summary>模板 + 3 参。这是「不分配 <c>object[]</c>」的上限——4 参起会掉进 <c>params</c> 重载。</summary>
        internal static string Format<T1, T2, T3>(string template, T1 arg1, T2 arg2, T3 arg3)
        {
            if (template == null)
                return string.Empty;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, arg1, arg2, arg3);
            }
            catch (Exception)
            {
                return template;
            }
        }
    }
}
