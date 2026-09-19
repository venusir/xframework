using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XData;
using XFramework.XFileManager;
using XFramework.XSerialize;

namespace XFramework.XSave.Tests
{
    /// <summary>
    /// Save 模块端到端测试（<see cref="SaveManagerImpl"/> + 真实文件系统临时目录）。
    /// <para>覆盖:保存/覆盖/加载、双缓冲残留清理、元数据读取、损坏文件容错、玩家隔离。</para>
    /// </summary>
    [TestFixture]
    public class SaveManagerImplTests
    {
        [Serializable]
        private sealed class WalletData : IDataBlock
        {
            public string BlockName => "Wallet";
            public int Gold;
            public int DataVersion => 0;
            public object OnSave() => Gold;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { if (data is int i) Gold = i; }
            public void OnClear() => Gold = 0;
        }

        private TempFileProvider _fileProvider;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _fileProvider?.Cleanup();
        }

        [SetUp]
        public void SetUp()
        {
            // 每个测试使用全新临时目录：共享目录会让槽位文件跨测试残留
            // （如 slot_1.save 破坏「槽位不存在」类测试的前提）
            _fileProvider?.Cleanup();
            _fileProvider = new TempFileProvider();

            // FileManager 支持 Destroy 后重新 Initialize（提交 6a），是每个测试注入独立临时目录的前提
            FileManager.Destroy();
            FileManager.Initialize(_fileProvider);
            Serializer.Initialize();
            DataManager.Initialize(new DataManagerImpl());
            SaveManager.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            SaveManager.Shutdown();
            DataManager.Shutdown();
            FileManager.Destroy();

            // 还原成「零配置可用」，而不是把销毁闩锁留给后续 fixture——懒加载豁免只对「未初始化」生效，
            // 已销毁时 EnsureInitialized 直接抛，同进程混跑两个平台会让后面的 EditMode 用例吃到它
            FileManager.Initialize();
        }

        [Test]
        public async Task SaveAsync_ThenSlotExists()
        {
            await SaveManager.SaveAsync(1);

            Assert.IsTrue(await SaveManager.SlotExistsAsync(1), "保存后槽位应存在");
        }

        [Test]
        public async Task SaveAsync_ReturnsMetaWithFields()
        {
            var meta = await SaveManager.SaveAsync(1);

            Assert.AreEqual(1, meta.slot);
            Assert.IsTrue(meta.version >= 1, "快照版本应为 1 或更高");
            Assert.IsFalse(string.IsNullOrEmpty(meta.timestamp), "时间戳不应为空");
            Assert.IsTrue(meta.fileSize > 0, "文件大小应大于 0");
            Assert.AreEqual("slot_1.save", meta.relativePath);
        }

        [Test]
        public async Task SaveAsync_OverwriteExistingSlot()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 10;
            await SaveManager.SaveAsync(1);

            wallet.Gold = 20;
            await SaveManager.SaveAsync(1); // 覆盖:Provider 层原子写(tmp → 替换正式文件)

            wallet.Gold = 0;
            await SaveManager.LoadAsync(1);

