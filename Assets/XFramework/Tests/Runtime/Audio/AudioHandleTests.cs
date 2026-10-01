using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 句柄语义：代际安全、幂等、值语义。
    /// <para><b>代际安全是本模块最容易写错、也最难在使用中察觉的一条</b>：槽位复用后旧句柄若还能停掉
    /// 新播放，症状是「随机有个音效被掐断」，从现象几乎回不到原因。这里把它钉死。</para>
    /// </summary>
    [TestFixture]
    public class AudioHandleTests
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
        public void Play_ReturnsHandleThatReportsPlaying()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsFalse(handle.IsDefault);
            Assert.IsTrue(AudioManager.IsPlaying(handle));
        }

        [Test]
        public void Stop_MakesHandleStopReportingPlaying()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(AudioManager.Stop(handle));
            Assert.IsFalse(AudioManager.IsPlaying(handle));
        }

        [Test]
        public void Stop_IsIdempotent()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(AudioManager.Stop(handle));
            Assert.IsFalse(AudioManager.Stop(handle), "第二次 Stop 应落空而不是误伤别的播放");
            Assert.IsFalse(AudioManager.Stop(handle));
        }

        [Test]
        public void Handle_StopInstanceMethod_MatchesFacade()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(handle.Stop());
            Assert.IsFalse(AudioManager.IsPlaying(handle));
        }

        [Test]
        public void Handle_IsAValueType_CopiesStayValid()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            var copy = handle;

            Assert.IsTrue(AudioManager.IsPlaying(copy));
            Assert.IsTrue(copy.Stop());
            Assert.IsFalse(AudioManager.IsPlaying(handle), "复制出去的句柄指向的是同一次播放");
        }

        [Test]
        public void IsPlaying_ForUnknownSlot_IsFalse()
        {
            Assert.IsFalse(AudioManager.IsPlaying(new AudioHandle(99, 1)));
        }

        #endregion

        #region 代际安全（核心）

        /// <summary>
        /// 槽位被复用之后，旧句柄<b>不能</b>停掉那个位置上新的播放。
        /// </summary>
        [Test]
        public void Stop_StaleHandleAfterSlotReuse_DoesNotStopTheNewPlayback()
        {
            var old = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsTrue(AudioManager.Stop(old));

            var fresh = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.AreEqual(old.Id, fresh.Id, "前提：新播放复用了同一个槽位");
            Assert.AreNotEqual(old.Generation, fresh.Generation);

            Assert.IsFalse(AudioManager.Stop(old), "过期句柄的 Stop 必须落空");
            Assert.IsTrue(AudioManager.IsPlaying(fresh), "新播放不能因为旧句柄而被掐断");
        }

        /// <summary>
        /// 反过来：旧句柄也不能把新播放<b>误报成在播</b>。
        /// </summary>
        [Test]
        public void IsPlaying_StaleHandleAfterSlotReuse_IsFalse()
        {
            var old = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Stop(old);

            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            Assert.IsFalse(AudioManager.IsPlaying(old), "旧句柄不该报告同槽位上新的那次播放");
        }

        /// <summary>
        /// 已被停止的句柄在槽位尚未被复用之前也不该「复活」。
        /// </summary>
        [Test]
        public void IsPlaying_AfterStopAndBeforeReuse_IsFalse()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Stop(handle);

            Assert.IsFalse(AudioManager.IsPlaying(handle));
            Assert.IsFalse(AudioManager.Stop(handle));
        }

        #endregion

        #region 默认句柄

        [Test]
        public void DefaultHandle_IsDefault()
        {
            Assert.IsTrue(default(AudioHandle).IsDefault);
            Assert.IsFalse(AudioManager.Play("audio/a").IsDefault);
        }

        [Test]
        public void DefaultHandle_StopAndIsPlaying_AreSafe()
        {
            Assert.IsFalse(AudioManager.Stop(default));
            Assert.IsFalse(AudioManager.IsPlaying(default));
            Assert.IsFalse(default(AudioHandle).Stop());
        }

        #endregion

        #region 多槽位互不干扰

        [Test]
        public void StoppingOnePlayback_LeavesOthersUntouched()
        {
            var a = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            var b = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            AudioManager.Stop(a);

            Assert.IsFalse(AudioManager.IsPlaying(a));
            Assert.IsTrue(AudioManager.IsPlaying(b), "停一个不该连带停另一个");
        }

        #endregion
    }
}
