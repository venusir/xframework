using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 查询面：活跃计数、按通道计数、<c>StopAll</c> 的返回值。
    /// <para>计数是「槽位表里非空闲的条数」，<b>含加载中与已暂停的</b>——它们都实打实地占着播放源。
    /// 把它当成「正在出声的数量」会误判，README 有专门一句说这件事。</para>
    /// </summary>
    [TestFixture]
    public class AudioQueryTests
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

        #region 活跃计数

        [Test]
        public void ActiveVoiceCount_StartsAtZero()
        {
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ActiveVoiceCount_CountsEachPlayback()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);

            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.AreEqual(2, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ActiveVoiceCount_DropsAfterStop()
        {
            var a = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            AudioManager.Stop(a);

            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ActiveVoiceCount_IncludesPausedVoices()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Pause();

            Assert.AreEqual(1, AudioManager.ActiveVoiceCount,
                "暂停中的播放仍占着槽位，计数必须把它算进去");
        }

        [UnityTest]
        public IEnumerator ActiveVoiceCount_IncludesLoadingVoices()
        {
            _loader.DelayFrames = 3;

            AudioManager.Play("audio/a");
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "已经预占了槽位的加载中播放也算活跃");

            yield return null;
            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "加载完成后仍是 1");
        }

        #endregion

        #region 按通道计数

        [Test]
        public void GetActiveVoiceCount_FiltersByChannel()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Se, loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(AudioChannels.Se, loop: true));
            AudioManager.Play("audio/c", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            Assert.AreEqual(2, AudioManager.GetActiveVoiceCount(AudioChannels.Se));
            Assert.AreEqual(1, AudioManager.GetActiveVoiceCount(AudioChannels.Bgm));
            Assert.AreEqual(0, AudioManager.GetActiveVoiceCount(AudioChannels.Voice));
        }

        [Test]
        public void GetActiveVoiceCount_NullChannel_MeansDefaultChannel()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));   // 通道省略 ⇒ 默认通道

            Assert.AreEqual(1, AudioManager.GetActiveVoiceCount(null));
            Assert.AreEqual(1, AudioManager.GetActiveVoiceCount(AudioChannels.Default));
        }

        #endregion

        #region StopAll

        [Test]
        public void StopAll_StopsEverythingAndReturnsTheCount()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Se, loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            int stopped = AudioManager.StopAll();

            Assert.AreEqual(2, stopped, "返回值应是实际停掉的数量");
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void StopAll_WithChannel_OnlyStopsThatChannel()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Se, loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            int stopped = AudioManager.StopAll(AudioChannels.Se);

            Assert.AreEqual(1, stopped);
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(1, AudioManager.GetActiveVoiceCount(AudioChannels.Bgm));
        }

        [Test]
        public void StopAll_WithNoMatch_ReturnsZero()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Se, loop: true));

            Assert.AreEqual(0, AudioManager.StopAll(AudioChannels.Bgm));
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "没匹配到就不该动任何东西");
        }

        [Test]
        public void StopAll_OnEmptyPool_ReturnsZero()
        {
            Assert.AreEqual(0, AudioManager.StopAll());
        }

        [Test]
        public void StopAll_ThenPlayAgain_Works()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.StopAll();

            var handle = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            Assert.IsFalse(handle.IsDefault, "StopAll 之后池应完全可用");
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);
        }

        #endregion
    }
}
