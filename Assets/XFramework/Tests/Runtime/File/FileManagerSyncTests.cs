using System;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XSave.Tests;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// <see cref="FileManager"/> 同步内容 API 契约测试。
    /// <para>覆盖：同步入口与异步入口的双向互通、文件不存在时的返回契约、目录枚举的正斜杠规范，
    /// 以及「移动端 Streaming 域同步读」的拒绝判定与门面级抛出。</para>
    /// </summary>
    [TestFixture]
    public class FileManagerSyncTests
    {
        private TempFileProvider _fileProvider;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _fileProvider?.Cleanup();
        }

        [SetUp]
        public void SetUp()
        {
            _fileProvider?.Cleanup();
            _fileProvider = new TempFileProvider();

            FileManager.Destroy();
            FileManager.Initialize(_fileProvider);
        }

        [TearDown]
        public void TearDown()
        {
            FileManager.Destroy();

            // 还原成「零配置可用」，而不是把销毁闩锁留给后续 fixture——懒加载豁免只对「未初始化」生效，
            // 已销毁时 EnsureInitialized 直接抛，同进程混跑会让后面的用例吃到它（同 FileManagerAtomicTests）
            FileManager.Initialize();
        }

        #region 同步与异步入口互通

        [Test]
        public async Task WriteAllText_Sync_IsVisibleToAsyncEntry()
        {
            FileManager.WriteAllText(FileDomain.AppData, "cfg/game.json", "{\"v\":1}");

            var read = await FileManager.ReadAllTextAsync(FileDomain.AppData, "cfg/game.json");

            Assert.AreEqual("{\"v\":1}", read, "同步写入的内容应能被异步入口原样读到");
        }

        [Test]
        public async Task ReadAllText_Sync_SeesContentWrittenByAsync()
        {
            await FileManager.WriteAllTextAsync(FileDomain.AppData, "cfg/game.json", "{\"v\":2}");

            var read = FileManager.ReadAllText(FileDomain.AppData, "cfg/game.json");

            Assert.AreEqual("{\"v\":2}", read, "同步读应看到异步写入的内容");
        }

        [Test]
        public async Task ReadAllBytes_Sync_SeesBytesWrittenByAsync()
        {
            // 载荷含非 UTF-8 字节：若同步读误走文本通道，这些字节会被替换字符损坏
            var payload = new byte[] { 0x00, 0xFF, 0x10, 0x80 };
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, "blob.bin", payload);

            var read = FileManager.ReadAllBytes(FileDomain.AppData, "blob.bin");

            CollectionAssert.AreEqual(payload, read, "同步读应逐字节还原异步写入的字节");
        }

        [Test]
        public void ReadAllText_MissingFile_ReturnsNull()
        {
            Assert.IsNull(FileManager.ReadAllText(FileDomain.AppData, "nope.json"),
                "文件不存在应返回 null 而非抛异常（与异步版契约一致）");
        }

        [Test]
        public void ReadAllBytes_MissingFile_ReturnsNull()
        {
            Assert.IsNull(FileManager.ReadAllBytes(FileDomain.AppData, "nope.bin"),
                "文件不存在应返回 null 而非抛异常（与异步版契约一致）");
        }

        [Test]
        public async Task GetFiles_Sync_ReturnsForwardSlashRelativePaths()
        {
            await FileManager.WriteAllTextAsync(FileDomain.AppData, "sub/a.txt", "a");
            await FileManager.WriteAllTextAsync(FileDomain.AppData, "sub/b.txt", "b");

            var files = FileManager.GetFiles(FileDomain.AppData, "sub");

            CollectionAssert.AreEquivalent(new[] { "sub/a.txt", "sub/b.txt" }, files,
                "同步枚举应返回相对域根、正斜杠分隔的路径，可直接回传本模块其他方法");
        }

        [Test]
        public void GetFiles_MissingDirectory_ReturnsEmpty()
        {
            var files = FileManager.GetFiles(FileDomain.AppData, "no_such_dir");

            Assert.IsNotNull(files, "目录不存在时返回空数组而非 null");
            Assert.IsEmpty(files, "目录不存在时返回空数组");
        }

        #endregion

        #region 移动端 Streaming 域：同步读拒绝

        [Test]
        public void IsSyncStreamingUnsupported_MobileProviderStreamingDomain_IsTrue()
        {
            Assert.IsTrue(
                FileManager.IsSyncStreamingUnsupported(new MobileFileProvider(), FileDomain.Streaming),
                "移动端 Streaming 域：内容读的续体依赖 PlayerLoop（同步等必死锁），" +
                "查询是主线程无界自旋（安全性未经验证）——两者一并拒绝");
        }

        [Test]
        public void IsSyncStreamingUnsupported_MobileProviderOtherDomain_IsFalse()
        {
            Assert.IsFalse(
                FileManager.IsSyncStreamingUnsupported(new MobileFileProvider(), FileDomain.AppData),
                "移动端非 Streaming 域走 System.IO 线程池，同步访问可用");
        }

        [Test]
        public void IsSyncStreamingUnsupported_DesktopProviderStreamingDomain_IsFalse()
        {
            Assert.IsFalse(
                FileManager.IsSyncStreamingUnsupported(_fileProvider, FileDomain.Streaming),
                "桌面 Provider 的 Streaming 走 System.IO 线程池，同步访问可用——不可与移动端类推");
        }

        [Test]
        public void ReadAllText_MobileStreamingDomain_ThrowsInsteadOfHanging()
        {
            FileManager.Destroy();
            FileManager.Initialize(new MobileFileProvider());
            try
            {
                Assert.Throws<NotSupportedException>(
                    () => FileManager.ReadAllText(FileDomain.Streaming, "cfg/game.json"),
                    "移动端 Streaming 的同步读应拒绝，而不是阻塞到死锁");
            }
            finally
            {
                FileManager.Destroy();
            }
        }

        [Test]
        public void ReadAllBytes_MobileStreamingDomain_ThrowsInsteadOfHanging()
        {
            FileManager.Destroy();
            FileManager.Initialize(new MobileFileProvider());
            try
            {
                Assert.Throws<NotSupportedException>(
                    () => FileManager.ReadAllBytes(FileDomain.Streaming, "blob.bin"),
                    "移动端 Streaming 的同步读应拒绝，而不是阻塞到死锁");
            }
            finally
            {
                FileManager.Destroy();
            }
        }

        [Test]
        public void Exists_MobileStreamingDomain_ThrowsInsteadOfSpinningForever()
        {
            FileManager.Destroy();
            FileManager.Initialize(new MobileFileProvider());
            try
            {
                // 拒绝的理由与内容读不同：这里的同步实现是 while (!request.isDone) {} 的无界自旋，
                // 其完成依赖一条本仓无法证实的引擎断言——为假就是永久挂死（无异常、无日志）
                Assert.Throws<NotSupportedException>(
                    () => FileManager.Exists(FileDomain.Streaming, "cfg/game.json"),
                    "移动端 Streaming 的同步查询应拒绝，而不是无界自旋");
            }
            finally
            {
                FileManager.Destroy();
            }
        }

        #endregion

        #region FileManagerExtensions 入口与门面一致

        [Test]
        public void FileManagerExtensions_AndFacade_AreInterchangeable()
        {
            // 门面写 → 旧入口读
            FileManager.WriteAllText(FileDomain.AppData, "cfg/a.json", "A");
            Assert.AreEqual("A", FileManagerExtensions.ReadAllText(FileDomain.AppData, "cfg/a.json"),
                "旧入口读到的内容应与门面写入的一致");

            // 旧入口写 → 门面读
            FileManagerExtensions.WriteAllText(FileDomain.AppData, "cfg/b.json", "B");
            Assert.AreEqual("B", FileManager.ReadAllText(FileDomain.AppData, "cfg/b.json"),
                "门面读到的内容应与旧入口写入的一致");
        }

        [Test]
        public async Task FileManagerExtensions_GetFiles_MatchesFacade()
        {
            await FileManager.WriteAllTextAsync(FileDomain.AppData, "sub/c.txt", "c");

            var viaExtensions = FileManagerExtensions.GetFiles(FileDomain.AppData, "sub");

            CollectionAssert.AreEquivalent(new[] { "sub/c.txt" }, viaExtensions,
                "旧入口的枚举结果应与门面一致（同为相对域根、正斜杠分隔）");
        }

        [Test]
        public void FileManagerExtensions_ReadAllText_MobileStreamingDomain_ThrowsInsteadOfHanging()
        {
            FileManager.Destroy();
            FileManager.Initialize(new MobileFileProvider());
            try
            {
                Assert.Throws<NotSupportedException>(
                    () => FileManagerExtensions.ReadAllText(FileDomain.Streaming, "cfg/game.json"),
                    "旧入口同样应拒绝而非死锁——它改走门面后一并继承了这道守卫");
            }
            finally
            {
                FileManager.Destroy();
            }
        }

        #endregion

        #region 同步面补齐：原子写与子目录枚举

        [Test]
        public async Task WriteAllBytesAtomic_Sync_KeepsOneGenerationBackup()
        {
            var backupPath = "slot_1.save" + FilePathUtility.BackupFileSuffix;
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, "slot_1.save", Encoding.UTF8.GetBytes("old"));

            FileManager.WriteAllBytesAtomic(FileDomain.AppData, "slot_1.save", Encoding.UTF8.GetBytes("new"));

            var current = FileManager.ReadAllBytes(FileDomain.AppData, "slot_1.save");
            var backup = FileManager.ReadAllBytes(FileDomain.AppData, backupPath);
            Assert.AreEqual("new", Encoding.UTF8.GetString(current), "同步原子写应完整覆盖旧内容");
            Assert.IsNotNull(backup, "替换既有文件后应生成一代备份");
            Assert.AreEqual("old", Encoding.UTF8.GetString(backup), "备份应保留替换前的旧内容");
        }

        [Test]
        public void WriteAllBytesAtomic_Sync_LeavesNoTmpResidue()
        {
            FileManager.WriteAllBytesAtomic(FileDomain.AppData, "slot_2.save", Encoding.UTF8.GetBytes("v1"));

            Assert.IsFalse(FileManager.Exists(FileDomain.AppData, "slot_2.save" + FilePathUtility.TempFileSuffix),
                "同步原子写完成后不应残留 .tmp 文件");
            Assert.IsTrue(FileManager.Exists(FileDomain.AppData, "slot_2.save"),
                "首个版本应已落到正式文件");
        }

        [Test]
        public void GetDirectories_Sync_ReturnsPathsRelativeToDomainRoot()
        {
            FileManager.CreateDirectory(FileDomain.AppData, "parent/child");

            var atRoot = FileManager.GetDirectories(FileDomain.AppData, null);
            var underParent = FileManager.GetDirectories(FileDomain.AppData, "parent");

            CollectionAssert.AreEquivalent(new[] { "parent" }, atRoot, "域根下应能枚举出 parent");
            CollectionAssert.AreEquivalent(new[] { "parent/child" }, underParent,
                "返回路径相对域根而非被查询目录——与 GetFiles 同规范，可直接回传本模块其他方法");
        }

        [Test]
        public void GetDirectories_MissingDirectory_ReturnsEmpty()
        {
            var dirs = FileManager.GetDirectories(FileDomain.AppData, "no_such_dir");

            Assert.IsNotNull(dirs, "目录不存在时返回空数组而非 null");
            Assert.IsEmpty(dirs, "目录不存在时返回空数组");
        }

        #endregion

        #region 生命周期往返

        /// <summary>
        /// 销毁 → 抛异常 → 重新初始化恢复，这条往返此前**零断言**（`IsInitialized` 更是全仓零引用），
        /// 而它是错误文案「请重新调用 Initialize」所承诺的东西——文案教人做的事，照着做必须真的能成。
        /// <para><b>今天是绿的，属新增守卫而非回归</b>：`ThrowIfDestroyed` 与恢复路径都已存在，
        /// 本用例只是第一次把它们钉住。</para>
        /// </summary>
        [Test]
        public void Destroy_ThenAnyCall_ThrowsObjectDisposed_AndInitializeRecovers()
        {
            FileManager.WriteAllText(FileDomain.AppData, "lifecycle/probe.txt", "keep");
            Assert.IsTrue(FileManager.IsInitialized, "前置：已初始化");

            FileManager.Destroy();

            Assert.IsFalse(FileManager.IsInitialized, "销毁后 IsInitialized 应为 false");
            Assert.Throws<ObjectDisposedException>(
                () => FileManager.Exists(FileDomain.AppData, "lifecycle/probe.txt"),
                "销毁后任何公开调用都应抛 ObjectDisposedException，而不是静默失败或悄悄自动重建");

            FileManager.Initialize(_fileProvider);

            Assert.IsTrue(FileManager.IsInitialized, "Initialize 是文档指明的恢复入口");
            Assert.AreEqual("keep", FileManager.ReadAllText(FileDomain.AppData, "lifecycle/probe.txt"),
                "恢复后文件操作照常，且原有数据仍在");
        }

        #endregion
    }
}
