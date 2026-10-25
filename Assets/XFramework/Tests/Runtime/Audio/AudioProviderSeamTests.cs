using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// <see cref="IAudioProvider"/> 播放引擎缝：核心语义层与自定义 provider 的接线、音量/暂停/回收、
    /// 失败与异常隔离、provider 句柄映射。
    /// </summary>
    [TestFixture]
    public class AudioProviderSeamTests
    {
        #region Setup

        private FakeAudioProvider _fake;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _fake = new FakeAudioProvider();
            AudioManager.SetProvider(_fake, new AudioInitOptions { MaxVoices = 4 });
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();
        }

        #endregion

        #region 注入与请求

        [Test]
        public void SetProvider_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AudioManager.SetProvider(null));
        }

        [Test]
        public void SetProvider_DisposesThePreviousInstance()
        {
            var next = new FakeAudioProvider();

            AudioManager.SetProvider(next);

            Assert.IsTrue(_fake.Disposed, "SetProvider 必须先 Dispose 旧实例（provider 由框架持有）");
            Assert.IsFalse(next.Disposed);
        }

        [Test]
        public void Play_ForwardsLocationAndRequest()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            Assert.IsFalse(handle.IsDefault);
            Assert.AreEqual(1, _fake.Plays.Count);
            Assert.AreEqual("audio/a", _fake.LastLocation);
            Assert.IsTrue(_fake.LastRequest.Loop);
            Assert.AreEqual(1, AudioManager.GetActiveVoiceCount(AudioChannels.Bgm));
            Assert.IsTrue(AudioManager.IsPlaying(handle));
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void Play_NormalizesRequestBeforeCallingProvider()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(
                pitch: 0f,
                spatialBlend: 3f,
                position: new Vector3(1f, 2f, 3f)));

            var request = _fake.LastRequest;

            Assert.AreEqual(1f, request.Pitch, 0.001f, "非正音高必须归一化为 1");
            Assert.AreEqual(1f, request.SpatialBlend, 0.001f, "空间混合必须钳到 [0,1]");
            Assert.AreEqual(new Vector3(1f, 2f, 3f), request.Position);
            Assert.IsTrue(request.ValidityToken.CanBeCanceled);
            Assert.IsFalse(request.ValidityToken.IsCancellationRequested);
        }

        [Test]
        public void Volume_IsComputedByCoreAndPushedToProvider()
        {
            AudioManager.MasterVolume = 0.5f;
            AudioManager.SetChannelVolume(AudioChannels.Bgm, 0.5f);

            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Bgm, 0.5f, loop: true));

            Assert.AreEqual(0.125f, _fake.LastRequest.Volume, 0.001f, "主 × 通道 × 单次 = 0.125");
            Assert.GreaterOrEqual(_fake.SetVolumeCalls, 1);
            Assert.AreEqual(0.125f, _fake.Find(FindProviderHandle()).Volume, 0.001f);
        }

        [Test]
        public void MasterVolumeChange_PushesToProvider()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.MasterVolume = 0.25f;

            Assert.AreEqual(0.25f, _fake.Find(FindProviderHandle()).Volume, 0.001f);
            Assert.GreaterOrEqual(_fake.SetVolumeCalls, 2);
        }

        [Test]
        public void PauseAndResume_ForwardToProvider()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Pause();
            Assert.IsTrue(AudioManager.IsPlaying(handle), "暂停中的播放仍算在播");
            Assert.IsTrue(_fake.Find(FindProviderHandle()).Paused);

            AudioManager.Resume();
            Assert.IsFalse(_fake.Find(FindProviderHandle()).Paused);
            Assert.IsTrue(AudioManager.IsPlaying(handle));
        }

        #endregion

        #region 回收、停止与失败

        [Test]
        public void NaturalEnd_IsReapedBySweep()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.End(FindProviderHandle());

            ((AudioManagerImpl)AudioManager.CurrentManagerForTests).SweepVoices();

            Assert.IsFalse(AudioManager.IsPlaying(handle));
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(1, _fake.ReleaseCalls);
        }

        [Test]
        public void Stop_ReleasesProviderHandle()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(AudioManager.Stop(handle));

            Assert.AreEqual(1, _fake.StopCalls);
            Assert.AreEqual(1, _fake.ReleaseCalls);
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.IsFalse(AudioManager.TryGetProviderHandle(handle, out _));
        }

        [Test]
        public void ProviderFailure_PlayReturnsInvalidPreReservedHandle_PlayAsyncReturnsDefault()
        {
            _fake.FailNext = true;
            var handle = AudioManager.Play("audio/a");

            Assert.IsFalse(handle.IsDefault, "Play 的契约是预占即返回");
            Assert.IsFalse(AudioManager.IsPlaying(handle));
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public async Task ProviderFailure_PlayAsyncReturnsDefault()
        {
            _fake.FailNext = true;

            var handle = await AudioManager.PlayAsync("audio/a");

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        #endregion

        #region 句柄映射与诊断

        [Test]
        public void TryGetProviderHandle_Probes()
        {
            Assert.IsFalse(AudioManager.TryGetProviderHandle(default, out _), "default 句柄必须返回 false");
            Assert.IsFalse(AudioManager.TryGetProviderHandle(new AudioHandle(99, 1), out _),
                "未知句柄必须返回 false");

            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(AudioManager.TryGetProviderHandle(handle, out AudioProviderHandle providerHandle));
            Assert.IsFalse(providerHandle.IsDefault);
            Assert.IsNotNull(_fake.Find(providerHandle));

            AudioManager.Destroy();
            Assert.IsFalse(AudioManager.TryGetProviderHandle(handle, out _),
                "已销毁的管理器必须安全返回 false");
        }

        [Test]
        public void CopyChannels_WorksWithCustomProvider()
        {
            AudioManager.SetChannelVolume(AudioChannels.Bgm, 0.4f);
            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            var buffer = new List<AudioChannelInfo>();
            int count = AudioManager.CopyChannels(buffer);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AudioChannels.Bgm, buffer[0].Channel);
            Assert.AreEqual(0.4f, buffer[0].Volume, 0.001f);
            Assert.AreEqual(1, buffer[0].ActiveVoiceCount);
        }

        [Test]
        public void ProviderHandle_EqualityAndDefault()
        {
            Assert.IsTrue(default(AudioProviderHandle).IsDefault);
            Assert.IsTrue(new AudioProviderHandle(0, 0).IsDefault);

            var a = new AudioProviderHandle(1, 1);
            var b = new AudioProviderHandle(1, 1);
            var c = new AudioProviderHandle(1, 2);

            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.IsTrue(a != c);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        #endregion

        #region provider 抛异常：核心隔离

        [Test]
        public void ProviderThrowsOnPlay_IsIsolated()
        {
            _fake.ThrowOnPlay = true;
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 播放 'audio/a' 时发生未预期异常")));

            var handle = AudioManager.Play("audio/a");

            Assert.IsFalse(handle.IsDefault, "Play 的预占句柄仍然返回");
            Assert.IsFalse(AudioManager.IsPlaying(handle));
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ProviderThrowsOnPoll_IsIsolatedAndReclaims()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnPoll = true;
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 轮询音频播放状态时发生异常")));

            ((AudioManagerImpl)AudioManager.CurrentManagerForTests).SweepVoices();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ProviderThrowsOnVolume_IsIsolatedAndReclaims()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnSetVolume = true;
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 刷新音频音量时发生异常")));

            AudioManager.MasterVolume = 0.5f;

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ProviderThrowsOnPause_IsIsolatedAndReclaims()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnSetPaused = true;
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 暂停音频播放时发生异常")));

            AudioManager.Pause();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        #endregion

        #region Helpers

        /// <summary>最近一次成功播放的 provider 句柄（FakeAudioProvider 的 Id 从 0 递增、代际恒 1）。</summary>
        private AudioProviderHandle FindProviderHandle()
            => new AudioProviderHandle(_fake.Plays.Count - 1, 1);

        #endregion
    }
}
