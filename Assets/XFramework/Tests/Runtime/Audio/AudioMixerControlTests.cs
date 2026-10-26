using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// provider 中立的 bank / bus / snapshot 控制面：转发、探测语义与异常隔离。
    /// </summary>
    [TestFixture]
    public class AudioMixerControlTests
    {
        #region Setup

        private FakeCapabilityProvider _fake;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _fake = new FakeCapabilityProvider();
            AudioManager.SetProvider(_fake, new AudioInitOptions { MaxVoices = 4 });
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();
        }

        #endregion

        #region Bank

        [Test]
        public void LoadBank_ForwardsAndTracksState()
        {
            Assert.IsTrue(AudioManager.LoadBank("Master", loadSamples: true));
            Assert.IsTrue(AudioManager.IsBankLoaded("Master"));

            Assert.AreEqual(1, _fake.BankLoads.Count);
            Assert.AreEqual("Master", _fake.BankLoads[0].Bank);
            Assert.IsTrue(_fake.BankLoads[0].LoadSamples);
        }

        [Test]
        public void UnloadBank_ForwardsAndTracksState()
        {
            AudioManager.LoadBank("Master");

            Assert.IsTrue(AudioManager.UnloadBank("Master"));
            Assert.IsFalse(AudioManager.IsBankLoaded("Master"));
            Assert.AreEqual(1, _fake.BankUnloads.Count);
        }

        [Test]
        public void LoadBank_RejectedByProvider_ReturnsFalse()
        {
            _fake.RejectBankLoad = true;

            Assert.IsFalse(AudioManager.LoadBank("Master"));
        }

        [Test]
        public void LoadBank_ProviderThrows_IsIsolated()
        {
            _fake.ThrowOnBankLoad = true;

            LogAssert.Expect(LogType.Error, new Regex("加载音频 bank 时 provider 抛出异常"));

            Assert.IsFalse(AudioManager.LoadBank("Master"));
        }

        #endregion

        #region Bus

        [Test]
        public void BusControls_ForwardWithoutClamping()
        {
            Assert.IsTrue(AudioManager.SetBusVolume("bus:/SFX", 1.5f));
            Assert.IsTrue(AudioManager.SetBusMuted("bus:/SFX", true));
            Assert.IsTrue(AudioManager.SetBusPaused("bus:/SFX", true));

            Assert.AreEqual(1.5f, _fake.BusVolumes[0].Volume, 0.001f,
                "核心必须原样透传 bus 音量，不做钳制");
            Assert.IsTrue(_fake.BusMutes[0].Muted);
            Assert.IsTrue(_fake.BusPauses[0].Paused);
        }

        [Test]
        public void SetBusVolume_RejectedByProvider_ReturnsFalse()
        {
            _fake.RejectBusVolume = true;

            Assert.IsFalse(AudioManager.SetBusVolume("bus:/SFX", 0.5f));
        }

        [Test]
        public void SetBusVolume_ProviderThrows_IsIsolated()
        {
            _fake.ThrowOnBusVolume = true;

            LogAssert.Expect(LogType.Error, new Regex("设置音频 bus 音量时 provider 抛出异常"));

            Assert.IsFalse(AudioManager.SetBusVolume("bus:/SFX", 0.5f));
        }

        #endregion

        #region Snapshot

        [Test]
        public void SnapshotControls_Forward()
        {
            Assert.IsTrue(AudioManager.StartSnapshot("snapshot:/Pause"));
            Assert.IsTrue(AudioManager.StopSnapshot("snapshot:/Pause"));

            Assert.AreEqual(1, _fake.StartedSnapshots.Count);
            Assert.AreEqual("snapshot:/Pause", _fake.StartedSnapshots[0]);
            Assert.AreEqual(1, _fake.StoppedSnapshots.Count);
            Assert.AreEqual("snapshot:/Pause", _fake.StoppedSnapshots[0]);
        }

        [Test]
        public void StartSnapshot_RejectedByProvider_ReturnsFalse()
        {
            _fake.RejectSnapshotStart = true;

            Assert.IsFalse(AudioManager.StartSnapshot("snapshot:/Pause"));
        }

        [Test]
        public void StartSnapshot_ProviderThrows_IsIsolated()
        {
            _fake.ThrowOnSnapshotStart = true;

            LogAssert.Expect(LogType.Error, new Regex("启动音频 snapshot 时 provider 抛出异常"));

            Assert.IsFalse(AudioManager.StartSnapshot("snapshot:/Pause"));
        }

        #endregion

        #region 参数校验与未实现能力

        [Test]
        public void BlankNames_Throw()
        {
            Assert.Throws<System.ArgumentException>(() => AudioManager.LoadBank(null));
            Assert.Throws<System.ArgumentException>(() => AudioManager.UnloadBank(" "));
            Assert.Throws<System.ArgumentException>(() => AudioManager.IsBankLoaded(null));
            Assert.Throws<System.ArgumentException>(() => AudioManager.SetBusVolume(" ", 1f));
            Assert.Throws<System.ArgumentException>(() => AudioManager.SetBusMuted(null, true));
            Assert.Throws<System.ArgumentException>(() => AudioManager.SetBusPaused(" ", true));
            Assert.Throws<System.ArgumentException>(() => AudioManager.StartSnapshot(null));
            Assert.Throws<System.ArgumentException>(() => AudioManager.StopSnapshot(" "));
        }

        [Test]
        public void MixerMethods_WhenProviderDoesNotImplement_ReturnFalse()
        {
            AudioManager.SetProvider(new FakeAudioProvider(), new AudioInitOptions { MaxVoices = 4 });

            Assert.IsFalse(AudioManager.LoadBank("Master"));
            Assert.IsFalse(AudioManager.UnloadBank("Master"));
            Assert.IsFalse(AudioManager.IsBankLoaded("Master"));
            Assert.IsFalse(AudioManager.SetBusVolume("bus:/SFX", 0.5f));
            Assert.IsFalse(AudioManager.SetBusMuted("bus:/SFX", true));
            Assert.IsFalse(AudioManager.SetBusPaused("bus:/SFX", true));
            Assert.IsFalse(AudioManager.StartSnapshot("snapshot:/Pause"));
            Assert.IsFalse(AudioManager.StopSnapshot("snapshot:/Pause"));
        }

        #endregion
    }
}
