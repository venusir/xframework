using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAudio;
using XFramework.XUpdate;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// <see cref="FmodInstaller"/> 的 FMOD-free 自动安装逻辑：前置检查、bank 加载、失败清理与接线。
    /// </summary>
    [TestFixture]
    public class FmodInstallerTests
    {
        private FmodAudioSettings _settings;
        private FakeFmodEventBackend _backend;
        private Func<IFmodEventBackend> _factory;
        private int _factoryCalls;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _settings = ScriptableObject.CreateInstance<FmodAudioSettings>();
            _backend = new FakeFmodEventBackend();
            _factoryCalls = 0;
            _factory = () =>
            {
                _factoryCalls++;
                return _backend;
            };
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();

            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
                _settings = null;
            }
        }

        [Test]
        public void NullSettings_DoesNotInstall()
        {
            Assert.IsFalse(FmodInstaller.TryInstall(null, _factory, true));
            Assert.AreEqual(0, _factoryCalls);
            Assert.IsFalse(AudioManager.IsInitialized);
        }

        [Test]
        public void Disabled_DoesNotInstall()
        {
            _settings.Enabled = false;

            Assert.IsFalse(FmodInstaller.TryInstall(_settings, _factory, true));
            Assert.AreEqual(0, _factoryCalls);
            Assert.IsFalse(AudioManager.IsInitialized);
        }

        [Test]
        public void FmodSettingsMissing_DoesNotInstall()
        {
            LogAssert.Expect(LogType.Error, new Regex("FMODStudioSettings"));

            Assert.IsFalse(FmodInstaller.TryInstall(_settings, _factory, false));
            Assert.AreEqual(0, _factoryCalls);
            Assert.IsFalse(AudioManager.IsInitialized);
        }

        [Test]
        public void AlreadyInitialized_DoesNotOverride()
        {
            var existingBackend = new FakeFmodEventBackend();
            AudioManager.SetProvider(
                new FmodAudioProvider(existingBackend, new FmodProviderOptions("event:/", 4)),
                new AudioInitOptions { MaxVoices = 4 });

            LogAssert.Expect(LogType.Warning, new Regex("已初始化"));

            Assert.IsFalse(FmodInstaller.TryInstall(_settings, _factory, true));
            Assert.AreEqual(0, _factoryCalls);
            Assert.IsTrue(AudioManager.IsInitialized);
        }

        [Test]
        public async Task Success_InstallsProviderAndLoadsBanks()
        {
            _settings.Banks = new[] { "Master", "Master.strings" };
            _settings.LoadBankSamples = true;

            Assert.IsTrue(FmodInstaller.TryInstall(_settings, _factory, true));
            Assert.AreEqual(1, _factoryCalls);
            Assert.IsTrue(AudioManager.IsInitialized);
            Assert.AreEqual(2, _backend.BankLoads.Count);
            Assert.AreEqual("Master", _backend.BankLoads[0].Bank);
            Assert.IsTrue(_backend.BankLoads[0].LoadSamples);
            Assert.AreEqual("Master.strings", _backend.BankLoads[1].Bank);

            var handle = await AudioManager.PlayAsync("event:/probe");
            Assert.IsFalse(handle.IsDefault);
            Assert.AreEqual(1, _backend.CreateCalls);
        }

        [Test]
        public void PreloadedBank_IsNotReloaded()
        {
            _backend.LoadedBanks.Add("Master");
            _settings.Banks = new[] { "Master" };

            Assert.IsTrue(FmodInstaller.TryInstall(_settings, _factory, true));
            Assert.IsEmpty(_backend.BankLoads);
            Assert.IsTrue(AudioManager.IsInitialized);
        }

        [Test]
        public void BankLoadFailure_CleansUpNewlyLoadedBanks()
        {
            _settings.Banks = new[] { "A", "B" };
            _backend.FailBankLoadNames.Add("B");

            LogAssert.Expect(LogType.Error, new Regex("bank 'B'"));

            Assert.IsFalse(FmodInstaller.TryInstall(_settings, _factory, true));
            Assert.AreEqual(1, _backend.BankUnloads.Count);
            Assert.AreEqual("A", _backend.BankUnloads[0]);
            Assert.IsTrue(_backend.Disposed);
            Assert.IsFalse(AudioManager.IsInitialized);
        }

        [Test]
        public void FactoryThrows_DoesNotInstall()
        {
            _factory = () => throw new InvalidOperationException("fake factory boom");

            LogAssert.Expect(LogType.Error, new Regex("自动安装失败"));

            Assert.IsFalse(FmodInstaller.TryInstall(_settings, _factory, true));
            Assert.IsFalse(AudioManager.IsInitialized);
        }

        [Test]
        public void BackendFactoryReturnsNull_DoesNotInstall()
        {
            _factory = () => null;

            LogAssert.Expect(LogType.Error, new Regex("返回 null"));

            Assert.IsFalse(FmodInstaller.TryInstall(_settings, _factory, true));
            Assert.IsFalse(AudioManager.IsInitialized);
        }
    }
}
