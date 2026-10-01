using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 暂停与恢复。
    /// <para><b>本组最要紧的一条是「暂停中的播放不会被自动回收」</b>：前置探针实测确认 <c>Pause()</c> 之后
    /// <c>isPlaying</c> 就是 <c>false</c>——若回收判据只看它，暂停一场战斗会把整池播放源当场收光。
    /// 这正是内部状态机存在的理由。</para>
    /// </summary>
    [TestFixture]
    public class AudioPauseTests
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

        #region 基本语义

        [Test]
        public void Initially_IsNotPaused()
        {
            Assert.IsFalse(AudioManager.IsPaused);
        }

        [Test]
        public void Pause_SetsIsPaused()
        {
            AudioManager.Pause();

            Assert.IsTrue(AudioManager.IsPaused);
        }

        [Test]
        public void Resume_ClearsIsPaused()
        {
            AudioManager.Pause();
            AudioManager.Resume();

            Assert.IsFalse(AudioManager.IsPaused);
        }

        [Test]
        public void Pause_Twice_IsIdempotent()
        {
            AudioManager.Pause();
            AudioManager.Pause();

            Assert.IsTrue(AudioManager.IsPaused);
        }

        [Test]
        public void Resume_WithoutPause_IsSafe()
        {
            AudioManager.Resume();

            Assert.IsFalse(AudioManager.IsPaused);
        }

        [Test]
        public void Pause_IsSafeWhenNothingIsPlaying()
        {
            AudioManager.Pause();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.IsTrue(AudioManager.IsPaused);
        }

        #endregion

        #region 暂停不是停止

        [Test]
        public void Pause_DoesNotStopPlayback()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Pause();

            Assert.IsTrue(AudioManager.IsPlaying(handle), "暂停中的播放仍报告在播——它不是被停掉，只是暂时不出声");
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "暂停不该释放槽位");
        }

        [Test]
        public void Resume_KeepsTheSameHandlePlaying()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Pause();
            AudioManager.Resume();

            Assert.IsTrue(AudioManager.IsPlaying(handle));
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void Stop_OnPausedPlayback_Works()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Pause();

            Assert.IsTrue(AudioManager.Stop(handle), "暂停中的播放应当能被正常停止");
            Assert.IsFalse(AudioManager.IsPlaying(handle));
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        #endregion

        #region 管理器级闸门

        /// <summary>
        /// 暂停期间新起的播放也进入暂停。否则「暂停期间新起的声音照放，恢复后却归它管」自相矛盾。
        /// </summary>
        [Test]
        public void PlaybackStartedWhilePaused_IsAlsoPaused()
        {
            AudioManager.Pause();

            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(AudioManager.IsPlaying(handle), "它确实起了，只是被暂停");
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);

            AudioManager.Resume();

            Assert.IsTrue(AudioManager.IsPlaying(handle), "恢复后应继续在播");
        }

        #endregion

        #region 回收判据（核心）

        /// <summary>
        /// 暂停中的播放<b>不</b>被回收——即便它远超 clip 时长。
        /// <para>这条直接对应探针的实测结论：<c>Pause()</c> 之后 <c>isPlaying</c> 是 <c>false</c>，
        /// 只看它就会把暂停中的播放当场收掉。</para>
        /// </summary>
        [UnityTest]
        public IEnumerator PausedPlayback_IsNotRecycled()
        {
            var handle = AudioManager.Play("audio/a");
            Assert.IsTrue(AudioManager.IsPlaying(handle));

            AudioManager.Pause();

            yield return WaitRealtime(0.6f);   // 远超 clip 的 0.1s

            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "暂停中的槽位不该被回收");
            Assert.IsTrue(AudioManager.IsPlaying(handle));

            AudioManager.Resume();
            Assert.IsTrue(AudioManager.IsPlaying(handle), "恢复后仍在播");
        }

        [UnityTest]
        public IEnumerator ResumeThenFinish_RecyclesNormally()
        {
            var handle = AudioManager.Play("audio/a");

            AudioManager.Pause();
            yield return WaitRealtime(0.2f);
            AudioManager.Resume();

            yield return WaitRealtime(0.6f);

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount, "恢复之后照常走完并回收");
            Assert.IsFalse(AudioManager.IsPlaying(handle));
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
