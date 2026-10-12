using System;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 共享的「粗尺子」：把一段动作放大 N 次，量托管堆增量。
    /// <para><b>为什么要有这个共享实现</b>：本环境有两把分配尺子（<c>GC.GetAllocatedBytesForCurrentThread()</c>
    /// 与 <c>Profiler.GetTotalAllocatedMemoryLong()</c>）<b>恒返回 0</b>——2026-10-11 实测：分配 1 MB 数组后
    /// 增量仍为 0。唯一可用的代理是 <c>Profiler.GetMonoUsedSizeLong()</c>，其粒度约 4–8 KB，单次装箱
    /// （约 32 B）根本看不见，故<b>必须放大迭代</b>。放大的写法此前只在
    /// <c>ReactiveEnumerationAllocationTests</c> 里私有实现过一份；各模块若各自复制，就是
    /// 「坏形态靠模仿扩散」的镜像——<b>好形态也会靠复制扩散</b>。本类型是唯一实现，且是<b>全仓唯一允许
    /// 读那把可用尺子的地方</b>（由 <c>AllocationRulerTests</c> 的源码守卫锁定）。</para>
    /// <para><b>为什么放在 Architecture</b>：<c>Tests/Runtime</c> 下只有「模块子目录 + Architecture」两种桶，
    /// 而 Architecture 已是横切、非模块物的既有落点（<c>AutoInitTests</c> 族清单、<c>NamingConventionTests</c>
    /// 源码扫描）。等哪天出现<b>第二个</b>横切测试工具再抽 <c>Common/</c> 并把两者一起搬过去
    /// （与 <c>ModuleBoundaryTests.ReadCodeOnly</c>「两处副本、第三处就该抽」同一纪律）。</para>
    /// <para><b>契约（防误用）</b>：</para>
    /// <para>① <paramref name="action"/> 必须是<b>自清理、可无限重复</b>的稳态动作（不得泄漏定时器 / 订阅 /
    /// 池化实例）；</para>
    /// <para>② 委托与闭包必须在<b>测量窗口之外</b>构造——调用点直接写 lambda 字面量即可（它在进入本类型前
    /// 就分配完了）；<b>严禁</b>在循环体内 new 委托或捕获循环变量；</para>
    /// <para>③ 阈值必须<b>来自实测信号</b>：临时注入「每迭代一个对象」（约 32 B）量到增量 S，阈值取 S/4，
    /// 并保证放大后的 S ≥ 1 MB（4–8 KB 粒度的 32 倍以上）；<b>不得随手写</b>，也不得为了让门禁变绿抬阈值；</para>
    /// <para>④ <b>不要在本类型之外自建测量循环</b>——两把恒零尺子已有源码守卫，本类型是唯一合法入口。</para>
    /// </summary>
    internal static class AllocationRuler
    {
        #region Constants

        /// <summary>
        /// 默认预热次数：JIT、静态构造、首次表分配都发生在这一段，量的是稳态路径
        /// （与 <c>TimerAllocationTests</c> 的「每个用例先预热再量」同一口径）。
        /// </summary>
        internal const int DefaultWarmupIterations = 1000;

        /// <summary>
        /// 尺子自检用的噪声地板断言值（字节）。低于它才说明「什么都不分配时尺子是安静的」；
        /// 一旦越线，下游全部粗尺子断言失去证据力（处置见 <c>Documentation/Workflow.md</c> §六）。
        /// </summary>
        internal const long QuietFloorBytes = 128 * 1024;

        #endregion

        #region Measurement

        /// <summary>
        /// 预热 → 双 Collect → 放大 <paramref name="iterations"/> 次 → 返回托管堆增量（字节，可为负——那是测量噪声）。
        /// <para>双 <c>GC.Collect()</c> 夹一次 <c>WaitForPendingFinalizers()</c>：把终结器队列里的对象也挤出去，
        /// 否则它们会在测量窗口内被回收，把增量压小甚至压成负值。</para>
        /// </summary>
        internal static long MeasureHeapGrowth(string label, Action action, int iterations,
            int warmupIterations = DefaultWarmupIterations)
        {
            for (int i = 0; i < warmupIterations; i++)
                action();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < iterations; i++)
                action();
            long growth = Profiler.GetMonoUsedSizeLong() - before;

            // 每次运行都把量到的值写进测试结果——便于长期观察漂移（同 TimerTierTableTests 的做法）。
            TestContext.WriteLine($"[AllocationRuler] {label}：{iterations} 次迭代，堆增量 {growth} 字节");
            return growth;
        }

        #endregion

        #region Assertions

        /// <summary>
        /// 断言放大 <paramref name="iterations"/> 次后的堆增量 &lt; <paramref name="maxGrowthBytes"/>。
        /// </summary>
        internal static void AssertNoHeapGrowth(string label, Action action, int iterations, long maxGrowthBytes)
        {
            long growth = MeasureHeapGrowth(label, action, iterations);

            Assert.Less(growth, maxGrowthBytes,
                $"{label}：{iterations} 次迭代的堆增量应 < {maxGrowthBytes} B，实测 {growth} B。\n" +
                "先怀疑产品、再怀疑干扰：把迭代数翻倍，看增量是否线性增长——线性即真分配，" +
                "按「增量 ÷ 迭代数」估每次的字节数（24–32 B 是对象或装箱、40–80 B 是枚举器、KB 级是字符串或容器）。" +
                "若线性增长来自测试侧闭包（在 action 里 new 委托、捕获循环变量），改测试即可。");
        }

        /// <summary>
        /// 反向断言：堆增量必须 ≥ <paramref name="minGrowthBytes"/>——<b>尺子自检用</b>，它必须看得见已知量。
        /// </summary>
        internal static void AssertHeapGrowthAtLeast(string label, Action action, int iterations,
            long minGrowthBytes, int warmupIterations = DefaultWarmupIterations)
        {
            long growth = MeasureHeapGrowth(label, action, iterations, warmupIterations);

            Assert.GreaterOrEqual(growth, minGrowthBytes,
                $"{label}：尺子读到的堆增量应 ≥ {minGrowthBytes} B，实测 {growth} B——本次分配是已知量、" +
                "对象全程可达。读到 0 或远小于分配量说明这把尺子也失效了，此时全部粗尺子断言失去证据力" +
                "（处置见 Documentation/Workflow.md §六）。");
        }

        #endregion
    }
}
