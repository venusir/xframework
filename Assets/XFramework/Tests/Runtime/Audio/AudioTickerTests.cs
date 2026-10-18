using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 回收：播完的槽位被收回、循环的不被自动回收、闲置时 ticker 被注销。
    /// <para><b>这些用例走真实播放驱动</b>，不造内部 seam——前置探针已实测确认 batchmode（无音频设备）下
    /// Unity 的音频子系统照常推进：<c>isPlaying</c> 会变 <c>false</c>、<c>time</c> 会前进。</para>
    /// </summary>
    [TestFixture]
    public class AudioTickerTests
    {
        #region Setup

        private FakeAudioClipLoader _loader;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoDriveEnabled = true;
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _clip = AudioTestKit.CreateClip("short", 0.1f);
            _loader = new FakeAudioClipLoader { Clip = _clip };
            AudioManager.SetInstance(new AudioManagerImpl(new AudioInitOptions { MaxVoices = 4 }, _loader));
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

        #region 播完回收

        [UnityTest]
        public IEnumerator Playback_WhenClipEnds_SlotIsRecycled()
        {
            var handle = AudioManager.Play("audio/a");
            Assert.IsTrue(AudioManager.IsPlaying(handle), "前提：确实起播了");
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);

            yield return WaitRealtime(0.6f);

            Assert.IsFalse(AudioManager.IsPlaying(handle), "播完后句柄应不再报告在播");
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount, "播完的槽位必须被自动回收");
        }

        [UnityTest]
        public IEnumerator Playback_WhenClipEnds_LeaseIsReturned()
        {
            AudioManager.Play("audio/a");
            Assert.AreEqual(1, _loader.OutstandingLeases);

            yield return WaitRealtime(0.6f);

            Assert.AreEqual(1, _loader.ReleasedLeases, "回收槽位时必须归还资源租约");
            Assert.AreEqual(0, _loader.OutstandingLeases, "回收之后不该还有未归还的租约");
        }

        [UnityTest]
        public IEnumerator ReclaimedSlot_IsReused()
        {
            var first = AudioManager.Play("audio/a");
            yield return WaitRealtime(0.6f);

            var second = AudioManager.Play("audio/b");

            Assert.AreEqual(first.Id, second.Id, "回收掉的槽位应被下一次播放复用");
            Assert.AreNotEqual(first.Generation, second.Generation,
                "复用槽位必须递增代际——旧句柄的 ABA 防线（AUD-005）");
        }

        #endregion

        #region 循环与暂停

        /// <summary>
        /// 循环播放永远不会「播完」，因此不该被自动回收——它只能被显式停止。
        /// </summary>
        [UnityTest]
        public IEnumerator LoopingPlayback_IsNotAutoRecycled()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsTrue(AudioManager.IsPlaying(handle));

            yield return WaitRealtime(0.6f);   // 远超 clip 的 0.1s

            Assert.IsTrue(AudioManager.IsPlaying(handle), "循环播放不该被回收");
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);

            Assert.IsTrue(AudioManager.Stop(handle));
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        #endregion

        #region ticker 的注册与注销

        [UnityTest]
        public IEnumerator Ticker_IsUnregisteredWhenNothingIsPlaying()
        {
            int baseline = UpdateManager.TotalCount;

            AudioManager.Play("audio/a");
            Assert.Greater(UpdateManager.TotalCount, baseline, "有活跃播放时 ticker 应挂在调度器上");

            yield return WaitRealtime(0.6f);

            Assert.AreEqual(baseline, UpdateManager.TotalCount,
                "最后一个播放结束后 ticker 必须注销——闲置时不该还有帧开销");
        }

        [Test]
        public void NoPlayback_DoesNotRegisterTicker()
        {
            // AUD-005：原断言比较的是同一个表达式——恒真。fixture 已清空调度器，直接断言基线为 0。
            Assert.AreEqual(0, UpdateManager.TotalCount, "前提：fixture 已清空调度器");

            // 走一遍读路径：读 API 不得懒注册 ticker
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);

            Assert.AreEqual(0, UpdateManager.TotalCount, "没播过任何东西时不该占用调度槽位");
        }

        /// <summary>
        /// AUD-013：<see cref="AudioListener.pause"/> 是全局暂停，不是 <see cref="AudioSource.Pause"/>。
        /// 若全局暂停也让 <c>isPlaying</c> 变 false，<c>Sweep</c> 会把在播的一次性音效当成「播完」回收。
        /// 本用例先做探针、再作为回归锁：暂停期间句柄必须仍在播。
        /// </summary>
        [UnityTest]
        public IEnumerator GlobalListenerPause_DoesNotRecycleOneShot()
        {
            var longClip = AudioTestKit.CreateClip("long", 1f);
            _loader.Clip = longClip;
            bool originalPause = AudioListener.pause;
            try
            {
                var handle = AudioManager.Play("audio/a");
                yield return null;

                AudioListener.pause = true;
                yield return WaitRealtime(0.2f);

                Assert.IsTrue(AudioManager.IsPlaying(handle),
                    "全局暂停期间一次性音效不得被 Sweep 回收（isPlaying 的暂停语义不改变播放存在）");
            }
            finally
            {
                AudioListener.pause = originalPause;
                UnityEngine.Object.DestroyImmediate(longClip);
            }
        }

        #endregion

        #region Helpers

        private static IEnumerator WaitRealtime(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        #endregion
    }
}
