using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// <b>跨模块集成哨兵</b>：Timer 内置的档位周期表必须与 Update 的<b>真实派发节奏</b>一致。
    /// <para><b>为什么需要它</b>：<c>UpdateScheduler.TickPeriod</c> 是 <c>internal const</c>，而模块边界
    /// 不许引用别的模块的 Internal 命名空间——Timer 只能自己抄一份「60Hz 基准」。抄来的常量没有编译器
    /// 兜底：Update 哪天把基准改成 50Hz、或改了补格算法，Timer 的选档会静默失准（表现为定时器触发变晚），
    /// 而任何一方的单元测试都还是绿的。</para>
    /// <para><b>量的是真实节奏而不是常量</b>：用例注册一个探针节点到某一档，用固定步长手动驱动，
    /// 量两次相邻派发之间的<b>实际间隔</b>，再与 <c>TierPeriodOf</c> 比对。故它锁住的不是「常量写没写对」，
    /// 而是「Timer 以为的周期」与「Update 实际给的周期」是否还是同一件事。</para>
    /// </summary>
    [TestFixture]
    public class TimerTierTableTests
    {
        #region Test Doubles

        /// <summary>记录每次被派发时刻的探针；档位恒定，否则量到的是调度器的换档而不是周期。</summary>
        private sealed class DispatchProbe : IUpdateable
        {
            private readonly UpdateTier _tier;

            internal readonly List<double> Times = new List<double>();

            internal DispatchProbe(UpdateTier tier)
            {
                _tier = tier;
            }

            public void OnEnable()
            {
            }

            public void OnDisable()
            {
            }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                Times.Add(time);
                return _tier;
            }
        }

        #endregion

        #region Fixture

        private float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            TimerTestKit.SetUp();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            TimerTestKit.TearDown();
        }

        #endregion

        [Test]
        public void TierPeriods_MatchUpdateRealDispatchRhythm()
        {
            // 一帧恰好一格：驱动步长按 1/60 给定，于是「2^k 格」在驱动时间上就是 2^k / 60 秒。
            // 用略大的步长（1.001/60）也行，但那会让量到的周期带上 0.1% 的系统性偏差
            var driver = new TimerDriver();

            for (int k = 1; k <= (int)UpdateTier.Max; k++)
            {
                var tier = (UpdateTier)k;
                var probe = new DispatchProbe(tier);
                UpdateManager.Register(probe, order: 0, initialTier: tier);

                // 2^k 格一轮，走 600 帧足够量出十余次派发；Tier7（128 格）也能拿到 4 次以上
                driver.Advance(600);

                UpdateManager.Unregister(probe);

                Assert.GreaterOrEqual(probe.Times.Count, 4,
                    $"{tier} 在 600 帧里只被派发了 {probe.Times.Count} 次，量不出周期");

                double measured = (probe.Times[probe.Times.Count - 1] - probe.Times[0])
                                  / (probe.Times.Count - 1);
                double expected = TimerCore.TierPeriodOf(tier);

                // 实测值写进测试结果文件：维护向文档的「实测记录」直接抄它，不必回头再跑一遍
                TestContext.WriteLine(
                    $"{tier}: 实测 {measured * 1000:0.##} ms / 标称 {expected * 1000:0.##} ms" +
                    $"（{probe.Times.Count} 次派发）");

                // 容差取 2%：本用例要抓的是「基准换了」（60→50 差 17%）或「补格算法改了」这类
                // 结构性失配，不是浮点噪声
                Assert.AreEqual(expected, measured, expected * 0.02,
                    $"{tier}：Timer 的周期表算出 {expected:0.####} 秒，Update 实际是 {measured:0.####} 秒" +
                    "——两边的节拍基准已经对不上了（TimerCore.TicksPerSecond 需要同步）");
            }
        }

        /// <summary>
        /// 探针自己也要靠谱：档位越粗，驱动时间的间隔就越长，且恰好是 2^k 倍——这条把「第一个用例量的确实是
        /// 档位周期」钉住，免得哪天探针被改坏、第一个用例变成在量一个恒定值。
        /// </summary>
        [Test]
        public void MeasuredPeriods_DoublePerTier()
        {
            var driver = new TimerDriver();
            var measured = new double[(int)UpdateTier.Max + 1];

            for (int k = 1; k <= (int)UpdateTier.Max; k++)
            {
                var tier = (UpdateTier)k;
                var probe = new DispatchProbe(tier);
                UpdateManager.Register(probe, order: 0, initialTier: tier);

                // 驱动时间在用例内连续推进，间隔一律按差值算——绝对时刻是多少无关紧要
                driver.Advance(600);
                UpdateManager.Unregister(probe);

                measured[k] = (probe.Times[probe.Times.Count - 1] - probe.Times[0])
                              / (probe.Times.Count - 1);
            }

            for (int k = 2; k <= (int)UpdateTier.Max; k++)
            {
                Assert.AreEqual(2d * measured[k - 1], measured[k], measured[k] * 0.02,
                    $"实测：第 {k - 1} 档 {measured[k - 1]:0.####} 秒、第 {k} 档 {measured[k]:0.####} 秒——应恰好翻倍");
            }
        }
    }
}
