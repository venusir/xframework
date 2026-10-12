using System;
using NUnit.Framework;
using XFramework.Architecture.Tests;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 「未启用不格式化」的可执行形式——本模块立项三条理由之一。
    /// <para><b>两道防线</b>：</para>
    /// <para>① <b>直接观测（确定性，不依赖任何尺子）</b>——「格式化」这件事有可观测的副作用：
    /// <c>string.Format</c> 必然调用参数的 <c>ToString()</c>，而分发必然经过 <c>ILogSink.Write</c>。
    /// 于是用「<c>ToString()</c> 自增计数的探针」与「只计数的 sink」把关档前后的行为差异变成事实断言。
    /// <b>每个用例都带正对照组</b>：把档位开回来，探针与 sink 必须都动——否则「探针根本没接上」
    /// 这类假绿会直接通过。</para>
    /// <para>② <b>放大粗尺子</b>（机制见 <see cref="AllocationRuler"/>）——探针与 sink 覆盖不了
    /// 「在早退之前就分配了」（例如先装箱参数、先建临时容器）：那种回归不触发 <c>ToString</c>、
    /// 也不进 sink，两道观测都会放行。尺子是唯一能看见它的东西。</para>
    /// <para><b>校准实测（2026-10-12）与由此得到的一条纪律</b>：注入「先格式化再判档」后，
    /// 探针立刻变 1（精确命中），而同一注入在尺子上只读到 417,792 B——因为格式化产生的是<b>瞬时垃圾</b>，
    /// 测量窗口内的 GC 会削掉相当一部分信号（同一批的持有式注入则读到 1.5–3.2 MB）。
    /// 结论：<b>这类回归以探针为准，尺子只是粗网</b>——阈值因此维持 400 KB（它要拦的是持有式 /
    /// 大体量分配那一档），不为了追瞬时垃圾把阈值压到噪声地板附近。</para>
    /// <para><b>为什么必须补这两道</b>：2026-10-12 之前这里只有 <c>GC.GetAllocatedBytesForCurrentThread()</c>
    /// ——它在本环境恒返回 0，四条断言全是恒真的（见 <see cref="AllocationRuler"/> 的类注释）。
    /// 谁哪天把模板路径改成先 <c>string.Format</c> 再判档位，功能用例全绿，而这里此前只有一条不会响的警报。</para>
    /// <para><b>量的是稳态路径</b>：JIT、静态构造、首次走完整条链路的分支预热都在预热段完成
    /// （<see cref="AllocationRuler"/> 统一做 1000 次）。</para>
    /// </summary>
    [TestFixture]
    public class LogAllocationTests
    {
        #region Constants

        /// <summary>粗尺子放大次数与阈值（阈值 = 实测坏形态信号 1/4，校准方法与数值见 Timer 侧同批用例）。</summary>
        private const int AmplifiedIterations = 100_000;

        private const long AllocationThresholdBytes = 400_000;

        #endregion

        #region Test Doubles

        /// <summary>
        /// <c>ToString()</c> 自增计数的探针：格式化是否发生，看它动不动。
        /// </summary>
        private sealed class ToStringProbe
        {
            internal int Count;

            public override string ToString()
            {
                Count++;
                return "probe";
            }
        }

        /// <summary>只计数的输出端：条目是否被分发出去，看它动不动。</summary>
        private sealed class CountingSink : ILogSink
        {
            internal int Count;

            public void Write(in LogEntry entry) => Count++;

            public void Flush()
            {
            }
        }

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp()
        {
            LogTestKit.SetUp();
        }

        [TearDown]
        public void TearDown()
        {
            LogTestKit.TearDown();
        }

        #endregion

        #region 未启用路径

        /// <summary>模板 + 2 参：关档后 10 万次既不许格式化、也不许分发、更不许分配。</summary>
        [Test]
        public void DisabledPath_TemplateOverload_NeitherFormatsNorAllocates()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);
            var probe = new ToStringProbe();
            var sink = new CountingSink();
            LogManager.AddSink(sink);

            AllocationRuler.AssertNoHeapGrowth("关档后写 10 万条（模板 + 2 参）",
                () => LogManager.Warning(LogCategories.Save, "x {0} y {1}", 1, 2f),
                AmplifiedIterations, AllocationThresholdBytes);

            // 关档下把探针真地传进去（两条模板入口各一次）——计数必须纹丝不动
            LogManager.Warning(LogCategories.Save, "x {0}", probe);
            LogManager.Warning(LogCategories.Save, "x {0} y {1}", probe, probe);

            Assert.AreEqual(0, probe.Count, "关档后 ToString 一次都不该被调用——「未启用不格式化」是本模块立项理由之一");
            Assert.AreEqual(0, sink.Count, "关档后不该有任何条目到达输出端");

            // 正对照：开回 Warning 档，两条观测都必须动
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Warning);
            LogManager.Warning(LogCategories.Save, "x {0} y {1}", probe, 1);

            Assert.AreEqual(1, probe.Count, "正对照：开档后 ToString 应被调用恰好一次");
            Assert.AreEqual(1, sink.Count, "正对照：开档后条目应到达输出端");
        }

        /// <summary>现成字符串重载：没有可格式化的动作，「关档即静默」就是它的全部承诺。</summary>
        [Test]
        public void DisabledPath_MessageOverload_NeitherDispatchesNorAllocates()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);
            var sink = new CountingSink();
            LogManager.AddSink(sink);

            AllocationRuler.AssertNoHeapGrowth("关档后写 10 万条（现成字符串）",
                () => LogManager.Warning(LogCategories.Save, "现成字符串"),
                AmplifiedIterations, AllocationThresholdBytes);

            Assert.AreEqual(0, sink.Count, "关档后不该有任何条目到达输出端");

            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Warning);
            LogManager.Warning(LogCategories.Save, "现成字符串");

            Assert.AreEqual(1, sink.Count, "正对照：开档后条目应到达输出端");
        }

        /// <summary>通用入口（<c>Log</c>）与分级入口走同一条路，同样既不许格式化、也不许分配。</summary>
        [Test]
        public void DisabledPath_GenericEntry_NeitherFormatsNorAllocates()
        {
            LogManager.MinimumLevel = LogLevel.Off;
            var probe = new ToStringProbe();
            var sink = new CountingSink();
            LogManager.AddSink(sink);

            AllocationRuler.AssertNoHeapGrowth("全局关档后写 10 万条（通用入口）",
                () => LogManager.Log(LogLevel.Error, LogCategories.Data, "{0}", 1),
                AmplifiedIterations, AllocationThresholdBytes);

            // 关档下把探针真地传进去一次——计数必须纹丝不动
            LogManager.Log(LogLevel.Error, LogCategories.Data, "{0}", probe);

            Assert.AreEqual(0, probe.Count, "全局关档后 ToString 一次都不该被调用");
            Assert.AreEqual(0, sink.Count, "全局关档后不该有任何条目到达输出端");

            // 正对照刻意用 Warning 而不是 Error：Error 级会触发测试框架的「意外错误日志」判失败，
            // 而正对照要证明的只是「探针与 sink 已接上」——那与级别无关。
            LogManager.MinimumLevel = LogLevel.Verbose;
            LogManager.Log(LogLevel.Warning, LogCategories.Data, "{0}", probe);

            Assert.AreEqual(1, probe.Count, "正对照：开档后 ToString 应被调用恰好一次");
            Assert.AreEqual(1, sink.Count, "正对照：开档后条目应到达输出端");
        }

        /// <summary><c>IsEnabled</c> 是「先探测再构造昂贵参数」的入口，它自己必须免费。</summary>
        [Test]
        public void IsEnabled_DoesNotAllocate()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            AllocationRuler.AssertNoHeapGrowth("探测 10 万次",
                () => LogManager.IsEnabled(LogLevel.Warning, LogCategories.Save),
                AmplifiedIterations, AllocationThresholdBytes);
        }

        #endregion

        #region 启用路径的边界

        /// <summary>
        /// 参数在调用方求值——与插值一致，这是<b>刻意的</b>。本用例把这条写进契约，防止有人
        /// 「优化」成延迟求值（那会改变副作用时机）。
        /// </summary>
        [Test]
        public void DisabledPath_StillEvaluatesArguments()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            int callCount = 0;
            LogManager.Warning(LogCategories.Save, "{0}", Count());
            Assert.AreEqual(1, callCount, "关档只免掉格式化，不该免掉参数求值");

            int Count()
            {
                callCount++;
                return callCount;
            }
        }

        #endregion
    }
}
