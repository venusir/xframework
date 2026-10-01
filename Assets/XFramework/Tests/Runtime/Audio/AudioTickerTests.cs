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
    /// Unity 的音频子系统照常推进：<c>isPlaying</c> 会变 <c>false</c>、<c>time</c> 会前进。
    /// 探针记录见 <c>Documentation/Modules/Audio.md</c> 的实测记录。</para>
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
            int baseline = UpdateManager.TotalCount;

            Assert.AreEqual(baseline, UpdateManager.TotalCount, "没播过任何东西时不该占用调度槽位");
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
