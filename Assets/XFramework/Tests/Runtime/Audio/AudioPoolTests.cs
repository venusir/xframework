using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 播放源池与宿主的行为：惰性创建、槽位复用、容量上限与限流告警。
    /// <para>全部经公开门面 + <see cref="AudioHandle.Id"/> / <see cref="AudioHandle.Generation"/> 观察，
    /// 不依赖任何为测试而加的生产代码缺口。</para>
    /// </summary>
    [TestFixture]
    public class AudioPoolTests
    {
        #region Setup

        private FakeAudioClipLoader _loader;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _clip = AudioTestKit.CreateClip();
            _loader = new FakeAudioClipLoader { Clip = _clip };
            AudioManager.SetInstance(new AudioManagerImpl(new AudioInitOptions { MaxVoices = 2 }, _loader));
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();

            if (_clip != null)
                UnityEngine.Object.DestroyImmediate(_clip);
        }

        #endregion

        #region 宿主

        /// <summary>
        /// 这三个用例必须是 <c>[UnityTest]</c> 并先等一帧：<c>Object.Destroy</c> 是<b>延迟</b>的，宿主要到
        /// 帧末才真正消失，而同步的 <c>[Test]</c> 里永远等不到帧末——上一个用例留下的待销毁宿主会全部被数进来。
        /// </summary>
        [UnityTest]
        public IEnumerator Play_CreatesHostLazily()
        {
            yield return null;   // 冲掉前一个用例留下的待销毁宿主

            Assert.AreEqual(0, FindHosts().Length, "只是初始化、还没播放时不该建宿主——从不播放的项目一个对象都不该建");

            AudioManager.Play("audio/a");

            Assert.AreEqual(1, FindHosts().Length, "首个播放应惰性创建宿主");
        }

        [UnityTest]
        public IEnumerator MultiplePlays_ShareOneHost()
        {
            yield return null;

            AudioManager.Play("audio/a");
            AudioManager.Play("audio/b");

            Assert.AreEqual(1, FindHosts().Length, "宿主是模块级单例，不该按播放次数增长");
        }

        /// <summary>
        /// <c>PrewarmVoices = true</c>：初始化时就建宿主与全部槽位——与
        /// <see cref="Play_CreatesHostLazily"/> 的惰性默认配成对照。预热的价值是把
        /// 「首次播放时创建 N 个对象」的开销提前，避开第一声的卡顿。
        /// </summary>
        [UnityTest]
        public IEnumerator PrewarmVoices_True_CreatesHostAndAllSlotsUpfront()
        {
            yield return null;   // 冲掉前一个用例留下的待销毁宿主

            AudioManager.Destroy();
            yield return null;   // Destroy 是延迟的

            AudioManager.SetInstance(new AudioManagerImpl(
                new AudioInitOptions { MaxVoices = 3, PrewarmVoices = true }, _loader));

            Assert.AreEqual(1, FindHosts().Length, "预热应连同宿主一起建");
            Assert.AreEqual(3, CountVoices(), "预热应一次建满 MaxVoices 个播放源，而不是等到播放");
        }

        [UnityTest]
        public IEnumerator Destroy_DestroysHost()
        {
            yield return null;

            AudioManager.Play("audio/a");
            Assert.AreEqual(1, FindHosts().Length);

            AudioManager.Destroy();
            yield return null;   // Destroy 是延迟的

            Assert.AreEqual(0, FindHosts().Length, "Destroy 必须连同宿主一起清掉，否则切换实现后会留下孤儿对象");
        }

        #endregion

        #region 槽位复用与代际

        [Test]
        public void ReleasedSlot_IsReusedWithANewGeneration()
        {
            var first = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsFalse(first.IsDefault);

            AudioManager.Stop(first);

            var second = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.IsFalse(second.IsDefault);

            Assert.AreEqual(first.Id, second.Id, "释放后的槽位应被复用，池不该无谓增长");
            Assert.AreNotEqual(first.Generation, second.Generation,
                "复用必须换一代——否则旧句柄会误停新播放");
        }

        [Test]
        public void DistinctPlaybacks_GetDistinctSlots()
        {
            var a = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            var b = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            Assert.AreNotEqual(a.Id, b.Id, "同时播放的两个声音必须各占一个槽位");
        }

        #endregion

        #region 加载中停止后立即重播（P1-1）

        /// <summary>
        /// 加载中 <c>Stop</c> 必须<b>立即</b>归还 provider 池槽，否则紧接着的重播会撞上池满窗口：
        /// 核心槽位已释放，provider 的 Loading 槽却还占着，重播拿到一个必然失效的预占句柄。
        /// </summary>
        [UnityTest]
        public IEnumerator StopWhileLoading_ImmediateReplay_StartsTheNewPlayback()
        {
            AudioManager.Destroy();

            var loader = new FakeAudioClipLoader { Clip = _clip, DelayFrames = 3 };
            AudioManager.SetInstance(new AudioManagerImpl(new AudioInitOptions { MaxVoices = 1 }, loader));

            var first = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsFalse(first.IsDefault, "前提：首个播放已预占槽位");
            Assert.AreEqual(1, loader.LoadCount, "前提：加载已发起且未完成");
            Assert.AreEqual(0, loader.IssuedLeases);

            Assert.IsTrue(AudioManager.Stop(first), "加载中的播放也应能被停下");

            var second = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.IsFalse(second.IsDefault, "Stop 后立即重播不得撞上 provider 的容量窗口");

            yield return null;
            yield return null;
            yield return null;
            yield return null;   // 越过两个加载窗口，让晚到的续体也跑完

            Assert.IsTrue(AudioManager.IsPlaying(second), "第二个播放应已起播");
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "只应有第二个播放活着");
            Assert.AreEqual(1, loader.OutstandingLeases, "此时只该持有第二个播放的租约");

            AudioManager.Stop(second);

            Assert.AreEqual(0, loader.OutstandingLeases, "两个租约都必须归还，且都不能重复释放");
        }

        /// <summary>
        /// 同样的窗口走 <see cref="AudioManager.PlayAsync"/>：Stop 之后的重播必须等到加载完成并返回有效句柄，
        /// 而不是 default。
        /// </summary>
        [Test]
        public async Task StopWhileLoading_ImmediatePlayAsync_ReturnsValidHandle()
        {
            AudioManager.Destroy();

            var loader = new FakeAudioClipLoader { Clip = _clip, DelayFrames = 3 };
            AudioManager.SetInstance(new AudioManagerImpl(new AudioInitOptions { MaxVoices = 1 }, loader));

            var first = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsTrue(AudioManager.Stop(first), "前提：加载中的播放已停下");

            var secondTask = AudioManager.PlayAsync("audio/b", new AudioPlayOptions(loop: true));

            await UniTask.DelayFrame(4);

            var second = await secondTask;

            Assert.IsFalse(second.IsDefault, "重播必须拿到有效句柄，而不是被 provider 容量窗口丢弃");
            Assert.IsTrue(AudioManager.IsPlaying(second));

            AudioManager.Stop(second);
            Assert.AreEqual(0, loader.OutstandingLeases);
        }

        #endregion

        #region 容量上限

        [Test]
        public void Play_WhenPoolExhausted_ReturnsDefaultHandleAndWarns()
        {
            var a = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            var b = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.IsFalse(a.IsDefault);
            Assert.IsFalse(b.IsDefault);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Audio\] 播放源已耗尽"));

            var c = AudioManager.Play("audio/c", new AudioPlayOptions(loop: true));

            Assert.IsTrue(c.IsDefault, "池满时必须退化成默认句柄，而不是静默抢掉别人的播放");
            Assert.IsFalse(AudioManager.IsPlaying(c));
            Assert.IsFalse(AudioManager.Stop(c), "默认句柄上的 Stop 应安全返回 false");
        }

        [Test]
        public void Exhaustion_WarnsOnlyOncePerExhaustionPeriod()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            int warnings = 0;
            Application.LogCallback handler = (message, stackTrace, type) =>
            {
                if (type == LogType.Warning && message.Contains("播放源已耗尽"))
                    warnings++;
            };

            Application.logMessageReceived += handler;
            try
            {
                // 连续三次丢弃：同一段耗尽期只该提醒一次，否则一帧几十个音效会把控制台刷爆
                AudioManager.Play("audio/c", new AudioPlayOptions(loop: true));
                AudioManager.Play("audio/d", new AudioPlayOptions(loop: true));
                AudioManager.Play("audio/e", new AudioPlayOptions(loop: true));
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            Assert.AreEqual(1, warnings, "同一段耗尽期应只告警一次");
        }

        [Test]
        public void Exhaustion_CanWarnAgainAfterASlotIsFreed()
        {
            var a = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Audio\] 播放源已耗尽"));
            AudioManager.Play("audio/c", new AudioPlayOptions(loop: true));

            // 释放一个槽位后，下一轮耗尽应重新提醒——否则长期运行会彻底静音且无人知道
            AudioManager.Stop(a);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Audio\] 播放源已耗尽"));
            AudioManager.Play("audio/d", new AudioPlayOptions(loop: true));   // 占回刚释放的槽位
            AudioManager.Play("audio/e", new AudioPlayOptions(loop: true));   // 再次耗尽
        }

        [Test]
        public void MaxVoices_BelowOne_IsClampedToOne()
        {
            AudioManager.Destroy();
            AudioManager.SetInstance(new AudioManagerImpl(new AudioInitOptions { MaxVoices = 0 }, _loader));

            var first = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsFalse(first.IsDefault, "MaxVoices 传 0 应被钳到 1，而不是变成一个永远播不出声的池");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Audio\] 播放源已耗尽"));
            var second = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            Assert.IsTrue(second.IsDefault);
        }

        #endregion

        #region 参数防御

        [Test]
        public void Play_WithNullLocation_Throws()
        {
            Assert.Throws<ArgumentException>(() => AudioManager.Play(null));
        }

        [Test]
        public void Play_WithBlankLocation_Throws()
        {
            Assert.Throws<ArgumentException>(() => AudioManager.Play("   "));
        }

        /// <summary>
        /// <c>"master"</c> 是保留名。把它当通道用会抛——否则会静默地建出一个没人收听的通道，
        /// 而调用方以为自己控住了总音量。
        /// </summary>
        [Test]
        public void Play_WithReservedMasterChannel_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(
                () => AudioManager.Play("audio/a", new AudioPlayOptions(AudioManagerImpl.ReservedMasterChannel)));

            StringAssert.Contains("[Audio]", ex.Message);
        }

        #endregion

        #region 短音效回收（AUD-001）

        /// <summary>
        /// 短于一个扫尾节拍的 one-shot 不得卡在 <c>Starting</c> 等 60 拍超时（AUD-001）。
        /// <para>改前：<c>Play()</c> 后只置 <c>Starting</c>，下一拍 <c>Sweep</c> 时 clip 已播完、
        /// <c>isPlaying</c> 为 false，于是占槽约 2 秒（Tier1 × 60 拍）后被强制回收并误报「起播超时」。</para>
        /// </summary>
        [UnityTest]
        public IEnumerator ShortOneShot_IsReclaimedAfterItEnds()
        {
            var shortClip = AudioTestKit.CreateClip("short", 0.01f);
            _loader.Clip = shortClip;
            try
            {
                AudioManager.Play("audio/short");
                Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "起播后应占一个槽位");

                yield return new WaitForSecondsRealtime(0.3f);

                Assert.AreEqual(0, AudioManager.ActiveVoiceCount,
                    "短于 1 拍的 one-shot 播完后应立即回收，而不是卡到起播超时");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shortClip);
            }
        }

        /// <summary>
        /// <c>UpdateManager.Clear()</c> 摘掉 ticker 后，本地 <c>_tickerRegistered</c> 仍为 true；
        /// 下一次 Play 若不向调度器核对，回收会永久停摆（AUD-002）。
        /// </summary>
        [UnityTest]
        public IEnumerator Play_AfterUpdateManagerClear_ReregistersTicker()
        {
            var shortClip = AudioTestKit.CreateClip("short-clear", 0.01f);
            _loader.Clip = shortClip;
            try
            {
                AudioManager.Play("audio/first");
                Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "前提：第一声已占槽");

                // 在 ticker 仍注册、槽位仍活跃时摘掉调度器——本地 _tickerRegistered 仍为 true
                UpdateManager.Clear();

                AudioManager.Play("audio/second");
                Assert.AreEqual(2, AudioManager.ActiveVoiceCount);

                yield return new WaitForSecondsRealtime(0.3f);

                Assert.AreEqual(0, AudioManager.ActiveVoiceCount,
                    "UpdateManager.Clear() 之后 Play 必须重新注册 ticker，否则两声音效都不会被回收（AUD-002）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shortClip);
            }
        }

        /// <summary>
        /// 播放源被外部销毁后，Sweep 读 <c>isPlaying</c> 会抛 MissingReference 并中断整轮扫尾；
        /// 必须在状态机入口按 Unity 假 null 收尾（AUD-003）。
        /// </summary>
        [UnityTest]
        public IEnumerator ExternallyDestroyedSource_IsReclaimedWithoutThrowing()
        {
            var handle = AudioManager.Play("audio/a");
            Assert.IsFalse(handle.IsDefault, "前提：已起播");
            var source = AudioTestKit.FindSourceFor(handle);
            Assert.IsNotNull(source, "前提：能找到播放源");

            LogAssert.Expect(LogType.Warning,
                new Regex(Regex.Escape("[Audio] 播放源已被外部销毁")));
            UnityEngine.Object.DestroyImmediate(source);

            yield return new WaitForSecondsRealtime(0.2f);

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount,
                "被外部销毁的播放源必须在 Sweep 中收尾回收（AUD-003）");
        }

        /// <summary>
        /// 宿主被外部整体销毁后，下一次播放不得永久命中坏源 Free 槽（R4-AUD-001）。
        /// </summary>
        [UnityTest]
        public IEnumerator ExternallyDestroyedHost_NextPlayRebuildsSlotAndSucceeds()
        {
            var first = AudioManager.Play("audio/a");
            Assert.IsFalse(first.IsDefault, "前提：已起播");
            var host = FindHosts()[0];

            LogAssert.Expect(LogType.Warning,
                new Regex(Regex.Escape("[Audio] 播放源已被外部销毁")));
            UnityEngine.Object.DestroyImmediate(host.gameObject);

            yield return new WaitForSecondsRealtime(0.2f);

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount, "前提：旧槽位已被 Sweep 回收");

            var second = AudioManager.Play("audio/b");

            Assert.IsFalse(second.IsDefault, "宿主外部销毁后下一次播放应重建槽位并成功起播");
            Assert.AreEqual(first.Id, second.Id, "应在原槽位下标重建，不新增槽位");
            Assert.IsNotNull(AudioTestKit.FindSourceFor(second), "重建后应能找到播放源");
        }

        /// <summary>
        /// 只销毁 <c>AudioSource</c> 组件时，下一次播放应在同一槽位补回组件（R4-AUD-001）。
        /// </summary>
        [UnityTest]
        public IEnumerator ExternallyDestroyedSource_NextPlayRebuildsSource()
        {
            var first = AudioManager.Play("audio/a");
            Assert.IsFalse(first.IsDefault, "前提：已起播");
            var source = AudioTestKit.FindSourceFor(first);
            Assert.IsNotNull(source, "前提：能找到播放源");

            LogAssert.Expect(LogType.Warning,
                new Regex(Regex.Escape("[Audio] 播放源已被外部销毁")));
            UnityEngine.Object.DestroyImmediate(source);

            yield return new WaitForSecondsRealtime(0.2f);

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount, "前提：旧槽位已被 Sweep 回收");

            var second = AudioManager.Play("audio/b");

            Assert.IsFalse(second.IsDefault, "只销毁 AudioSource 组件后下一次播放应补回组件");
            Assert.AreEqual(first.Id, second.Id, "应在原槽位下标重建");
            Assert.IsNotNull(AudioTestKit.FindSourceFor(second), "重建后应能找到播放源");
        }

        #endregion

        #region Helpers

        private static AudioHost[] FindHosts()
            => UnityEngine.Object.FindObjectsByType<AudioHost>(FindObjectsInactive.Include);

        /// <summary>播放源个数 = 宿主下的子节点数（每个槽位一个 <c>Voice {index}</c>）。</summary>
        private static int CountVoices()
        {
            var hosts = FindHosts();
            return hosts.Length == 0 ? 0 : hosts[0].transform.childCount;
        }

        #endregion
    }
}
