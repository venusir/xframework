using NUnit.Framework;
using UnityEngine;
using XFramework.XAudio;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// <see cref="FmodAudioSettings"/> 的默认值、归一化与快照构造。资产只提供配置形状；
    /// 自动安装消费逻辑由 <c>FmodInstallerTests</c> 覆盖。
    /// </summary>
    [TestFixture]
    public class FmodSettingsTests
    {
        private FmodAudioSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<FmodAudioSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
                Object.DestroyImmediate(_settings);
        }

        [Test]
        public void Defaults_AreExpected()
        {
            Assert.IsTrue(_settings.Enabled);
            Assert.AreEqual("event:/", _settings.EventPathPrefix);
            Assert.IsEmpty(_settings.Banks);
            Assert.IsFalse(_settings.LoadBankSamples);
            Assert.IsFalse(_settings.OwnsBanks);
            Assert.AreEqual(32, _settings.MaxVoices);
            Assert.AreEqual(1f, _settings.MasterVolume, 0.001f);
            Assert.IsFalse(_settings.MasterMuted);
        }

        [Test]
        public void Normalization_ClampsAndDefaults()
        {
            _settings.EventPathPrefix = null;
            Assert.AreEqual("event:/", _settings.EventPathPrefix);

            _settings.EventPathPrefix = string.Empty;
            Assert.AreEqual(string.Empty, _settings.EventPathPrefix);

            _settings.Banks = null;
            Assert.IsEmpty(_settings.Banks);

            _settings.MaxVoices = 0;
            Assert.AreEqual(1, _settings.MaxVoices);

            _settings.MasterVolume = 2f;
            Assert.AreEqual(1f, _settings.MasterVolume, 0.001f);

            _settings.MasterVolume = -1f;
            Assert.AreEqual(0f, _settings.MasterVolume, 0.001f);
        }

        [Test]
        public void CreateProviderOptions_SnapshotsPrefixAndCapacity()
        {
            _settings.EventPathPrefix = "custom:/";
            _settings.MaxVoices = 8;

            var options = _settings.CreateProviderOptions();

            Assert.AreEqual("custom:/", options.EventPathPrefix);
            Assert.AreEqual(8, options.MaxVoices);
        }

        [Test]
        public void CreateAudioInitOptions_SnapshotsCoreOptions()
        {
            _settings.MaxVoices = 12;
            _settings.MasterVolume = 0.5f;
            _settings.MasterMuted = true;

            var options = _settings.CreateAudioInitOptions();

            Assert.AreEqual(12, options.MaxVoices);
            Assert.AreEqual(0.5f, options.MasterVolume, 0.001f);
            Assert.IsTrue(options.MasterMuted);
            Assert.IsFalse(options.PrewarmVoices, "FMOD 不使用内置 provider 的预热");
        }

        [Test]
        public void BanksAndOwnership_ArePreserved()
        {
            _settings.Banks = new[] { "Master", "Master.strings" };
            _settings.LoadBankSamples = true;
            _settings.OwnsBanks = true;

            Assert.AreEqual(2, _settings.Banks.Length);
            Assert.AreEqual("Master.strings", _settings.Banks[1]);
            Assert.IsTrue(_settings.LoadBankSamples);
            Assert.IsTrue(_settings.OwnsBanks);
        }
    }
}
