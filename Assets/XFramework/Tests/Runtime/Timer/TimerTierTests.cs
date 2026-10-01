using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 档位自动升降档：选档规则、新建更近的定时器立刻升档、档位切换不改变截止时刻。
    /// <para><b>最后一条是本提交里最要紧的哨兵</b>。把档位从粗改细走的是 <c>ProcessImmediate</c>，
    /// 它会把该节点的时间基准写成传入的时刻——传错一个值（比如图省事传 <c>Time.time</c>，或干脆传 0）
    /// 就会让逻辑时钟凭空跳掉一大段，表现在使用方那里是「加了条短定时器，结果长定时器提前/推迟触发了」。
    /// 那条用例把「切换档位不动截止时刻」变成可执行的事实。</para>
    /// </summary>
    [TestFixture]
    public class TimerTierTests
    {
        #region Fixture

        private static readonly Action Noop = () => { };

        private float _originalTimeScale;
        private TimerDriver _driver;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            TimerTestKit.SetUp();
            _driver = new TimerDriver();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            TimerTestKit.TearDown();
        }

        #endregion

        #region 选档规则

        /// <summary>
        /// 选档公式与周期表绑在一起核对：D 越大档位越粗，且实测分界与
        /// <c>SelectTier</c> 的「周期 × 8 ≤ D」一致。
        /// </summary>
        [TestCase(0.1f, UpdateTier.Tier0)]
        [TestCase(0.3f, UpdateTier.Tier1)]
        [TestCase(0.5f, UpdateTier.Tier1)]
        [TestCase(1f, UpdateTier.Tier2)]
        [TestCase(2f, UpdateTier.Tier3)]
        [TestCase(5f, UpdateTier.Tier5)]
        [TestCase(10f, UpdateTier.Tier6)]
        [TestCase(60f, UpdateTier.Tier7)]
        public void SelectedTier_MatchesTheTable(float duration, UpdateTier expected)
        {
            TimerManager.After(duration, Noop);

            Assert.AreEqual(expected, TimerManager.GetDispatchTier(UpdateTimeMode.Scaled),
                $"{duration} 秒的定时器应落在 {expected}——「周期 × 8 ≤ 剩余」这条边界动了？");

            TimerManager.CancelAll();
        }

        [Test]
        public void TierPeriodOf_DoublesPerLevel()
        {
            for (int k = 2; k <= (int)UpdateTier.Max; k++)
            {
                Assert.AreEqual(2d * TimerCore.TierPeriodOf((UpdateTier)(k - 1)),
                    TimerCore.TierPeriodOf((UpdateTier)k), 1e-9,
                    $"第 {k} 档的周期应是第 {k - 1} 档的两倍");
            }
        }

        #endregion

        #region 升档

        /// <summary>
        /// 停在粗档时新建一条更近的定时器，档位必须<b>当场</b>变细——否则它要等一个旧档周期
        /// （Tier7 下是两秒多）才会被第一次看到。
        /// </summary>
        [Test]
        public void CreatingANearerTimer_LiftsTheTierImmediately()
        {
            TimerManager.After(60f, Noop);
            _driver.Advance(2);
            Assert.AreEqual(UpdateTier.Tier7, TimerManager.GetDispatchTier(UpdateTimeMode.Scaled));

            TimerManager.After(0.1f, Noop);

            Assert.AreEqual(UpdateTier.Tier0, TimerManager.GetDispatchTier(UpdateTimeMode.Scaled),
                "新建的 0.1 秒定时器必须立刻把驱动器拉回 Tier0");
        }

        /// <summary>
        /// 逆方向不成立：更粗的请求一律忽略，拉粗由每趟扫尾的返回值决定（那里才知道真实的最近截止）。
        /// </summary>
        [Test]
        public void CoarserRequest_IsIgnored()
        {
            TimerManager.After(0.1f, Noop);
            _driver.Advance(2);
            Assert.AreEqual(UpdateTier.Tier0, TimerManager.GetDispatchTier(UpdateTimeMode.Scaled));

            TimerManager.After(60f, Noop);

            Assert.AreEqual(UpdateTier.Tier0, TimerManager.GetDispatchTier(UpdateTimeMode.Scaled),
                "多一条长定时器不该把已经变细的档位拉粗——那会拖慢那条短的");
        }

        /// <summary>
        /// 到期前档位会自动变细：离截止远时降频、临近时升回 Tier0，这正是「自动降频」承诺的形状。
        /// </summary>
        [Test]
        public void TierRefinesAsTheDeadlineApproaches()
        {
            TimerManager.After(5f, Noop);
            _driver.Advance(2);

            UpdateTier atStart = TimerManager.GetDispatchTier(UpdateTimeMode.Scaled);
            Assert.Greater((int)atStart, (int)UpdateTier.Tier0, "5 秒的定时器起步不该停在每帧档");

            // 走到只剩 0.1 秒：剩下的余量已经不足 8 个 Tier5 周期，档位必须一路细下来
            _driver.Advance(300);

            Assert.AreEqual(UpdateTier.Tier0, TimerManager.GetDispatchTier(UpdateTimeMode.Scaled),
                "临近截止时必须回到每帧档，否则触发会晚将近一整个粗档周期");
        }

        /// <summary>
        /// 「自动降频」的<b>端到端</b>观察：一条 60 秒的定时器在 4 秒里只让驱动器跑起来两三次。
        /// <para>逻辑时钟只在该轴的派发里前进，因此 <see cref="TimerHandle.Remaining"/> 是<b>阶梯状</b>的
        /// ——每次跳跃的跨度就是当前的档位周期。降频若没生效，它会随每一帧平滑下滑，取样值的个数就会
        /// 接近帧数。</para>
        /// <para>另两条用例分别锁住「选到了粗档」与「粗档在 Update 里的真实周期」，本用例把两者接起来：
        /// 少了它，一个「选档算错但仍自洽」的实现也能让前两条各自通过。</para>
        /// </summary>
        [Test]
        public void CoarseTier_MakesRemainingStepInsteadOfSlide()
        {
            TimerHandle handle = TimerManager.After(60f, Noop);
            _driver.Advance(2);

            var samples = new HashSet<float>();
            for (int i = 0; i < 240; i++) // 4 秒
            {
                _driver.Advance(1);
                samples.Add(handle.Remaining);
            }

            Assert.Greater(samples.Count, 1, "剩余量一动不动，说明时钟根本没前进");
            Assert.LessOrEqual(samples.Count, 4,
                $"4 秒里剩余量出现了 {samples.Count} 个不同取值——驱动器几乎每帧都在跑，降频没生效");
        }

        [Test]
        public void NoActiveTimers_GoesIdleAtTier0()
        {
            TimerManager.After(60f, Noop).Dispose();

            Assert.AreEqual(UpdateTier.Tier0, TimerManager.GetDispatchTier(UpdateTimeMode.Scaled));
            Assert.IsFalse(TimerManager.IsTickerRegistered(UpdateTimeMode.Scaled));
        }

        #endregion

        #region 档位切换不动截止时刻（哨兵）

        /// <summary>
        /// 中途把档位从粗拉到细（走 <c>ProcessImmediate</c> 那条通道）之后，长定时器的触发时刻必须
        /// <b>逐字不变</b>。
        /// <para>若时间基准被写错，逻辑时钟会凭空跳掉一大段，触发时刻会明显偏早或偏晚——本用例就是
        /// 那条通道的哨兵。</para>
        /// </summary>
        [Test]
        public void TierSwitch_DoesNotMoveTheDeadline()
        {
            double created = _driver.ScaledTime;
            double firedAt = -1d;

            TimerManager.After(3f, () => firedAt = _driver.ScaledTime);
            _driver.Advance(30); // 0.5 秒，此时驱动器停在 Tier5 之类的粗档上

            UpdateTier before = TimerManager.GetDispatchTier(UpdateTimeMode.Scaled);

            // 插入一条短定时器把档位拉到 Tier0——这一次切换会走 ProcessImmediate
            var burst = new TimerFires(_driver);
            TimerManager.Every(0.05f, burst.Record);

            Assert.Less((int)TimerManager.GetDispatchTier(UpdateTimeMode.Scaled), (int)before,
                "本用例的前提是这次创建真的把档位拉细了");

            _driver.Advance(240);

            Assert.Greater(burst.Count, 10, "短定时器应一直在跑，说明档位确实细了");
            Assert.GreaterOrEqual(firedAt, 0d, "3 秒的定时器应当已经触发");

            // 容差给到 0.2 秒：粗档结束、细档启动的交界处本来就有不到一帧的量化，
            // 而「时间基准被写错」的偏差是半秒到数秒量级，两者不会混淆
            Assert.AreEqual(3d, firedAt - created, 0.2d,
                "切换档位动了时间基准——长定时器的触发时刻被推走了");
        }

        #endregion
    }
}
