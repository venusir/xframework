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

        #region 原子写入与一代备份

        // 以下用例的取值刻意与 PersistedSettings 的字段初始值（Volume=1 / Name="default"）
        // 区分开，否则「没从备份恢复」与「从备份恢复」会得到相同断言结果

        [Test]
        public void Save_LeavesNoTempFile()
        {
            var store = new JsonFileStore(_path);

            store.Save(new PersistedSettings { Volume = 7, Name = "first" });

            Assert.IsFalse(File.Exists(_path + FilePathUtility.TempFileSuffix),
                "写入完成后不应残留 .tmp");
        }

        [Test]
        public void Save_FirstWrite_CreatesNoBackup()
        {
            var store = new JsonFileStore(_path);

            store.Save(new PersistedSettings { Volume = 7, Name = "first" });

            Assert.IsFalse(File.Exists(_path + FilePathUtility.BackupFileSuffix),
                "首次写入没有旧版本可备份");
        }

        [Test]
        public void Save_SecondWrite_KeepsPreviousContentAsBackup()
        {
            var store = new JsonFileStore(_path);
            store.Save(new PersistedSettings { Volume = 7, Name = "first" });

            store.Save(new PersistedSettings { Volume = 2, Name = "second" });

            var backupPath = _path + FilePathUtility.BackupFileSuffix;
            Assert.IsTrue(File.Exists(backupPath), "第二次写入应保留一代备份");
            StringAssert.Contains("first", File.ReadAllText(backupPath), "备份里是上一版数据");
            Assert.AreEqual(2, store.Load<PersistedSettings>().Volume, "正式文件是最新数据");
        }

        [Test]
        public void Load_CorruptMainFile_RecoversFromBackup()
        {
            var store = new JsonFileStore(_path);
            store.Save(new PersistedSettings { Volume = 7, Name = "first" });
            store.Save(new PersistedSettings { Volume = 2, Name = "second" }); // .bak 中留下 first

            File.WriteAllText(_path, "[1, 2, 3]"); // 损坏正式文件

            LogAssert.Expect(LogType.Warning, new Regex(@"设置文件内容无法解析为 PersistedSettings"));
            LogAssert.Expect(LogType.Warning, new Regex(@"主设置文件不可用，已从备份恢复"));
            var loaded = store.Load<PersistedSettings>();

            Assert.AreEqual(7, loaded.Volume, "从备份恢复出的是上一版数据，而非字段初始值");
            Assert.AreEqual("first", loaded.Name);
        }

        [Test]
        public void Load_MissingMainFile_DoesNotRecoverFromBackup()
        {
            // 锁定 Reset 语义：备份存在不等于「有持久化数据」。若缺失也回退备份，
            // 玩家重置后旧设置会从 .bak 里复活
            var store = new JsonFileStore(_path);
            store.Save(new PersistedSettings { Volume = 7, Name = "first" });
            store.Save(new PersistedSettings { Volume = 2, Name = "second" });
            Assert.IsTrue(File.Exists(_path + FilePathUtility.BackupFileSuffix));

            File.Delete(_path); // 只删正式文件，故意留着备份

            var loaded = store.Load<PersistedSettings>();

            Assert.AreEqual(1, loaded.Volume, "主文件缺失时应回到字段初始值，不得从备份恢复");
            Assert.AreEqual("default", loaded.Name);
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
        public void Delete_RemovesBackupToo()
        {
            var store = new JsonFileStore(_path);
            store.Save(new PersistedSettings { Volume = 7 });
            store.Save(new PersistedSettings { Volume = 2 });
            var backupPath = _path + FilePathUtility.BackupFileSuffix;
            Assert.IsTrue(File.Exists(backupPath));

            store.Delete();

            Assert.IsFalse(store.Exists());
            Assert.IsFalse(File.Exists(backupPath), "重置不得留下可被后续 Load 恢复的备份");
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
