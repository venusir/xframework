using NUnit.Framework;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 粗尺子的<b>自检</b>：证实 <see cref="AllocationRuler"/> 读的那把尺子
    /// （<c>Profiler.GetMonoUsedSizeLong()</c>，机制说明见 <see cref="AllocationRuler"/> 的类注释）
    /// 在本环境真的看得见分配。
    /// <para><b>为什么必须有它</b>：不可用的尺子不会报错，它只会<b>沉默</b>——本仓有两把恒返回 0 的分配尺子
    /// 让 11 处 <c>Assert.AreEqual(0L, allocated, …)</c> 恒真了数月而无人察觉（2026-10-11 实测归档）。
    /// 自检把「尺子可用」变成每次全量门禁都会跑的前提条件：尺子坏掉时红在这里，而不是让下游各模块的
    /// 粗尺子断言集体退化成恒真。</para>
    /// <para><b>为什么不断言那两把坏尺子恒返回 0</b>：那是把<b>环境事实</b>固化成断言——测试壳或编辑器版本
    /// 一变，它就在别人的机器上变红，而红的信息量为零。正确做法是：环境事实写进注释（上面就是），
    /// 「不许用」交给同目录的源码守卫（守卫是<b>约束</b>，可长期成立；断言是<b>描述</b>，会过期）。</para>
    /// </summary>
    [TestFixture]
    public class AllocationRulerTests
    {
        #region Fixture

        /// <summary>已知量大分配的自检载荷——静态字段持有，保证测量窗口内全程可达。</summary>
        private static byte[] _ballast;

        /// <summary>
        /// 小对象自检的载荷槽：容纳预热 1000 次 + 测量 10 万次共 10.1 万个「每迭代一个对象」，
        /// <b>全部可达</b>——既不被中途回收，也不会因超出分配预算触发窗口内 GC 而压低读数。
        /// </summary>
        private static readonly object[] SmallObjectBallast = new object[131_072];

        private static int _ballastIndex;

        /// <summary>「什么都不分配」自检的可观测副作用载体，防止动作被优化掉。</summary>
        private static long _quietSink;

        #endregion

        #region 自检

        /// <summary>
        /// ① 看得见已知的大分配：窗口内分配 1 MB，读数必须跟上。
        /// </summary>
        [Test]
        public void Ruler_SeesAKnownLargeAllocation()
        {
            long growth = AllocationRuler.MeasureHeapGrowth(
                "已知 1 MB 分配",
                static () => _ballast = new byte[1024 * 1024],
                iterations: 1,
                warmupIterations: 1);

            Assert.GreaterOrEqual(growth, 512 * 1024,
                $"窗口内分配了 1 MB，尺子只读到 {growth} B——它看不见这一档的分配");
            Assert.Less(growth, 4 * 1024 * 1024,
                $"窗口内只分配了 1 MB，尺子读到 {growth} B——它在乱报，读数不可信");
        }

        /// <summary>
        /// ② 看得见「每迭代一个小对象」这一档——<b>这正是下游全部粗尺子阈值所依赖的假设</b>
        /// （最小坏形态统一建模为每迭代约 32 B）。
        /// </summary>
        [Test]
        public void Ruler_SeesManySmallObjects()
        {
            _ballastIndex = 0;

            long growth = AllocationRuler.MeasureHeapGrowth(
                "每迭代一个小对象",
                static () => SmallObjectBallast[_ballastIndex++] = new object(),
                iterations: 100_000);

            Assert.GreaterOrEqual(growth, 1024 * 1024,
                $"窗口内分配了 10 万个可达的小对象（≥ 1.6 MB），尺子只读到 {growth} B——" +
                "它看不见这一档，下游按「32 B/迭代」取阈值的断言全部失去证据力");
        }

        /// <summary>
        /// ③ 什么都不分配时保持安静：噪声地板必须远低于下游最小阈值（当前最小 320 KB），否则余量不足。
        /// </summary>
        [Test]
        public void Ruler_StaysQuiet_WhenNothingIsAllocated()
        {
            long growth = AllocationRuler.MeasureHeapGrowth(
                "什么都不分配",
                static () => _quietSink++,
                iterations: 100_000);

            Assert.Less(growth, AllocationRuler.QuietFloorBytes,
                $"零分配的 10 万次迭代读到了 {growth} B 的增长，噪声地板高于 " +
                $"{AllocationRuler.QuietFloorBytes} B——下游粗尺子的最小阈值（320 KB）只剩不到 3 倍余量，" +
                "它们全部失去证据力");
        }

        #endregion
    }
}
