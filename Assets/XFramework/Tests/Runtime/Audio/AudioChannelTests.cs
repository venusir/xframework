using System;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 通道：开放字符串域、序数比较、惰性建状态、保留名、推荐常量。
    /// <para><b>通道刻意不是枚举</b>——锁死枚举就等于「使用方必须按某种架构组织音频」，与包级 README 的
    /// 「不预设 GamePlay 架构」相悖。先例是 <c>UILayers</c>（推荐层级常量）与 <c>XInput</c>（纯字符串动作名）。</para>
    /// </summary>
    [TestFixture]
    public class AudioChannelTests
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

        #region 推荐常量

        /// <summary>
        /// 常量值是公开契约的一部分——使用方靠它们跨模块对齐通道名，改值即破坏性变更。
        /// </summary>
        [Test]
        public void RecommendedConstants_AreStable()
        {
            Assert.AreEqual("default", AudioChannels.Default);
            Assert.AreEqual("bgm", AudioChannels.Bgm);
            Assert.AreEqual("se", AudioChannels.Se);
            Assert.AreEqual("voice", AudioChannels.Voice);
            Assert.AreEqual("ui", AudioChannels.Ui);
        }

        #endregion

        #region 查询：不建状态、返回默认值

        [Test]
        public void UnknownChannel_GetVolume_ReturnsDefaultOne()
        {
            Assert.AreEqual(1f, AudioManager.GetChannelVolume("从未声明过的通道"), 0.001f);
        }

        [Test]
        public void UnknownChannel_IsMuted_ReturnsFalse()
        {
            Assert.IsFalse(AudioManager.IsChannelMuted("从未声明过的通道"));
        }

        [Test]
        public void UnknownChannel_ActiveVoiceCount_IsZero()
        {
            Assert.AreEqual(0, AudioManager.GetActiveVoiceCount("从未声明过的通道"));
        }

        #endregion

        #region 写入：惰性建状态

        [Test]
        public void SetChannelVolume_LazilyCreatesTheChannel()
        {
            AudioManager.SetChannelVolume("自建通道", 0.3f);

            Assert.AreEqual(0.3f, AudioManager.GetChannelVolume("自建通道"), 0.001f);
        }

        [Test]
        public void SetChannelMuted_LazilyCreatesTheChannel()
        {
            AudioManager.SetChannelMuted("自建通道", true);

            Assert.IsTrue(AudioManager.IsChannelMuted("自建通道"));
        }

        [Test]
        public void UnmutingAnUnknownChannel_LeavesItDefault()
        {
            AudioManager.SetChannelMuted("自建通道", false);

            Assert.IsFalse(AudioManager.IsChannelMuted("自建通道"));
            Assert.AreEqual(1f, AudioManager.GetChannelVolume("自建通道"), 0.001f, "解除静音不该顺手改音量");
        }

        #endregion

        #region 序数比较 / 归一化

        /// <summary>
        /// 通道名区分大小写。这是公开承诺：若改用忽略大小写的比较，<c>"BGM"</c> 与 <c>"bgm"</c> 会被静默合并，
        /// 两个互不相关的作者取到同名通道时会互相串改音量——与 Lock 踩过的「键空间与相等语义」是同一类坑。
        /// </summary>
        [Test]
        public void ChannelNames_AreCaseSensitive()
        {
            AudioManager.SetChannelVolume("bgm", 0.2f);

            Assert.AreEqual(1f, AudioManager.GetChannelVolume("BGM"), 0.001f, "\"BGM\" 应是另一个通道");
            Assert.AreEqual(0.2f, AudioManager.GetChannelVolume("bgm"), 0.001f);
        }

        [Test]
        public void NullChannel_NormalizesToDefault()
        {
            AudioManager.SetChannelVolume(null, 0.4f);

            Assert.AreEqual(0.4f, AudioManager.GetChannelVolume(AudioChannels.Default), 0.001f);
        }

        [Test]
        public void EmptyChannel_NormalizesToDefault()
        {
            AudioManager.SetChannelVolume("", 0.4f);

            Assert.AreEqual(0.4f, AudioManager.GetChannelVolume(AudioChannels.Default), 0.001f);
        }

        /// <summary>播放时省略通道（<c>null</c>）落到默认通道，因此默认通道的音量能管到它。</summary>
        [Test]
        public void Play_WithoutChannel_LandsOnTheDefaultChannel()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));   // Channel 为 null

            AudioManager.SetChannelVolume(null, 0.5f);   // 即默认通道

            Assert.AreEqual(0.5f, AudioTestKit.VolumeOf(handle), 0.001f);
        }

        #endregion

        #region 保留名

        /// <summary>
        /// <c>"master"</c> 不是通道。若不拦，调用方会静默建出一个没人收听的通道，而以为自己控住了总音量。
        /// </summary>
        [Test]
        public void ReservedMasterChannel_ThrowsOnEveryWriteApi()
        {
            const string master = AudioManagerImpl.ReservedMasterChannel;

            Assert.Throws<ArgumentException>(() => AudioManager.SetChannelVolume(master, 0.5f));
            Assert.Throws<ArgumentException>(() => AudioManager.SetChannelMuted(master, true));
            Assert.Throws<ArgumentException>(() => AudioManager.RegisterChannel(master, new AudioChannelConfig()));
            Assert.Throws<ArgumentException>(() => AudioManager.Play("audio/a", new AudioPlayOptions(master)));
            Assert.Throws<ArgumentException>(() => AudioManager.StopAll(master));
        }

        [Test]
        public void ReservedMasterChannel_ExceptionMessagePointsAtTheRightApi()
        {
            var ex = Assert.Throws<ArgumentException>(
                () => AudioManager.SetChannelVolume("master", 0.5f));

            StringAssert.Contains("[Audio]", ex.Message);
            StringAssert.Contains("MasterVolume", ex.Message, "异常消息必须指出该用什么");
        }

        #endregion

        #region RegisterChannel

        [Test]
        public void RegisterChannel_AppliesConfig()
        {
            AudioManager.RegisterChannel(AudioChannels.Voice, new AudioChannelConfig { Volume = 0.7f, Muted = true });

            Assert.AreEqual(0.7f, AudioManager.GetChannelVolume(AudioChannels.Voice), 0.001f);
            Assert.IsTrue(AudioManager.IsChannelMuted(AudioChannels.Voice));
        }

        [Test]
        public void RegisterChannel_AffectsAlreadyPlayingVoice()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Voice, loop: true));

            AudioManager.RegisterChannel(AudioChannels.Voice, new AudioChannelConfig { Volume = 0.2f });

            Assert.AreEqual(0.2f, AudioTestKit.VolumeOf(handle), 0.001f);
        }

        [Test]
        public void RegisterChannel_OverExistingChannel_Overwrites()
        {
            AudioManager.SetChannelVolume(AudioChannels.Voice, 0.9f);

            AudioManager.RegisterChannel(AudioChannels.Voice, new AudioChannelConfig { Volume = 0.1f });

            Assert.AreEqual(0.1f, AudioManager.GetChannelVolume(AudioChannels.Voice), 0.001f);
        }

        /// <summary>配置在调用时读取一次并快照，之后再改那个实例不影响已注册的通道。</summary>
        [Test]
        public void RegisterChannel_SnapshotsTheConfig()
        {
            var config = new AudioChannelConfig { Volume = 0.5f };
            AudioManager.RegisterChannel(AudioChannels.Voice, config);

            config.Volume = 0.9f;
            config.Muted = true;

            Assert.AreEqual(0.5f, AudioManager.GetChannelVolume(AudioChannels.Voice), 0.001f);
            Assert.IsFalse(AudioManager.IsChannelMuted(AudioChannels.Voice));
        }

        [Test]
        public void RegisterChannel_NullConfig_Throws()
        {
            var ex = Assert.Throws<ArgumentNullException>(
                () => AudioManager.RegisterChannel(AudioChannels.Voice, null));

            StringAssert.Contains("[Audio]", ex.Message);
        }

        [Test]
        public void RegisterChannel_ClampsVolume()
        {
            AudioManager.RegisterChannel(AudioChannels.Voice, new AudioChannelConfig { Volume = 5f });

            Assert.AreEqual(1f, AudioManager.GetChannelVolume(AudioChannels.Voice), 0.001f);
        }

        #endregion
    }
}
