using System;
using System.Collections;
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

        #region Helpers

        private static AudioHost[] FindHosts()
            => UnityEngine.Object.FindObjectsByType<AudioHost>(FindObjectsInactive.Include);

        #endregion
    }
}