            Assert.AreEqual(20, wallet.Gold, "第二次保存应覆盖第一次的数据");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_1.save.tmp"), "覆盖保存后不应残留 tmp 文件");
        }

        [Test]
        public async Task LoadAsync_RestoresBlocksAndClearsDirty()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            DataManager.MarkDirty<WalletData>();

            await SaveManager.SaveAsync(1); // CreateSnapshot 成功后清空脏标记
            Assert.IsFalse(DataManager.HasDirtyBlocks, "保存后应清空脏标记");

            wallet.Gold = 0;
            await SaveManager.LoadAsync(1);

            Assert.AreEqual(42, wallet.Gold, "加载应恢复快照数据");
            Assert.IsFalse(DataManager.HasDirtyBlocks, "加载恢复后不应有脏标记");
        }

        [Test]
        public async Task LoadAsync_SlotNotExist_Throws()
        {
            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.LoadAsync(1),
                "不存在的槽位应抛出 InvalidOperationException");
        }

        [Test]
        public async Task LoadAsync_EmptyFile_Throws()
        {
            // 直接写入空文件（不走 SaveAsync），模拟空文件存档
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save", Array.Empty<byte>());

            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.LoadAsync(1),
                "空文件应抛出 InvalidOperationException");
        }

        [Test]
        public async Task LoadAsync_CorruptedFile_Throws()
        {
            // 直接写入垃圾字节（不走 SaveAsync），模拟损坏存档
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save", Encoding.UTF8.GetBytes("not a save file"));

            await AssertThrowsAsync<Exception>(() => SaveManager.LoadAsync(1),
                "损坏文件应抛出异常而非静默失败");
        }

        [Test]
        public async Task LoadAsync_ValidJsonButNotSave_ThrowsWithoutClearingData()
        {
            // 回归锁：内容为合法 JSON 但不是存档时，反序列化出的 DataSnapshot 其 blocks 取到的是
            // 字段初始化器给的空列表（不是 null）。旧实现照常 ApplySnapshot——先 OnClear 掉全部
            // 数据块、再因列表为空直接 return，玩家的内存数据被零警告清空
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save",
                Encoding.UTF8.GetBytes("{\"foo\":1}"));

            await AssertThrowsAsync<Exception>(() => SaveManager.LoadAsync(1),
                "合法 JSON 但不是存档的内容应被拒绝，而不是当作空快照应用");

            Assert.AreEqual(42, wallet.Gold, "拒绝加载后内存数据必须保持原样，不能被清空");
        }

        [Test]
        public async Task LoadAsync_JsonNull_ThrowsWithoutClearingData()
        {
            // 另一变体：字面量 null 反序列化结果本身即为 null，旧实现会先清空全部数据块再抛 NRE
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save",
                Encoding.UTF8.GetBytes("null"));

            await AssertThrowsAsync<Exception>(() => SaveManager.LoadAsync(1),
                "反序列化结果为 null 时应被拒绝");

            Assert.AreEqual(42, wallet.Gold, "拒绝加载后内存数据必须保持原样，不能被清空");
        }

        [Test]
        public async Task GetSlotMetaAsync_ValidJsonButNotSave_ReturnsCorruptedMeta()
        {
            // 与 GetSlotMetasAsync 结论一致：损坏即告警并返回带标记的条目，
            // 而不是让原始序列化异常穿透、也不是返回 null（null 严格表示「槽位不存在」）
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save",
                Encoding.UTF8.GetBytes("{\"foo\":1}"));

            LogAssert.Expect(LogType.Warning, new Regex("解析存档元数据失败"));
            var meta = await SaveManager.GetSlotMetaAsync(1);

            Assert.IsNotNull(meta, "损坏槽位仍应返回元数据，供界面展示与删除");
            Assert.IsTrue(meta.isCorrupted, "应带损坏标记");
            Assert.AreEqual(1, meta.slot);
        }

        [Test]
        public async Task GetSlotMetaAsync_MissingSlot_ReturnsNull()
        {
            // null 严格表示「槽位不存在」——与「存在但损坏」区分开
            Assert.IsNull(await SaveManager.GetSlotMetaAsync(7), "不存在的槽位应返回 null");
        }

        [Test]
        public async Task TryLoadAsync_HappyPath_ReturnsLoadedWithMeta()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            wallet.Gold = 0;

            var result = await SaveManager.TryLoadAsync(1);

            Assert.AreEqual(SaveLoadStatus.Loaded, result.Status);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Meta, "成功时应带回元数据");
            Assert.AreEqual(1, result.Meta.slot);
            Assert.IsNull(result.Message, "成功时不应有诊断信息");
            Assert.AreEqual(42, wallet.Gold);
        }

        [Test]
        public async Task TryLoadAsync_MissingSlot_ReportsMissingWithoutThrowing()
        {
            // 预期内的失败以状态回报：调用方（存档界面）用 try 表达正常分支是别扭的
            var result = await SaveManager.TryLoadAsync(7);

            Assert.AreEqual(SaveLoadStatus.Missing, result.Status);
            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.Meta, "缺失时不应有元数据");
            Assert.IsNotNull(result.Message, "失败时应带诊断信息");
        }

        [Test]
        public async Task TryLoadAsync_CorruptedFile_ReportsCorruptWithoutThrowing()
        {
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save",
                Encoding.UTF8.GetBytes("not a save"));

            var result = await SaveManager.TryLoadAsync(1);

            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status);
            Assert.IsFalse(result.IsSuccess);
        }

        [Test]
        public async Task TryLoadAsync_CorruptedMain_FallsBackToBackup()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();

            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            wallet.Gold = 99;
            await SaveManager.SaveAsync(1);   // 覆盖产生一代备份（内容为 Gold=42）

            // 破坏主文件，保留完好的备份
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save",
                Encoding.UTF8.GetBytes("corrupted"));

            wallet.Gold = 0;
            LogAssert.Expect(LogType.Warning, new Regex("已从备份恢复"));
            var result = await SaveManager.TryLoadAsync(1);

            Assert.AreEqual(SaveLoadStatus.LoadedFromBackup, result.Status, "主文件损坏时应从备份恢复");
            Assert.IsTrue(result.IsSuccess, "从备份恢复属于成功");
            Assert.AreEqual(42, wallet.Gold, "应恢复备份里的内容");
            Assert.IsNotNull(result.Message, "应保留主文件不可用的原因");
        }

        [Test]
        public async Task TryLoadAsync_BothUnusable_ReportsCorrupt()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(1);

            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save",
                Encoding.UTF8.GetBytes("corrupted"));
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData,
                "slot_1.save" + FilePathUtility.BackupFileSuffix, Encoding.UTF8.GetBytes("corrupted too"));

            var result = await SaveManager.TryLoadAsync(1);

            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status);
        }

        [Test]
        public async Task TryLoadAsync_ApplyFails_RollsBackMemory()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            DataManager.GetOrCreateBlock<ExplodingBlock>();
            await SaveManager.SaveAsync(1);

            // 加载前把内存改成另一个值：回滚成功的话应恢复到这个值，而不是被清成默认值
            wallet.Gold = 7;
            ExplodingBlock.RemainingThrows = 1;
            try
            {
                // 应用失败会记 LogError（结果结构体只带摘要文本，堆栈靠这里保留）
                LogAssert.Expect(LogType.Error, new Regex("应用存档失败，正在回滚内存数据"));

                var result = await SaveManager.TryLoadAsync(1);

                Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status, "快照无法应用时应回报为损坏");
                Assert.AreEqual(7, wallet.Gold, "应用失败后必须回滚到加载前的内存状态，而不是留下半清空状态");
            }
            finally
            {
                ExplodingBlock.RemainingThrows = 0;
            }
        }

        /// <summary>
        /// 探针 Block：在 <see cref="IDataBlock.OnClear"/> 中按次数抛异常，
        /// 用于验证「应用快照失败 → 回滚内存」这条路径。
        /// <para><b>为什么抛在 OnClear 而不是 OnLoad：</b><see cref="XData.DataManagerImpl"/> 的
        /// <c>ApplySnapshot</c> 只在清空阶段无保护（<c>ForEachBlock(b =&gt; b.OnClear())</c>），
        /// 恢复阶段走 <c>TryRestoreBlock</c>，其内部 try/catch 会把异常记成 warning 并吞掉——
        /// 抛在 OnLoad 上根本到不了 <see cref="SaveManagerImpl"/>，也就测不到回滚。</para>
        /// <para>「只抛一次」也是刻意的：若每次都抛，回滚自身的 <c>ApplySnapshot</c> 也会失败，
        /// 就只测到「回滚失败」分支，而要验证的「回滚成功」反而没覆盖。</para>
        /// </summary>
        [Serializable]
        private sealed class ExplodingBlock : IDataBlock
        {
            public static int RemainingThrows;

            public string BlockName => "Exploding";
            public int DataVersion => 0;
            public object OnSave() => 1;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { }

            public void OnClear()
            {
                if (RemainingThrows > 0)
                {
                    RemainingThrows--;
                    throw new InvalidOperationException("模拟第三方 Block 清空失败");
                }
            }
        }

        #region 加密接线

        [Test]
        public async Task Initialize_WithCryptoProvider_EncryptsSaveDataOnly()
        {
            SaveManager.Shutdown();
            SaveManager.Initialize(null, new SaveOptions { CryptoProvider = new XorCryptoProvider("test-key") });

            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, "plain.bin", Encoding.UTF8.GetBytes("plain"));

            // 未加密的存档是 JSON，首字节必为 '{'；密文不会
            var payloadRaw = await _fileProvider.ReadAllBytesAsync(FileDomain.SaveData, "slot_1.save");
            Assert.AreNotEqual((byte)'{', payloadRaw[0], "存档载荷应以密文形态落盘");

            var appRaw = await _fileProvider.ReadAllBytesAsync(FileDomain.AppData, "plain.bin");
            Assert.AreEqual("plain", Encoding.UTF8.GetString(appRaw), "非存档域不应被连带加密");

            wallet.Gold = 0;
            await SaveManager.LoadAsync(1);
            Assert.AreEqual(42, wallet.Gold, "加密存档应能正常读回");
        }

        [Test]
        public async Task CryptoEnabled_EnumerationAndLoadWork()
        {
            SaveManager.Shutdown();
            SaveManager.Initialize(null, new SaveOptions { CryptoProvider = new XorCryptoProvider("test-key") });

            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);

            var metas = await SaveManager.GetSlotMetasAsync();
            Assert.AreEqual(1, metas.Count, "加密存档的枚举应经侧车正常返回");

            wallet.Gold = 0;
            await SaveManager.LoadAsync(1);
            Assert.AreEqual(42, wallet.Gold);
        }

        [Test]
        public async Task EnablingCryptoAfterSaves_MakesExistingSavesUnreadable()
        {
            // 锁定 SaveOptions.CryptoProvider 文档里的陷阱一：对称实现面对明文同样会「解密成功」
            // 而不抛异常，只产出垃圾字节，最终表现为「存档已损坏」。
            // 因此开启加密或更换密钥前必须先迁移存量存档，不能直接切换
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);

            SaveManager.Shutdown();
            SaveManager.Initialize(null, new SaveOptions { CryptoProvider = new XorCryptoProvider("test-key") });

            var result = await SaveManager.TryLoadAsync(1);

            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status,
                "加密前的明文存档在开启加密后应被判定为损坏");
        }

        #endregion

        #region 槽位复制与移动

        [Test]
        public async Task CopySlotAsync_CopiesContentAndRebuildsSidecar()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);

            var destMeta = await SaveManager.CopySlotAsync(1, 2);

            Assert.AreEqual(2, destMeta.slot, "返回的应是目标槽位的元数据");
            Assert.AreEqual("slot_2.save", destMeta.relativePath);

            // 侧车必须按目标重建：照抄源侧车会让元数据把目标报成源槽位
            var listed = await SaveManager.GetSlotMetaAsync(2);
            Assert.AreEqual(2, listed.slot, "目标侧车里的槽位号应为目标槽位，而非源槽位");

            Assert.IsTrue(await SaveManager.SlotExistsAsync(1), "复制不应影响源槽位");

            wallet.Gold = 0;
            await SaveManager.LoadAsync(2);
            Assert.AreEqual(42, wallet.Gold, "目标槽位应能读出源的内容");
        }

        [Test]
        public async Task CopySlotAsync_TargetOccupiedWithoutOverwrite_Throws()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            wallet.Gold = 99;
            await SaveManager.SaveAsync(2);

            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.CopySlotAsync(1, 2),
                "目标已存在且未允许覆盖时应拒绝");

            wallet.Gold = 0;
            await SaveManager.LoadAsync(2);
            Assert.AreEqual(99, wallet.Gold, "被拒绝的复制不得改动目标槽位");
        }

        [Test]
        public async Task CopySlotAsync_Overwrite_ReplacesTarget()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            wallet.Gold = 99;
            await SaveManager.SaveAsync(2);

            await SaveManager.CopySlotAsync(1, 2, overwrite: true);

            wallet.Gold = 0;
            await SaveManager.LoadAsync(2);
            Assert.AreEqual(42, wallet.Gold, "允许覆盖时应替换目标内容");
        }

        [Test]
        public async Task CopySlotAsync_MissingSource_Throws()
        {
            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.CopySlotAsync(1, 2),
                "源槽位不存在时应拒绝");
        }

        [Test]
        public async Task CopySlotAsync_SameSlot_Throws()
        {
            await SaveManager.SaveAsync(1);

            await AssertThrowsAsync<ArgumentException>(() => SaveManager.CopySlotAsync(1, 1),
                "源与目标相同时应拒绝");
        }

        [Test]
        public async Task MoveSlotAsync_RemovesSourceEntirely()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(1);   // 再存一次，让源槽位带上 .bak

            var destMeta = await SaveManager.MoveSlotAsync(1, 3);

            Assert.AreEqual(3, destMeta.slot);
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_1.save"), "移动后源载荷应消失");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_1.save.meta"), "源侧车应一并清除");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_1.save" + FilePathUtility.BackupFileSuffix),
                "源备份应一并清除");

            wallet.Gold = 0;
            await SaveManager.LoadAsync(3);
            Assert.AreEqual(42, wallet.Gold, "内容应完整迁移到目标槽位");
        }

        [Test]
        public async Task TransferSlotAsync_IsGatedByBusy()
        {
            await SaveManager.SaveAsync(1);

            // 故意不 await：SaveAsync 会同步执行到第一个 IO await 处挂起，此时门禁已置位
            var saveTask = SaveManager.SaveAsync(2);

            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.CopySlotAsync(1, 3),
                "复制属于写操作，应受写门禁约束");

            await saveTask;
        }

        #endregion

        #region 序列化线程

        /// <summary>
        /// 包装真实序列化器并记录自己被调用的线程，用于验证序列化往返确实被移出了主线程。
        /// <para>Format 与内层一致，因此 <c>Serializer.Register</c> 会顶替默认注册——
        /// 而 <c>Serializer.Default</c> 按 "json" 查找，正好命中本替身。</para>
        /// <para><b>只记录载荷（<see cref="DataSnapshot"/>）的往返</b>：元数据侧车同样经
        /// <c>Serializer.Default</c>，但它刻意留在主线程（对象极小，线程池往返的调度成本高于收益）。
        /// 若不按类型过滤，侧车那次调用会覆盖记录，断言便会指向错误的观测对象。</para>
        /// </summary>
        private sealed class ThreadRecordingSerializer : ISerializer
        {
            public static int LastSerializeThreadId;
            public static int LastDeserializeThreadId;

            private readonly ISerializer _inner;

            public ThreadRecordingSerializer(ISerializer inner) => _inner = inner;

            public string Format => _inner.Format;

            public byte[] Serialize(object obj, Type type)
            {
                if (typeof(DataSnapshot).IsAssignableFrom(type))
                    LastSerializeThreadId = Environment.CurrentManagedThreadId;

                return _inner.Serialize(obj, type);
            }

            public object Deserialize(byte[] data, Type type)
            {
                if (typeof(DataSnapshot).IsAssignableFrom(type))
                    LastDeserializeThreadId = Environment.CurrentManagedThreadId;

                return _inner.Deserialize(data, type);
            }
        }

        [Test]
        public async Task SaveAndLoad_RunSerializationOffMainThread()
        {
            // 断言序列化器自身跑在哪个线程，而不是在测试体里查线程——测试方法自身是 async Task，
            // 其续体会被 Unity 的 SynchronizationContext 弹回主线程，在测试体里断言测不到真实情况。
            // 「操作返回后回到主线程」由本文件既有的两条探针测试（CreateMeta / OnLoad）覆盖。
            var mainThreadId = Environment.CurrentManagedThreadId;
            var original = Serializer.Default;
            Serializer.Register(new ThreadRecordingSerializer(original));
            try
            {
                await SaveManager.SaveAsync(1);
                await SaveManager.LoadAsync(1);

                Assert.AreNotEqual(mainThreadId, ThreadRecordingSerializer.LastSerializeThreadId,
                    "序列化应在线程池上执行，而不是阻塞主线程");
                Assert.AreNotEqual(mainThreadId, ThreadRecordingSerializer.LastDeserializeThreadId,
                    "反序列化应在线程池上执行");
            }
            finally
            {
                Serializer.Register(original);
            }
        }

        #endregion

        #region 进度上报

        /// <summary>
        /// 记录全部进度报告的测试替身。
        /// </summary>
        private sealed class RecordingProgress : IProgress<SaveReport>
        {
            public readonly List<SaveReport> Reports = new List<SaveReport>();

            public void Report(SaveReport value) => Reports.Add(value);
        }

        [Test]
        public async Task GetSlotMetasAsync_ReportsProgress()
        {
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(2);
            var progress = new RecordingProgress();

            var metas = await SaveManager.GetSlotMetasAsync(progress);

            Assert.AreEqual(2, metas.Count, "带进度的重载应返回与不带进度时相同的结果");
            Assert.IsTrue(progress.Reports.Count > 0, "应上报进度");

            var last = progress.Reports[progress.Reports.Count - 1];
            Assert.AreEqual(1f, last.Progress, 0.001f, "最后一次上报应为完成");
            Assert.IsFalse(string.IsNullOrEmpty(last.Description), "应带步骤描述");

            // 进度不得倒退：调用方据此画进度条，倒退会表现为进度条回跳
            for (int i = 1; i < progress.Reports.Count; i++)
                Assert.GreaterOrEqual(progress.Reports[i].Progress, progress.Reports[i - 1].Progress,
                    $"第 {i} 次上报的进度不应低于前一次");
        }

        [Test]
        public async Task GetPlayerSlotMetasAsync_ReportsProgress()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            SaveManager.SetCurrentPlayer("Alice");
            wallet.Gold = 1;
            await SaveManager.SaveAsync(1);

            var progress = new RecordingProgress();
            var metas = await SaveManager.GetPlayerSlotMetasAsync("Alice", progress);

            Assert.AreEqual(1, metas.Count);
            Assert.IsTrue(progress.Reports.Count > 0, "按玩家查询也应上报进度");
            Assert.AreEqual(1f, progress.Reports[progress.Reports.Count - 1].Progress, 0.001f);
        }

        [Test]
        public async Task DeleteAllSlotsAsync_ReportsProgress()
        {
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(2);
            var progress = new RecordingProgress();

            var deleted = await SaveManager.DeleteAllSlotsAsync(progress);

            Assert.AreEqual(2, deleted, "带进度的重载应返回与不带进度时相同的计数");
            Assert.IsTrue(progress.Reports.Count > 0, "应上报进度");
            Assert.AreEqual(1f, progress.Reports[progress.Reports.Count - 1].Progress, 0.001f);
        }

        [Test]
        public async Task DeletePlayerAsync_ReportsProgress()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            SaveManager.SetCurrentPlayer("Alice");
            wallet.Gold = 1;
            await SaveManager.SaveAsync(1);

            var progress = new RecordingProgress();
            var deleted = await SaveManager.DeletePlayerAsync("Alice", progress);

            Assert.AreEqual(1, deleted);
            Assert.IsTrue(progress.Reports.Count > 0, "按玩家删除也应上报进度");
        }

        #endregion

        #region 启动恢复扫描

        [Test]
        public async Task RecoverAsync_RestoresPayloadFromBackup()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            wallet.Gold = 99;
            await SaveManager.SaveAsync(1);   // 覆盖产生一代备份（内容为 Gold=42）

            // 模拟替换流程崩溃后最坏的情况：载荷没了、备份还在
            FileManager.Delete(FileDomain.SaveData, "slot_1.save");

            LogAssert.Expect(LogType.Warning, new Regex("载荷缺失，已由一代备份恢复"));
            await SaveManager.RecoverAsync();

            Assert.IsTrue(FileManager.Exists(FileDomain.SaveData, "slot_1.save"), "恢复扫描应还原载荷");

            wallet.Gold = 0;
            await SaveManager.LoadAsync(1);

            Assert.AreEqual(42, wallet.Gold, "还原的应是备份里的内容");
        }

        [Test]
        public async Task RecoverAsync_RemovesTempResidue()
        {
            await SaveManager.SaveAsync(1);
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save.tmp",
                Encoding.UTF8.GetBytes("partial"));

            await SaveManager.RecoverAsync();

            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_1.save.tmp"),
                "原子写之后 .tmp 只可能是崩溃残留，应被清掉");
            Assert.IsTrue(FileManager.Exists(FileDomain.SaveData, "slot_1.save"), "载荷不应受影响");
        }

        [Test]
        public async Task RecoverAsync_RebuildsMissingSidecar()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            FileManager.Delete(FileDomain.SaveData, SidecarPath);

            await SaveManager.RecoverAsync();

            Assert.IsTrue(FileManager.Exists(FileDomain.SaveData, SidecarPath), "侧车缺失时应由载荷重建");

            wallet.Gold = 0;
            var result = await SaveManager.TryLoadAsync(1);
            Assert.AreEqual(SaveLoadStatus.Loaded, result.Status, "重建侧车后应能正常加载");
            Assert.AreEqual(42, wallet.Gold);
        }

        [Test]
        public async Task RecoverAsync_KeepsSidecarWhenChecksumMismatches()
        {
            // 校验和不符是「载荷可能已损坏」的信号，重建侧车等于把它抹掉；应保留原侧车，
            // 让加载侧按校验和不符走备份回退
            await SaveManager.SaveAsync(1);

            var payload = await FileManager.ReadAllBytesAsync(FileDomain.SaveData, "slot_1.save");
            var tampered = new byte[payload.Length + 1];
            Array.Copy(payload, tampered, payload.Length);
            tampered[payload.Length] = (byte)' ';
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save", tampered);

            LogAssert.Expect(LogType.Warning, new Regex("校验和与载荷不符"));
            await SaveManager.RecoverAsync();

            // 侧车被保留 → 加载侧仍能识别出校验和不符
            var result = await SaveManager.TryLoadAsync(1);
            Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status, "恢复扫描不应抹掉损坏信号");
        }

        [Test]
        public async Task RecoverAsync_RemovesOrphanCompanions()
        {
            // 载荷与备份都不在，只剩配套文件：应收敛掉，而不是留下看起来像槽位的半截文件
            FileManager.CreateDirectory(FileDomain.SaveData, "Alice");
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "Alice/slot_5.save.meta",
                Encoding.UTF8.GetBytes("{}"));

            await SaveManager.RecoverAsync();

            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "Alice/slot_5.save.meta"),
                "孤儿侧车应被清掉");
        }

        [Test]
        public async Task RecoverAsync_ScansPlayerDirectories()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            SaveManager.SetCurrentPlayer("Alice");
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            wallet.Gold = 99;
            await SaveManager.SaveAsync(1);

            FileManager.Delete(FileDomain.SaveData, "Alice/slot_1.save");

            LogAssert.Expect(LogType.Warning, new Regex("载荷缺失，已由一代备份恢复"));
            await SaveManager.RecoverAsync();

            Assert.IsTrue(FileManager.Exists(FileDomain.SaveData, "Alice/slot_1.save"),
                "恢复扫描应遍历玩家子目录");
        }

        #endregion

        #region 存档格式版本门禁

        [Test]
        public void CurrentVersion_DefaultsToOne_AndIsSettable()
        {
            Assert.AreEqual(1, SaveManager.CurrentVersion, "默认存档格式版本应为 1");

            SaveManager.SetCurrentVersion(4);

            Assert.AreEqual(4, SaveManager.CurrentVersion);
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveManager.SetCurrentVersion(0),
                "版本号必须大于 0");
        }

        [Test]
        public void Initialize_WithOptions_AppliesCurrentVersion()
        {
            SaveManager.Shutdown();
            SaveManager.Initialize(null, new SaveOptions { CurrentVersion = 7 });

            Assert.AreEqual(7, SaveManager.CurrentVersion, "Initialize 应应用选项里的存档格式版本");
        }

        [Test]
        public async Task SaveAsync_StampsCurrentVersion()
        {
            SaveManager.SetCurrentVersion(3);

            var meta = await SaveManager.SaveAsync(1);

            Assert.AreEqual(3, meta.version, "保存应把当前客户端的格式版本写进快照");
        }

        [Test]
        public async Task TryLoadAsync_NewerVersion_RejectsWholeSnapshot()
        {
            // 代码回滚场景：存档由更新的客户端写出。旧客户端若「尽力加载」，
            // Data 模块会按块跳过版本更高的块，结果是内存里一半是新数据、一半空着
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            SaveManager.SetCurrentVersion(5);
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);

            SaveManager.SetCurrentVersion(3);
            wallet.Gold = 7;

            var result = await SaveManager.TryLoadAsync(1);

            Assert.AreEqual(SaveLoadStatus.VersionTooNew, result.Status, "更高版本的存档应整份拒绝");
            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.Message, "应说明需要先升级客户端");
            Assert.AreEqual(7, wallet.Gold, "被拒绝的存档不得部分应用到内存");
        }

        [Test]
        public async Task TryLoadAsync_OlderVersion_ReportsMigrated()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            SaveManager.SetCurrentVersion(3);
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);

            SaveManager.SetCurrentVersion(5);
            wallet.Gold = 0;

            var result = await SaveManager.TryLoadAsync(1);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status, "更旧版本的存档应迁移后加载");
            Assert.IsTrue(result.IsSuccess, "迁移属于成功");
            Assert.AreEqual(42, wallet.Gold);
        }

        #endregion

        #region 加载不完整检测

        [Test]
        public async Task TryLoadAsync_BlockRestoreFails_ReportsCorruptAndRollsBack()
        {
            // 这条覆盖的是一个曾经完全静默的路径：Data 模块把数据块恢复失败记成 warning 后吞掉，
            // ApplySnapshot 正常返回，于是 Save 侧会回报 Loaded——而内存里少了一整个块的数据
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            DataManager.GetOrCreateBlock<FailingLoadBlock>();
            await SaveManager.SaveAsync(1);

            wallet.Gold = 7;
            FailingLoadBlock.RemainingFailures = 1;
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("恢复数据块 FailingLoad 失败"));
                LogAssert.Expect(LogType.Error, new Regex("未能恢复，正在回滚内存数据"));

                var result = await SaveManager.TryLoadAsync(1);

                Assert.AreEqual(SaveLoadStatus.Corrupt, result.Status,
                    "有数据块未恢复时必须判定为加载不完整，而不是回报成功");
                Assert.IsNotNull(result.Message);
                Assert.AreEqual(7, wallet.Gold, "应回滚到加载前的内存状态，而不是留下半加载状态");
            }
            finally
            {
                FailingLoadBlock.RemainingFailures = 0;
            }
        }

        /// <summary>
        /// 探针 Block：<see cref="IDataBlock.OnLoad"/> 按次数抛异常，用于验证「块恢复失败 →
        /// 判定加载不完整并回滚」这条路径。
        /// <para>「只失败一次」是刻意的：回滚自身也会恢复同一个块，若每次都失败就只测到
        /// 「回滚也不完整」分支，而要验证的「回滚成功」反而没覆盖。</para>
        /// </summary>
        [Serializable]
        private sealed class FailingLoadBlock : IDataBlock
        {
            public static int RemainingFailures;

            public string BlockName => "FailingLoad";
            public int DataVersion => 0;
            public object OnSave() => 1;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnClear() { }

            public void OnLoad(object data)
            {
                if (RemainingFailures > 0)
                {
                    RemainingFailures--;
                    throw new InvalidOperationException("模拟第三方 Block 加载失败");
                }
            }
        }

        #endregion

        #region 元数据侧车与校验和

        // 侧车文件名：载荷路径 + .meta
        private const string SidecarPath = "slot_1.save.meta";

        [Test]
        public async Task SaveAsync_WritesSidecarWithChecksum()
        {
            var meta = await SaveManager.SaveAsync(1);

            Assert.IsTrue(FileManager.Exists(FileDomain.SaveData, SidecarPath), "保存后应写出元数据侧车");
            Assert.AreNotEqual(0UL, meta.checksum, "返回的元数据应带校验和");

            // 侧车内容应能直接支撑列表展示：枚举读到的 version/timestamp 与保存时一致
            var listed = await SaveManager.GetSlotMetaAsync(1);
            Assert.AreEqual(meta.version, listed.version);
            Assert.AreEqual(meta.timestamp, listed.timestamp);
            Assert.AreEqual(meta.checksum, listed.checksum, "侧车应完整还原校验和");
        }

        [Test]
        public async Task GetSlotMetasAsync_UsesSidecar_WithoutReadingPayload()
        {
            await SaveManager.SaveAsync(1);

            // 把载荷换成无法解析的内容，但保留侧车：若枚举仍读载荷就会退化成「损坏」，
            // 读到侧车则元数据完好——以此证明枚举没有读取载荷
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save",
                Encoding.UTF8.GetBytes("not a save"));

            var metas = await SaveManager.GetSlotMetasAsync();

            Assert.AreEqual(1, metas.Count);
            Assert.IsFalse(metas[0].isCorrupted, "枚举应走侧车，不应因载荷不可解析而判定损坏");
            Assert.AreEqual(1, metas[0].slot);
        }

        [Test]
        public async Task LoadAsync_TamperedPayloadWithValidJson_RejectedByChecksum()
        {
            // 校验和存在的唯一理由：JSON 仍合法、但字节已被悄悄改坏。
            // 追加一个尾随空格既保持 JSON 合法，又改变字节——没有校验和的话会被静默接受
            await SaveManager.SaveAsync(1);

            var payload = await FileManager.ReadAllBytesAsync(FileDomain.SaveData, "slot_1.save");
            var tampered = new byte[payload.Length + 1];
            Array.Copy(payload, tampered, payload.Length);
            tampered[payload.Length] = (byte)' ';
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_1.save", tampered);

            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.LoadAsync(1),
                "载荷与侧车校验和不符时应拒绝加载");
        }

        [Test]
        public async Task LoadAsync_MissingSidecar_SkipsChecksumAndLoads()
        {
            // 侧车只是加速层：缺失（旧版本写出的存档、或侧车被删）不应让存档不可读
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 42;
            await SaveManager.SaveAsync(1);
            FileManager.Delete(FileDomain.SaveData, SidecarPath);
            FileManager.Delete(FileDomain.SaveData, SidecarPath + FilePathUtility.BackupFileSuffix);

            wallet.Gold = 0;
            await SaveManager.LoadAsync(1);

            Assert.AreEqual(42, wallet.Gold, "侧车缺失时应回退到全量解析并正常加载");
        }

        [Test]
        public async Task DeleteSlotAsync_RemovesSidecarAndBackup()
        {
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(1);   // 第二次覆盖产生一代 .bak

            var backupPath = "slot_1.save" + FilePathUtility.BackupFileSuffix;
            Assert.IsTrue(FileManager.Exists(FileDomain.SaveData, backupPath), "覆盖保存应产生备份");

            await SaveManager.DeleteSlotAsync(1);

            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_1.save"), "载荷应被删除");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, SidecarPath), "侧车应一并删除");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, backupPath), "备份应一并删除");
        }

        [Test]
        public async Task DeleteAllSlotsAsync_RemovesSidecarsAndBackups()
        {
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(2);

            var deleted = await SaveManager.DeleteAllSlotsAsync();

            Assert.AreEqual(2, deleted, "返回的应是合法槽位载荷数量，不含配套文件");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_1.save"));
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_2.save"));
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, SidecarPath), "侧车应被清理");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_2.save.meta"), "侧车应被清理");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_1.save" + FilePathUtility.BackupFileSuffix),
                "备份应被清理");
        }

        #endregion

        [Test]
        public async Task DeleteSlot_RemovesFile()
        {
            await SaveManager.SaveAsync(1);

            await SaveManager.DeleteSlotAsync(1);

            Assert.IsFalse(await SaveManager.SlotExistsAsync(1), "删除后槽位不应存在");
        }

        [Test]
        public async Task DeleteAllSlots_RemovesAllIncludingTmp()
        {
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(2);
            // 手动制造残留 .tmp 文件（模拟写入中途崩溃后遗留）
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_9.save.tmp", Encoding.UTF8.GetBytes("partial"));

            var deleted = await SaveManager.DeleteAllSlotsAsync();

            Assert.AreEqual(2, deleted, "返回值应为实际删除的槽位数量（不含 .tmp 残留）");
            Assert.IsFalse(await SaveManager.SlotExistsAsync(1));
            Assert.IsFalse(await SaveManager.SlotExistsAsync(2));
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_9.save.tmp"), "残留 tmp 文件应被清理");
        }

        [Test]
        public async Task GetSlotMetas_ReturnsAllMetas()
        {
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(3);

            var metas = await SaveManager.GetSlotMetasAsync();

            Assert.AreEqual(2, metas.Count);
            var slots = new HashSet<int>();
            for (int i = 0; i < metas.Count; i++)
            {
                slots.Add(metas[i].slot);
                Assert.IsFalse(string.IsNullOrEmpty(metas[i].timestamp), "meta 应带时间戳");
                Assert.IsTrue(metas[i].fileSize > 0, "meta 应带文件大小");
            }
            Assert.IsTrue(slots.Contains(1) && slots.Contains(3), "应包含全部已保存槽位");
        }

        [Test]
        public async Task GetSlotMeta_NotExist_ReturnsNull()
        {
            var meta = await SaveManager.GetSlotMetaAsync(1);

            Assert.IsNull(meta, "不存在的槽位应返回 null");
        }

        [Test]
        public async Task GetSlotMetasAsync_CorruptedFile_ListedWithFlag()
        {
            await SaveManager.SaveAsync(1);
            // 直接写入损坏文件（不走 SaveAsync）
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_2.save", Encoding.UTF8.GetBytes("corrupted"));

            LogAssert.Expect(LogType.Warning, new Regex("解析存档元数据失败"));
            var metas = await SaveManager.GetSlotMetasAsync();

            // 损坏槽位带标记留在列表里而非消失：消失会与 SlotExistsAsync 返回 true 自相矛盾，
            // 界面既看不到也删不掉它
            Assert.AreEqual(2, metas.Count, "损坏槽位应带标记留在列表里，而不是消失");
            Assert.AreEqual(1, metas[0].slot);
            Assert.IsFalse(metas[0].isCorrupted, "正常存档不应带损坏标记");
            Assert.AreEqual(2, metas[1].slot);
            Assert.IsTrue(metas[1].isCorrupted, "损坏存档应带损坏标记");
            Assert.AreEqual("slot_2.save", metas[1].relativePath, "损坏条目仍应带可定位的路径");
        }

        [Test]
        public async Task GetSlotMetasAsync_ReturnsSortedBySlot()
        {
            // 文件系统返回顺序跨平台不确定，升序让 UI 与断言都有稳定预期
            await SaveManager.SaveAsync(3);
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(2);

            var metas = await SaveManager.GetSlotMetasAsync();

            Assert.AreEqual(3, metas.Count);
            Assert.AreEqual(1, metas[0].slot);
            Assert.AreEqual(2, metas[1].slot);
            Assert.AreEqual(3, metas[2].slot);
        }

        [Test]
        public async Task GetSlotMetasAsync_MalformedFileName_Skipped()
        {
            await SaveManager.SaveAsync(1);
            // slot_abc.save 从前后缀看像槽位文件，但解析不出槽位号——
            // 旧实现会把它当成 slot = -1 塞进列表
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_abc.save", Encoding.UTF8.GetBytes("x"));

            var metas = await SaveManager.GetSlotMetasAsync();

            Assert.AreEqual(1, metas.Count, "解析不出槽位号的文件不应进入列表");
            Assert.AreEqual(1, metas[0].slot);
        }

        [Test]
        public async Task GetSlotMetasAsync_Cancelled_ThrowsInsteadOfPartialList()
        {
            // 取消不是「文件损坏」：裸 catch (Exception) 会把它吞掉，调用方拿到一份
            // 「看起来正常」的部分列表——比抛异常危险得多
            await SaveManager.SaveAsync(1);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await AssertThrowsAsync<OperationCanceledException>(
                () => SaveManager.GetSlotMetasAsync(cts.Token),
                "取消应向上传播，而不是返回部分列表");
        }

        [Test]
        public async Task PlayerIsolation_SameSlotDifferentPlayers_Independent()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();

            SaveManager.SetCurrentPlayer("Alice");
            wallet.Gold = 10;
            await SaveManager.SaveAsync(1);

            wallet.Gold = 0;
            SaveManager.ClearCurrentPlayer(); // 根目录:无玩家上下文,不应看到 Alice 的存档
            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.LoadAsync(1),
                "根目录不应存在 Alice 的存档");
            Assert.IsFalse(await SaveManager.SlotExistsAsync(1), "根目录槽位不应存在");

            SaveManager.SetCurrentPlayer("Alice");
            await SaveManager.LoadAsync(1);

            Assert.AreEqual(10, wallet.Gold, "应恢复 Alice 玩家子目录下的独立存档");
        }

        [Test]
        public async Task GetAllPlayerIdsAsync_ReturnsPlayersWithSaves()
        {
            // 回归锁：旧实现靠 GetFilesAsync 的返回路径切前缀来识别玩家子目录，而该 API 是非递归的、
            // 根目录返回的路径永远不含分隔符，那个分支永不成立——接口长期恒返回空数组
            var wallet = DataManager.GetOrCreateBlock<WalletData>();

            SaveManager.SetCurrentPlayer("Bob");
            wallet.Gold = 1;
            await SaveManager.SaveAsync(1);

            SaveManager.SetCurrentPlayer("Alice");
            wallet.Gold = 2;
            await SaveManager.SaveAsync(1);

            var playerIds = await SaveManager.GetAllPlayerIdsAsync();

            CollectionAssert.AreEquivalent(new[] { "Alice", "Bob" }, playerIds, "应列出所有含存档的玩家");
            Assert.AreEqual("Alice", SaveManager.CurrentPlayerId, "查询玩家列表不得改变当前玩家上下文");
        }

        [Test]
        public async Task GetAllPlayerIdsAsync_ReturnsSorted()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            var ids = new[] { "Carol", "Alice", "Bob" };
            for (int i = 0; i < ids.Length; i++)
            {
                SaveManager.SetCurrentPlayer(ids[i]);
                wallet.Gold = i;
                await SaveManager.SaveAsync(1);
            }

            var playerIds = await SaveManager.GetAllPlayerIdsAsync();

            CollectionAssert.AreEqual(new[] { "Alice", "Bob", "Carol" }, playerIds,
                "应按序返回，便于 UI 稳定展示");
        }

        [Test]
        public async Task GetAllPlayerIdsAsync_EmptyPlayerDirectoryNotListed()
        {
            await SaveManager.SaveAsync(1);   // 根目录存档，不属于任何玩家

            SaveManager.SetCurrentPlayer("Alice");
            await SaveManager.SaveAsync(1);
            await SaveManager.DeletePlayerAsync("Alice");

            var playerIds = await SaveManager.GetAllPlayerIdsAsync();

            // 删除玩家只删文件、不删目录，残留的空目录不应被当成玩家
            Assert.AreEqual(0, playerIds.Length, "空玩家目录不应产生幽灵条目");
        }

        [Test]
        public async Task GetAllPlayerIdsAsync_NoPlayers_ReturnsEmptyArray()
        {
            var playerIds = await SaveManager.GetAllPlayerIdsAsync();

            Assert.IsNotNull(playerIds, "无玩家时应返回空数组而非 null");
            Assert.AreEqual(0, playerIds.Length);
        }

        [Test]
        public async Task GetPlayerSlotMetasAsync_DoesNotDisturbCurrentPlayer()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();

            SaveManager.SetCurrentPlayer("Bob");
            wallet.Gold = 99;
            await SaveManager.SaveAsync(1);

            SaveManager.SetCurrentPlayer("Alice");
            wallet.Gold = 10;
            await SaveManager.SaveAsync(1);

            var bobMetas = await SaveManager.GetPlayerSlotMetasAsync("Bob");

            Assert.AreEqual(1, bobMetas.Count, "应查到 Bob 的存档");
            Assert.AreEqual("Bob", bobMetas[0].playerId, "元数据归属应为被查询的玩家");
            Assert.AreEqual("Alice", SaveManager.CurrentPlayerId, "按玩家查询不得改变当前玩家上下文");

            // 上下文未被污染：后续保存仍应落在当前玩家目录
            wallet.Gold = 20;
            await SaveManager.SaveAsync(2);
            Assert.IsTrue(FileManager.Exists(FileDomain.SaveData, "Alice/slot_2.save"),
                "查询他人存档后，当前玩家的保存仍应落在自己的目录");
        }

        [Test]
        public async Task GetPlayerSlotMetasAsync_InterleavedWithSave_DoesNotMisroute()
        {
            // 回归锁：旧实现查询他人槽位时会把全局 playerId 临时改为目标玩家，并在 await 期间
            // 保持该状态（且该路径不设 IsBusy）。此时并发发起的 SaveAsync 会读到被改写的
            // playerId，把当前玩家的存档写进被查询者的目录——静默串档。
            var wallet = DataManager.GetOrCreateBlock<WalletData>();

            SaveManager.SetCurrentPlayer("Bob");
            wallet.Gold = 99;
            await SaveManager.SaveAsync(1);

            SaveManager.SetCurrentPlayer("Alice");
            wallet.Gold = 10;
            await SaveManager.SaveAsync(1);

            // 故意不 await：查询会同步执行到第一个 IO await 处挂起，构造交错窗口
            var queryTask = SaveManager.GetPlayerSlotMetasAsync("Bob");
            var saveTask = SaveManager.SaveAsync(2);

            var bobMetas = await queryTask;
            await saveTask;

            Assert.AreEqual(1, bobMetas.Count);
            Assert.AreEqual("Bob", bobMetas[0].playerId);
            Assert.IsTrue(FileManager.Exists(FileDomain.SaveData, "Alice/slot_2.save"),
                "查询他人槽位期间发起的保存必须落在当前玩家（Alice）目录");
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "Bob/slot_2.save"),
                "被查询者（Bob）目录不应出现当前会话写入的存档");
        }

        [Test]
        public async Task DeleteSlotAsync_WhileSaveInFlight_Throws()
        {
            // 存档替换期间被并发删除会破坏原子写语义：删槽位会在「已写 .tmp、尚未替换」的窗口里
            // 把目标文件抽走。旧实现不设门禁，静默删掉，存档就此消失
            await SaveManager.SaveAsync(1);

            // 故意不 await：SaveAsync 会同步执行到第一个 IO await 处挂起，此时门禁已置位
            var saveTask = SaveManager.SaveAsync(1);

            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.DeleteSlotAsync(1),
                "写操作在飞时应拒绝删除，而不是静默把目标文件抽走");

            await saveTask;

            Assert.IsTrue(await SaveManager.SlotExistsAsync(1), "被拒绝的删除不应影响存档");
        }

        [Test]
        public async Task SaveAsync_WhileSaveInFlight_Throws()
        {
            var saveTask = SaveManager.SaveAsync(1);

            await AssertThrowsAsync<InvalidOperationException>(() => SaveManager.SaveAsync(2),
                "上一次写操作未完成时应拒绝新的写操作");

            await saveTask;
        }

        [Test]
        public async Task ReadOperations_WhileSaveInFlight_AreNotBlocked()
        {
            // 读操作刻意不进写门禁：替换是原子的，读只会看到「旧的完整文件」或「新的完整文件」，
            // 永远不会读到半截；给读加门禁只会让存档 UI 在保存进行中莫名抛异常
            var saveTask = SaveManager.SaveAsync(1);

            Assert.IsFalse(await SaveManager.SlotExistsAsync(1), "首次保存尚未完成时槽位还不存在");
            Assert.IsNotNull(await SaveManager.GetSlotMetasAsync(), "读操作应正常返回而非抛异常");
            await SaveManager.GetSlotMetaAsync(1);

            await saveTask;

            Assert.IsTrue(await SaveManager.SlotExistsAsync(1), "保存完成后槽位应存在");
        }

        [Test]
        public void SetCurrentPlayer_InvalidPlayerId_ThrowsWithoutPoisoningContext()
        {
            // 路径穿越注入：playerId 含分隔符或 .. 时应在设置时即被拒绝，防止存档写到域根之外。
            // 校验点前移后不再等到 SaveAsync 才失败——那时快照已生成、脏标记已清空
            Assert.Throws<ArgumentException>(() => SaveManager.SetCurrentPlayer("../../outside"),
                "含路径分隔符的 playerId 应在设置时即被拒绝");
            Assert.IsNull(SaveManager.CurrentPlayerId, "被拒绝的 playerId 不得留下半切换的玩家上下文");
        }

        [Test]
        public void SetCurrentPlayer_SingleDotDot_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveManager.SetCurrentPlayer(".."),
                "playerId 为 .. 时应被拒绝");
            Assert.IsNull(SaveManager.CurrentPlayerId);
        }

        [Test]
        public void SetCurrentPlayer_SingleDot_Throws()
        {
            // "." 能穿过 FileManager 的路径沙箱（它只拦 .. 段）：存档会落到域根从而绕开玩家隔离，
            // 而 DeletePlayer(".") 会删掉域根下的全部存档
            Assert.Throws<ArgumentException>(() => SaveManager.SetCurrentPlayer("."),
                "playerId 为 . 时应被拒绝");
            Assert.IsNull(SaveManager.CurrentPlayerId);
        }

        [Test]
        public void SetCurrentPlayer_ColonInMiddle_Throws()
        {
            // "foo:bar" 的冒号不在索引 1，FileManager 的盘符前缀检查漏过它；
            // 于是在 Windows 上写入抛 NotSupportedException、而 Exists 静默返回 false，
            // 同一个 ID 上两个 API 给出矛盾结论
            Assert.Throws<ArgumentException>(() => SaveManager.SetCurrentPlayer("foo:bar"),
                "含文件系统非法字符的 playerId 应被拒绝");
        }

        [Test]
        public void SetCurrentPlayer_RejectedId_KeepsPreviousPlayer()
        {
            SaveManager.SetCurrentPlayer("Alice");

            Assert.Throws<ArgumentException>(() => SaveManager.SetCurrentPlayer("bad/id"));

            Assert.AreEqual("Alice", SaveManager.CurrentPlayerId,
                "被拒绝的 playerId 不应影响原有的玩家上下文");
        }

        [Test]
        public async Task SaveAsync_NegativeSlot_Throws()
        {
            await AssertThrowsAsync<ArgumentOutOfRangeException>(() => SaveManager.SaveAsync(-1),
                "负数槽位应被拒绝，而不是写出 slot_-1.save");
        }

        [Test]
        public async Task SlotExistsAsync_NegativeSlot_ReturnsFalse()
        {
            // 谓词语义：非法槽位按不存在处理而不抛异常（与其它入口刻意保留的不对称）
            Assert.IsFalse(await SaveManager.SlotExistsAsync(-1), "非法槽位应按不存在处理");
        }

        [Test]
        public async Task DeletePlayerAsync_EmptyPlayerId_Throws()
        {
            // 原先是静默 no-op，调用方无法区分「删掉了 0 个」与「参数给错了」
            await AssertThrowsAsync<ArgumentException>(() => SaveManager.DeletePlayerAsync(string.Empty),
                "空 playerId 应被拒绝而不是静默不删");
        }

        #region 续体线程

        /// <summary>
        /// 线程探针 Block：记录 <see cref="IDataBlock.OnLoad"/> 被回调时的线程 ID。
        /// <para><see cref="DataManager.ApplySnapshot"/> 会回调第三方 Block，而 Unity API 只能在主线程访问，
        /// 因此这里观测到的线程 ID 就是 <see cref="SaveManagerImpl"/> 续体的执行线程。</para>
        /// </summary>
        [Serializable]
        private sealed class ThreadProbeBlock : IDataBlock
        {
            public string BlockName => "ThreadProbe";

            /// <summary>OnLoad 回调时的线程 ID，0 表示尚未回调。</summary>
            public int OnLoadThreadId;

            public int DataVersion => 0;
            public object OnSave() => 0;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) => OnLoadThreadId = Environment.CurrentManagedThreadId;
            public void OnClear() { }
        }

        /// <summary>
        /// 线程探针快照：记录 <see cref="DataSnapshot.CreateMeta"/> 被调用时的线程 ID。
        /// <para>CreateMeta 是第三方扩展存档元数据的官方入口，且在 SaveAsync 的 IO <c>await</c> 之后执行，
        /// 故可用来观测该处续体的线程。</para>
        /// </summary>
        [Serializable]
        private sealed class ThreadProbeSnapshot : DataSnapshot
        {
            public static int CreateMetaThreadId;

            public override SaveMeta CreateMeta()
            {
                CreateMetaThreadId = Environment.CurrentManagedThreadId;
                return base.CreateMeta();
            }
        }

        // 说明:断言的是「回调内的线程 ID == 调用线程 ID」，而不是在测试体内查 PlayerLoopHelper.IsMainThread——
        // 测试方法自身是 async Task，其续体会被 Unity 的 SynchronizationContext 自动弹回主线程，
        // 在测试体内断言无法发现 SaveManagerImpl 内部跑在子线程的问题。

        [Test]
        public async Task SaveAsync_CreateMetaRunsOnCallingThread()
        {
            // Provider 的 IO 用 RunOnThreadPool(configureAwait: false) 完成时停留在子线程，
            // SaveManagerImpl 必须显式切回，否则第三方 CreateMeta 会在线程池线程上触碰 Unity API
            var callingThreadId = Environment.CurrentManagedThreadId;
            var originalFactory = DataSnapshot.Factory;
            DataSnapshot.Factory = () => new ThreadProbeSnapshot();
            try
            {
                await SaveManager.SaveAsync(1);
            }
            finally
            {
                DataSnapshot.Factory = originalFactory;
            }

            Assert.AreEqual(callingThreadId, ThreadProbeSnapshot.CreateMetaThreadId,
                "DataSnapshot.CreateMeta 应在调用线程上执行，而不是线程池线程");
        }

        [Test]
        public async Task LoadAsync_AppliesSnapshotOnCallingThread()
        {
            var probe = DataManager.GetOrCreateBlock<ThreadProbeBlock>();
            await SaveManager.SaveAsync(1);

            var callingThreadId = Environment.CurrentManagedThreadId;
            probe.OnLoadThreadId = 0;
            await SaveManager.LoadAsync(1);

            Assert.AreEqual(callingThreadId, probe.OnLoadThreadId,
                "IDataBlock.OnLoad 应在调用线程上回调，而不是线程池线程");
        }

        #endregion

        /// <summary>
        /// 断言异步操作抛出指定类型异常。
        /// <para>UTF 文档建议避免 <see cref="Assert.ThrowsAsync{T}(Func{Task})"/>：它阻塞主线程等待任务，
        /// 与 UniTask 的主线程恢复语义冲突。改用 try/catch + Fail 显式断言。</para>
        /// </summary>
        private static async Task AssertThrowsAsync<T>(Func<UniTask> action, string message = null) where T : Exception
        {
            try
            {
                await action();
            }
            catch (T)
            {
                return;
            }
            Assert.Fail(message ?? $"Expected {typeof(T).Name} to be thrown.");
        }
    }
}
