using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

        #region 坏尺子源码守卫

        /// <summary>
        /// 两把在本环境**恒返回 0** 的分配尺子。任何地方都不许用——测试里用它就是恒真断言，
        /// 生产里用它就是假遥测。
        /// <para><b><c>[B]</c> / <c>[M]</c> 是字符类自规避</b>：本守卫会扫到自己，模式字面量里直接写全
        /// 也会在自己的定义处命中（同 <c>NamingConventionTests</c> 的 <c>[U]i</c>）。
        /// **刻意不给本文件开豁免**——豁免是把守卫挖出一个洞。</para>
        /// </summary>
        private static readonly Regex[] BrokenRulers =
        {
            new Regex(@"GC\.GetAllocated[B]ytesForCurrentThread"),
            new Regex(@"Profiler\.GetTotalAllocatedMemory[M]Long"),
        };

        /// <summary>唯一可用的分配尺子——它是共享工具的内部实现，只许出现在那里。</summary>
        private static readonly Regex SharedRulerReader = new Regex(@"Profiler\.GetMonoUsed[S]izeLong");

        /// <summary>唯一允许读那把可用尺子的文件（白名单即「唯一实现」这条约束本身）。</summary>
        private const string SharedRulerFile = "Tests/Runtime/Architecture/AllocationRuler.cs";

        /// <summary>
        /// 两把恒零尺子在包内必须零命中。**在全部 11 处站点改造完成之后提交**——在那之前它自己是红的。
        /// </summary>
        [Test]
        public void BrokenRulers_AreNotUsedAnywhereInThePackage()
        {
            var violations = new List<string>();

            foreach (var pattern in BrokenRulers)
                violations.AddRange(Scan(pattern, null));

            Assert.IsEmpty(violations,
                "以下位置用了恒返回 0 的分配尺子——它们读到的永远是 0：写在测试里就是恒真断言" +
                "（本仓有 11 处这样躺了数月才被发现），写在生产里就是假遥测。替代处置：\n" +
                "  · 要证「不分配」→ 优先结构断言（见 Timer / Message / UI 的 StructuralEvidence 用例），" +
                "兜底用共享工具 AllocationRuler；\n" +
                "  · 要观测「没发生」→ 用直接观测（探针 ToString 计数 / 计数 sink，见 Log 侧用例）；\n" +
                "  · 真要与这两把尺子对话（例如再做一次本环境的实测探针）→ 做成不入仓的一次性探针。\n  " +
                string.Join("\n  ", violations));
        }

        /// <summary>
        /// 可用的那把尺子只许有<b>一个</b>读者：共享工具本身。各 fixture 各写一份测量循环，
        /// 就是「好形态靠复制扩散」——预热次数、双 Collect、放大倍数、阈值口径会各自漂移，
        /// 而它们本该只有一份。
        /// </summary>
        [Test]
        public void SharedRuler_IsReadOnlyByTheOneTool()
        {
            var violations = Scan(SharedRulerReader, relative => relative == SharedRulerFile);

            Assert.IsEmpty(violations,
                $"这把可用的分配尺子只允许出现在 {SharedRulerFile} 一个文件里——那里是唯一的测量实现，" +
                "各 fixture 请调用 AllocationRuler 的断言方法（迭代数与阈值由调用方给，机制只有一份）。\n  " +
                string.Join("\n  ", violations));
        }

        /// <summary>
        /// 扫描包内全部 <c>.cs</c>（注释行已剥），返回命中位置；<paramref name="isAllowed"/> 为 <c>null</c>
        /// 表示没有任何豁免。
        /// </summary>
        private static List<string> Scan(Regex pattern, Func<string, bool> isAllowed)
        {
            var hits = new List<string>();

            foreach (var file in SourceScanner.EnumerateSources())
            {
                var relative = SourceScanner.RelativePath(file);
                if (isAllowed != null && isAllowed(relative))
                    continue;

                var lines = SourceScanner.ReadCodeOnly(file).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (pattern.IsMatch(lines[i]))
                        hits.Add($"{relative}:{i + 1}  {lines[i].Trim()}");
                }
            }

            return hits;
        }

        #endregion
    }
}
