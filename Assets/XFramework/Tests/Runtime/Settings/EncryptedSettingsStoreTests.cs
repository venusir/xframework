using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
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

        /// <summary>记录 Encrypt 所在线程的提供者（R3-SET-001 的调用线程断言）。</summary>
        private sealed class ThreadRecordingCryptoProvider : ICryptoProvider
        {
            public int EncryptThreadId { get; private set; }

            public byte[] Encrypt(byte[] plainData)
            {
                EncryptThreadId = Thread.CurrentThread.ManagedThreadId;
                return plainData;
            }

            public byte[] Decrypt(byte[] cipherData) => cipherData;
        }

        /// <summary>「文件存在但读出来不是密文外壳」的假存储（R3-SET-006）。</summary>
        private sealed class ExistsButEmptyStore : ISettingsStore
        {
            public bool Exists() => true;

            public T Load<T>() where T : class, new() => new T();

            public void Save<T>(T settings) where T : class, new() { }

            public void Delete() { }
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

        /// <summary>
        /// 同步内层 + 异步写：序列化/加密必须发生在调用线程（R3-SET-001），
        /// 否则池线程会 JsonUtility.ToJson 活对象，调用方继续修改即竞态。
        /// </summary>
        [Test]
        public async Task SaveAsync_SyncInner_EncryptsOnCallingThread()
        {
            var crypto = new ThreadRecordingCryptoProvider();
            var store = new EncryptedSettingsStore(new MemoryStore(), crypto);

            int callerThread = Thread.CurrentThread.ManagedThreadId;
            await store.SaveAsync(new SampleSettings { MasterVolume = 0.5f });

            Assert.AreEqual(callerThread, crypto.EncryptThreadId,
                "加密（连同序列化）必须在调用线程完成");
        }

        [Test]
        public void Load_FileExistsButNotCipherEnvelope_Warns()
        {
            var store = new EncryptedSettingsStore(new ExistsButEmptyStore(), new XorCryptoProvider());

            LogAssert.Expect(LogType.Warning, new Regex("不是密文外壳"));

            var loaded = store.Load<SampleSettings>();

            Assert.IsNotNull(loaded, "仍按既有语义回退默认值");
        }

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

        #region 可选异步能力（SET-001）

        /// <summary>异步能力可观测的假存储：记录走的是异步还是同步成员。</summary>
        private sealed class AsyncMemoryStore : IAsyncSettingsStore
        {
            public object Data;
            public int ExistsAsyncCalls;
            public int LoadAsyncCalls;
            public int SaveAsyncCalls;
            public int SyncCalls;

            public bool Exists()
            {
                SyncCalls++;
                return Data != null;
            }

            public T Load<T>() where T : class, new()
            {
                SyncCalls++;
                return Data as T ?? new T();
            }

            public void Save<T>(T settings) where T : class, new()
            {
                SyncCalls++;
                Data = settings;
            }

            public void Delete() => Data = null;

            public UniTask<bool> ExistsAsync(CancellationToken cancellationToken = default)
            {
                ExistsAsyncCalls++;
                return UniTask.FromResult(Data != null);
            }

            public UniTask<T> LoadAsync<T>(CancellationToken cancellationToken = default) where T : class, new()
            {
                LoadAsyncCalls++;
                return UniTask.FromResult(Data as T ?? new T());
            }

            public UniTask SaveAsync<T>(T settings, CancellationToken cancellationToken = default) where T : class, new()
            {
                SaveAsyncCalls++;
                Data = settings;
                return UniTask.CompletedTask;
            }
        }

        /// <summary>
        /// 装饰器必须显式实现底层的可选异步能力（SET-001）：包住平台异步后端后，管理器的能力探测
        /// 应命中装饰器并走异步成员，而不是降级为线程池调同步成员。
        /// <para>改前：装饰器只实现 <see cref="ISettingsStore"/>，探测不到 <see cref="IAsyncSettingsStore"/>，
        /// 平台侧数据永不参与读写且无告警（违反 CLAUDE.md 的装饰器不变量）。</para>
        /// </summary>
        [Test]
        public async Task AsyncInnerStore_AsyncCapabilityIsForwarded()
        {
            var inner = new AsyncMemoryStore();
            var store = new EncryptedSettingsStore(inner, new XorCryptoProvider("unit-test-key"));

            Assert.IsTrue(store is IAsyncSettingsStore, "装饰器必须显式实现底层的可选能力接口");

            await store.SaveAsync(new SampleSettings { MasterVolume = 0.75f, PlayerName = "async" });
            Assert.AreEqual(1, inner.SaveAsyncCalls, "写应走内层的异步成员");
            Assert.AreEqual(0, inner.SyncCalls, "不应降级为线程池同步调用");

            Assert.IsTrue(await store.ExistsAsync());
            Assert.AreEqual(1, inner.ExistsAsyncCalls);

            var loaded = await store.LoadAsync<SampleSettings>();
            Assert.AreEqual(1, inner.LoadAsyncCalls);
            Assert.AreEqual(0.75f, loaded.MasterVolume, 1e-5f, "异步路径同样要解密");
            Assert.AreEqual("async", loaded.PlayerName);
            Assert.AreEqual(0, inner.SyncCalls, "全程不应触碰同步成员");
        }

        #endregion
    }
}
