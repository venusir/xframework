using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="JsonFileStore"/> 的文件读写与失败语义测试。
    /// <para>使用真实文件系统：原子写入与损坏回退都依赖物理文件，内存替身覆盖不到。
    /// 每个用例在 <c>Path.GetTempPath()/XFrameworkSettingsTests/{guid}</c> 下独立建目录，
    /// <see cref="TearDown"/> 递归删除。</para>
    /// </summary>
    [TestFixture]
    public class JsonFileStoreTests
    {
        #region Test Doubles

        /// <summary>被持久化的设置类型。JsonUtility 要求 <see cref="SerializableAttribute"/>。</summary>
        [Serializable]
        private sealed class PersistedSettings
        {
            public int Volume = 1;
            public string Name = "default";
        }

        #endregion

        #region Fixture

        private string _dir;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "XFrameworkSettingsTests", Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_dir, "settings.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        #endregion

        #region 构造

        [Test]
        public void Constructor_EmptyPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => new JsonFileStore(null));
            Assert.Throws<ArgumentException>(() => new JsonFileStore(string.Empty));
            Assert.Throws<ArgumentException>(() => new JsonFileStore("   "));
        }

        #endregion

        #region 读取

        [Test]
        public void Load_MissingFile_ReturnsDefault()
        {
            var loaded = new JsonFileStore(_path).Load<PersistedSettings>();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Volume, "文件不存在时返回 new T()");
        }

        [Test]
        public void Load_EmptyFile_ReturnsDefault()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, string.Empty);

            var loaded = new JsonFileStore(_path).Load<PersistedSettings>();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Volume, "空文件返回 new T()");
        }

        [Test]
        public void Load_CorruptJson_WarnsAndReturnsDefault()
        {
            Directory.CreateDirectory(_dir);
            // 顶层数组:仓库文档已载明 JsonUtility 不能直接解析顶层数组,故这里必定抛而非静默返回默认对象
            File.WriteAllText(_path, "[1, 2, 3]");

            LogAssert.Expect(LogType.Warning, new Regex(@"设置文件内容无法解析为 PersistedSettings"));
            var loaded = new JsonFileStore(_path).Load<PersistedSettings>();

            Assert.IsNotNull(loaded, "损坏文件必须回退默认值——向上抛会让此后每次启动都崩且玩家无法自救");
            Assert.AreEqual(1, loaded.Volume);
        }

        [Test]
        public void Load_FileLocked_WarnsAndReturnsDefault()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, "{ \"Volume\": 7 }");

            // FileShare.None 独占打开,使随后的 File.ReadAllText 抛 IOException,覆盖读失败分支
            using (var held = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                LogAssert.Expect(LogType.Warning, new Regex(@"读取设置文件失败"));
                var loaded = new JsonFileStore(_path).Load<PersistedSettings>();

                Assert.IsNotNull(loaded, "读失败同样回退默认值,不把 IOException 抛给游戏主循环");
                Assert.AreEqual(1, loaded.Volume);
            }
        }

        #endregion

        #region 写入

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            var store = new JsonFileStore(_path);

            store.Save(new PersistedSettings { Volume = 42, Name = "custom" });
            var loaded = store.Load<PersistedSettings>();

            Assert.AreEqual(42, loaded.Volume);
            Assert.AreEqual("custom", loaded.Name);
        }

        [Test]
        public void Save_CreatesMissingDirectory()
        {
            var store = new JsonFileStore(_path); // _dir 此刻尚不存在
            Assert.IsFalse(Directory.Exists(_dir));

            store.Save(new PersistedSettings());

            Assert.IsTrue(store.Exists(), "Save 应自动创建缺失的目录");
        }

        [Test]
        public void Save_Null_Throws()
        {
            var store = new JsonFileStore(_path);

            Assert.Throws<ArgumentNullException>(() => store.Save<PersistedSettings>(null));
        }

        #endregion

        #region 存在性与删除

        [Test]
        public void Exists_ReflectsFilePresence()
        {
            var store = new JsonFileStore(_path);

            Assert.IsFalse(store.Exists());

            store.Save(new PersistedSettings());

            Assert.IsTrue(store.Exists());
        }

        [Test]
        public void Delete_RemovesFile()
        {
            var store = new JsonFileStore(_path);
            store.Save(new PersistedSettings());

            store.Delete();

            Assert.IsFalse(store.Exists());
        }

        [Test]
        public void Delete_WhenMissing_DoesNotThrow()
        {
            var store = new JsonFileStore(_path);

            Assert.DoesNotThrow(() => store.Delete());
        }

        #endregion
    }
}
