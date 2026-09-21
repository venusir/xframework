using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XFileManager;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="EncryptedSettingsStore"/> 加解密装饰器测试。
    /// <para>覆盖：载荷确实被加密后才交给内层、往返一致、内层能力（存在性 / 删除）透传、
    /// 解密与解析失败一律回退默认值，以及与真实 <see cref="JsonFileStore"/> 的端到端落盘。</para>
    /// </summary>
    [TestFixture]
    public class EncryptedSettingsStoreTests
    {
        #region Test Doubles

        [Serializable]
        private sealed class SampleSettings
        {
            public float MasterVolume = 1f;
            public string PlayerName = "anon";
        }

        /// <summary>内存假存储：保留最后一次写入的对象，以便断言「内层看到的到底是什么」。</summary>
        private sealed class MemoryStore : ISettingsStore
        {
            public object Data;

            public bool Exists() => Data != null;

            public T Load<T>() where T : class, new() => Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new() => Data = settings;

            public void Delete() => Data = null;
        }

        /// <summary>解密恒失败（返回 <c>null</c>）的提供者，用于验证失败回退。</summary>
        private sealed class FailingCryptoProvider : ICryptoProvider
        {
            public byte[] Encrypt(byte[] plainData) => plainData;

            public byte[] Decrypt(byte[] cipherData) => null;
        }

        #endregion

        #region Fixture

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            SettingsManager.Destroy();
            _directory = Path.Combine(Path.GetTempPath(), "XFrameworkSettingsTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            SettingsManager.Destroy();

            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private string FilePath => Path.Combine(_directory, "settings.json");

        #endregion

        #region 构造

        [Test]
        public void Constructor_NullArguments_Throw()
        {
            var store = new MemoryStore();
            var crypto = new XorCryptoProvider();

            Assert.Throws<ArgumentNullException>(() => new EncryptedSettingsStore(null, crypto));
            Assert.Throws<ArgumentNullException>(() => new EncryptedSettingsStore(store, null));
        }

        #endregion

        #region 加解密

        [Test]
        public void Save_EncryptsBeforeHandingToInner()
        {
            var inner = new MemoryStore();
            var store = new EncryptedSettingsStore(inner, new XorCryptoProvider("unit-test-key"));

            store.Save(new SampleSettings { MasterVolume = 0.5f, PlayerName = "secret-name" });

            var raw = JsonUtility.ToJson(inner.Data);
            StringAssert.DoesNotContain("MasterVolume", raw,
                "交给内层的必须是密文——明文一旦出现在内层，加密就没有发生");
            StringAssert.DoesNotContain("secret-name", raw);
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            var store = new EncryptedSettingsStore(new MemoryStore(), new XorCryptoProvider("unit-test-key"));

            store.Save(new SampleSettings { MasterVolume = 0.25f, PlayerName = "alice" });
            var loaded = store.Load<SampleSettings>();

            Assert.AreEqual(0.25f, loaded.MasterVolume, 1e-5f);
            Assert.AreEqual("alice", loaded.PlayerName);
        }

        [Test]
        public void Save_Null_Throws()
        {
            var store = new EncryptedSettingsStore(new MemoryStore(), new XorCryptoProvider());

            Assert.Throws<ArgumentNullException>(() => store.Save<SampleSettings>(null));
        }

        #endregion

        #region 内层能力透传

        [Test]
        public void ExistsAndDelete_DelegateToInner()
        {
            var inner = new MemoryStore();
            var store = new EncryptedSettingsStore(inner, new XorCryptoProvider());

            Assert.IsFalse(store.Exists());

            store.Save(new SampleSettings());
            Assert.IsTrue(store.Exists());

            store.Delete();
            Assert.IsFalse(store.Exists(), "删除由内层决定：密文与明文是同一份存储");
        }

        #endregion

        #region 失败回退

        [Test]
        public void Load_NoInnerData_ReturnsDefaultInstance()
        {
            var store = new EncryptedSettingsStore(new MemoryStore(), new XorCryptoProvider());

            var loaded = store.Load<SampleSettings>();

            Assert.IsNotNull(loaded, "无数据时按 ISettingsStore 契约返回 new T()");
            Assert.AreEqual(1f, loaded.MasterVolume, 1e-5f, "字段初始化器的值");
        }

        [Test]
        public void Load_DecryptFails_WarnsAndFallsBackToDefault()
        {
            var inner = new MemoryStore();
            var writer = new EncryptedSettingsStore(inner, new XorCryptoProvider("unit-test-key"));
            writer.Save(new SampleSettings { MasterVolume = 0.9f });

            // 同一份密文、换一个解密恒失败的提供者——模拟「密钥已更换」
            var reader = new EncryptedSettingsStore(inner, new FailingCryptoProvider());
            LogAssert.Expect(LogType.Warning, new Regex("设置密文解密失败"));

            var loaded = reader.Load<SampleSettings>();

            Assert.AreEqual(1f, loaded.MasterVolume, 1e-5f, "解密失败回退默认值，不抛异常");
        }

        [Test]
        public void Load_CorruptBase64_WarnsAndFallsBackToDefault()
        {
            var inner = new MemoryStore { Data = new SettingsCipherBlob { Data = "这不是 Base64!!" } };
            var store = new EncryptedSettingsStore(inner, new XorCryptoProvider());

            LogAssert.Expect(LogType.Warning, new Regex("不是合法的 Base64"));

            var loaded = store.Load<SampleSettings>();

            Assert.AreEqual(1f, loaded.MasterVolume, 1e-5f);
        }

        #endregion

        #region 端到端

        [Test]
        public void EndToEnd_RealJsonFileStore_Versioned_RoundTripsAndHidesPlaintext()
        {
            // 同时启用版本化：信封在加密之前由管理器套上，故加解密对版本化是透明的。
            // 本用例还顺带验证 SettingsCipherBlob 能被 JsonUtility 正确序列化（它是 internal 类型）
            SettingsManager.Initialize<SampleSettings>(
                new EncryptedSettingsStore(new JsonFileStore(FilePath), new XorCryptoProvider("unit-test-key")),
                () => new SampleSettings { MasterVolume = 0.1f },
                new SettingsOptions { CurrentVersion = 1 });

            SettingsManager.Settings<SampleSettings>().MasterVolume = 0.75f;
            SettingsManager.Settings<SampleSettings>().PlayerName = "carol";
            SettingsManager.Save<SampleSettings>();

            var raw = File.ReadAllText(FilePath);
            StringAssert.DoesNotContain("MasterVolume", raw, "落盘文件是密文");
            StringAssert.DoesNotContain("carol", raw);
            StringAssert.Contains("Data", raw, "外层是只有一个 Data 字段的密文外壳");

            // 换一套全新的管理器与存储实例重读同一路径
            SettingsManager.Destroy();
            SettingsManager.Initialize<SampleSettings>(
                new EncryptedSettingsStore(new JsonFileStore(FilePath), new XorCryptoProvider("unit-test-key")),
                () => new SampleSettings { MasterVolume = 0.1f },
                new SettingsOptions { CurrentVersion = 1 });

            Assert.AreEqual(0.75f, SettingsManager.Settings<SampleSettings>().MasterVolume, 1e-5f,
                "加密对版本化透明：信封由管理器在加密前套上，读取时先解密再拆信封");
            Assert.AreEqual("carol", SettingsManager.Settings<SampleSettings>().PlayerName);
        }

        #endregion
    }
}
