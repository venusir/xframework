using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 端到端桥接：<see cref="AudioManager"/> 的公开控制面 → 核心能力探测 → <see cref="FmodAudioProvider"/>
    /// → fake backend。证明 internal 能力接口确实把 FMOD provider 接进了 AudioManager。
    /// </summary>
    [TestFixture]
    public class FmodAudioManagerBridgeTests
    {
        private FakeFmodEventBackend _backend;
        private FmodAudioProvider _provider;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _backend = new FakeFmodEventBackend();
            _provider = new FmodAudioProvider(_backend, new FmodProviderOptions("event:/", 4));
            AudioManager.SetProvider(_provider, new AudioInitOptions { MaxVoices = 4 });
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();
        }

        [Test]
        public async Task AudioManagerControlSurface_ReachesFmodBackend()
        {
            var handle = await AudioManager.PlayAsync("event:/SFX/Footstep",
                new AudioPlayOptions(
                    initialParameters: new[] { new AudioParameter("Health", 0.5f) }));

            Assert.IsFalse(handle.IsDefault);
            Assert.AreEqual("event:/SFX/Footstep", _backend.LastEventPath);
            Assert.AreEqual(1, _backend.Parameters.Count);
            Assert.AreEqual("Health", _backend.Parameters[0].Name);
            Assert.AreEqual(1, _backend.StartCalls);

            Assert.IsTrue(AudioManager.SetParameter(handle, "Surface", "Grass"));
            Assert.IsTrue(AudioManager.SetGlobalParameter("Weather", 0.5f));
            Assert.IsTrue(AudioManager.SetPosition(handle, new Vector3(1f, 2f, 3f)));
            Assert.IsTrue(AudioManager.LoadBank("Master", true));
            Assert.IsTrue(AudioManager.IsBankLoaded("Master"));
            Assert.IsTrue(AudioManager.SetBusVolume("bus:/SFX", 0.5f));
            Assert.IsTrue(AudioManager.SetBusMuted("bus:/SFX", true));
            Assert.IsTrue(AudioManager.SetBusPaused("bus:/SFX", true));
            Assert.IsTrue(AudioManager.StartSnapshot("snapshot:/Pause"));
            Assert.IsTrue(AudioManager.StopSnapshot("snapshot:/Pause"));

            Assert.AreEqual(1, _backend.GlobalParameters.Count);
            Assert.AreEqual(1, _backend.Set3DAttributesCalls);
            Assert.AreEqual(1, _backend.BankLoads.Count);
            Assert.AreEqual(1, _backend.BusVolumes.Count);
            Assert.AreEqual(1, _backend.BusMutes.Count);
            Assert.AreEqual(1, _backend.BusPauses.Count);
            Assert.AreEqual(1, _backend.StartedSnapshots.Count);
            Assert.AreEqual(1, _backend.StoppedSnapshots.Count);
        }

        [Test]
        public async Task AudioManagerStop_ReleasesBackendInstance()
        {
            var handle = await AudioManager.PlayAsync("event:/a");

            Assert.IsTrue(AudioManager.Stop(handle));
            Assert.AreEqual(1, _backend.ReleasedIds.Count);
        }

        [Test]
        public void AudioManagerUnloadBank_ReachesBackend()
        {
            Assert.IsTrue(AudioManager.LoadBank("Master"));
            Assert.IsTrue(AudioManager.IsBankLoaded("Master"));

            Assert.IsTrue(AudioManager.UnloadBank("Master"));
            Assert.IsFalse(AudioManager.IsBankLoaded("Master"));
            Assert.AreEqual(1, _backend.BankUnloads.Count);
            Assert.AreEqual("Master", _backend.BankUnloads[0]);
        }

        [Test]
        public async Task AudioManagerDestroy_DisposesProviderAndBackend()
        {
            await AudioManager.PlayAsync("event:/a");

            AudioManager.Destroy();

            Assert.IsTrue(_backend.Disposed);
        }
    }
}
