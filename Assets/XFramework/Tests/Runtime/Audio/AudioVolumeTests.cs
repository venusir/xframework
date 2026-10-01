using System;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 音量：三档相乘、变更即时作用于正在播放的声音、静音。
    /// <para><b>断言的是写进 <c>AudioSource.volume</c> 的实际值</b>，不经引擎的音量语义
    /// （<c>AudioListener.volume</c>、Mixer 总线都不参与）——框架的承诺就是「写到播放源上的值是多少」。</para>
    /// </summary>
    [TestFixture]
    public class AudioVolumeTests
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

        #region 起播时写值

        [Test]
        public void Play_AppliesVolumeScaleToTheSource()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(volumeScale: 0.5f, loop: true));

            Assert.AreEqual(0.5f, AudioTestKit.VolumeOf(handle), 0.001f);
        }

        [Test]
        public void Play_WithDefaultOptions_IsFullVolume()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.AreEqual(1f, AudioTestKit.VolumeOf(handle), 0.001f);
        }

        /// <summary>
        /// 归一化：<c>VolumeScale</c> 非正一律按 1 处理。这条是 <c>default(AudioPlayOptions)</c> 安全的前提——
        /// 不归一化的话，忘写 options 会得到一个完全静音的声音，比抛异常更难查。
        /// </summary>
        [Test]
        public void Play_WithNonPositiveVolumeScale_NormalizesToFullVolume()
        {
            var zero = AudioManager.Play("audio/a", new AudioPlayOptions(volumeScale: 0f, loop: true));
            var negative = AudioManager.Play("audio/b", new AudioPlayOptions(volumeScale: -1f, loop: true));

            Assert.AreEqual(1f, AudioTestKit.VolumeOf(zero), 0.001f);
            Assert.AreEqual(1f, AudioTestKit.VolumeOf(negative), 0.001f);
        }

        /// <summary>省略 options 实参走的是 <c>default(AudioPlayOptions)</c>（字段全零），同样必须安全。</summary>
        [Test]
        public void Play_WithoutOptions_IsFullVolume()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.AreEqual(1f, AudioTestKit.VolumeOf(handle), 0.001f);
        }

        #endregion

        #region 变更即时生效

        [Test]
        public void MasterVolume_Change_UpdatesPlayingSourceImmediately()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.MasterVolume = 0.25f;

            Assert.AreEqual(0.25f, AudioTestKit.VolumeOf(handle), 0.001f, "改主音量应立刻作用到正在播的声音");
        }

        [Test]
        public void ChannelVolume_Change_UpdatesPlayingSourceImmediately()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            AudioManager.SetChannelVolume(AudioChannels.Bgm, 0.4f);

            Assert.AreEqual(0.4f, AudioTestKit.VolumeOf(handle), 0.001f);
        }

        [Test]
        public void EffectiveVolume_MultipliesTheThreeFactors()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Bgm, 0.5f, loop: true));

            AudioManager.MasterVolume = 0.5f;
            AudioManager.SetChannelVolume(AudioChannels.Bgm, 0.5f);

            // 0.5(主) × 0.5(通道) × 0.5(单次) = 0.125
            Assert.AreEqual(0.125f, AudioTestKit.VolumeOf(handle), 0.001f);
        }

        [Test]
        public void VolumeChange_LeavesOtherChannelsUntouched()
        {
            var se = AudioManager.Play("audio/se", new AudioPlayOptions(AudioChannels.Se, loop: true));
            var bgm = AudioManager.Play("audio/bgm", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            AudioManager.SetChannelVolume(AudioChannels.Se, 0.3f);

            Assert.AreEqual(0.3f, AudioTestKit.VolumeOf(se), 0.001f);
            Assert.AreEqual(1f, AudioTestKit.VolumeOf(bgm), 0.001f, "改一个通道不该影响另一个");
        }

        #endregion

        #region 静音

        [Test]
        public void MasterMuted_ZeroesEffectiveVolume()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.MasterMuted = true;

            Assert.IsTrue(AudioManager.MasterMuted);
            Assert.AreEqual(0f, AudioTestKit.VolumeOf(handle), 0.001f, "主静音时实际音量为 0");
        }

        [Test]
        public void MasterMuted_DoesNotStopPlayback()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.MasterMuted = true;

            Assert.IsTrue(AudioManager.IsPlaying(handle), "静音是「听不见」而不是「停掉」——恢复时不该从头开始");
        }

        [Test]
        public void ChannelMuted_ZeroesOnlyThatChannel()
        {
            var se = AudioManager.Play("audio/se", new AudioPlayOptions(AudioChannels.Se, loop: true));
            var bgm = AudioManager.Play("audio/bgm", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            AudioManager.SetChannelMuted(AudioChannels.Se, true);

            Assert.AreEqual(0f, AudioTestKit.VolumeOf(se), 0.001f);
            Assert.AreEqual(1f, AudioTestKit.VolumeOf(bgm), 0.001f, "别的通道不该被牵连");
            Assert.IsTrue(AudioManager.IsPlaying(se), "静音的通道仍在播，只是听不见");
        }

        [Test]
        public void Unmuting_RestoresTheVolume()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(volumeScale: 0.6f, loop: true));

            AudioManager.MasterMuted = true;
            AudioManager.MasterMuted = false;

            Assert.AreEqual(0.6f, AudioTestKit.VolumeOf(handle), 0.001f, "取消静音应恢复原本的三档相乘结果");
        }

        #endregion

        #region 钳制与初始化

        [Test]
        public void MasterVolume_IsClampedToUnitRange()
        {
            AudioManager.MasterVolume = 2.5f;
            Assert.AreEqual(1f, AudioManager.MasterVolume, 0.001f);

            AudioManager.MasterVolume = -1f;
            Assert.AreEqual(0f, AudioManager.MasterVolume, 0.001f);
        }

        [Test]
        public void ChannelVolume_IsClampedToUnitRange()
        {
            AudioManager.SetChannelVolume(AudioChannels.Se, 2.5f);
            Assert.AreEqual(1f, AudioManager.GetChannelVolume(AudioChannels.Se), 0.001f);

            AudioManager.SetChannelVolume(AudioChannels.Se, -1f);
            Assert.AreEqual(0f, AudioManager.GetChannelVolume(AudioChannels.Se), 0.001f);
        }

        [Test]
        public void InitOptions_MasterVolumeAndMute_AreApplied()
        {
            AudioManager.Destroy();
            AudioManager.SetInstance(new AudioManagerImpl(
                new AudioInitOptions { MaxVoices = 2, MasterVolume = 0.5f, MasterMuted = true }, _loader));

            Assert.AreEqual(0.5f, AudioManager.MasterVolume, 0.001f);
            Assert.IsTrue(AudioManager.MasterMuted);

            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.AreEqual(0f, AudioTestKit.VolumeOf(handle), 0.001f, "初始化即静音时，首个播放就该是 0");
        }

        [Test]
        public void InitOptions_OutOfRangeMasterVolume_IsClamped()
        {
            AudioManager.Destroy();
            AudioManager.SetInstance(new AudioManagerImpl(
                new AudioInitOptions { MaxVoices = 2, MasterVolume = 9f }, _loader));

            Assert.AreEqual(1f, AudioManager.MasterVolume, 0.001f);
        }

        #endregion

        #region 与停止的界

        [Test]
        public void VolumeChange_AfterStop_DoesNotResurrectPlayback()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Stop(handle);

            AudioManager.MasterVolume = 0.3f;

            Assert.IsFalse(AudioManager.IsPlaying(handle), "改音量不该把已停止的播放弄回来");
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        #endregion
    }
}
