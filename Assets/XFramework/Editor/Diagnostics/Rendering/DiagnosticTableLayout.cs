using System;

namespace XFramework.Editor.Diagnostics
{
    /// <summary>
    /// 表格列宽分配（纯函数、零 Unity 依赖）：把各列的自然宽度压进可用宽度。
    /// <para><b>策略</b>：放得下就原样返回；放不下就按「可收缩余地」（自然宽度高出下限的部分）
    /// 等比收缩，每列不低于 <paramref name="minWidth"/>；余地用尽仍放不下时照实返回——
    /// 由渲染方裁剪或滚动，不再继续挤压（挤成一列宽的字更没法读）。</para>
    /// <para>窄于下限的列<b>不会被撑大</b>：本函数只做收缩，不改变「内容决定宽度」这个前提。</para>
    /// <para>纯函数是刻意的：IMGUI 调用本身不可测，这里是与它分离的那块数学。</para>
    /// </summary>
    internal static class DiagnosticTableLayout
    {
        /// <summary>
        /// 分配列宽。
        /// </summary>
        /// <param name="natural">各列的自然宽度（由内容测量得出）。</param>
        /// <param name="available">可用于列本身的宽度（调用方已扣掉列间距与边距）。</param>
        /// <param name="minWidth">单列下限；<paramref name="available"/> 非正或放得下时本参数不起作用。</param>
        /// <returns>各列最终宽度；长度与 <paramref name="natural"/> 一致（输入为 null/空时返回空数组）。</returns>
        internal static float[] Allocate(float[] natural, float available, float minWidth)
        {
            if (natural == null || natural.Length == 0)
                return Array.Empty<float>();

            int count = natural.Length;
            var widths = new float[count];

            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                widths[i] = natural[i];
                total += natural[i];
            }

            if (available <= 0f || total <= available)
                return widths;

            float shrinkable = 0f;
            for (int i = 0; i < count; i++)
            {
                float room = natural[i] - minWidth;
                if (room > 0f)
                    shrinkable += room;
            }

            if (shrinkable <= 0f)
                return widths;

            float overflow = total - available;
            float factor = overflow >= shrinkable ? 1f : overflow / shrinkable;

            for (int i = 0; i < count; i++)
            {
                float room = natural[i] - minWidth;
                if (room > 0f)
                    widths[i] = natural[i] - room * factor;
            }

            return widths;
        }
    }
}
