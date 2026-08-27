using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
        }

        [Test]
        public async Task SaveAsync_ThenSlotExists()
        {
            await SaveManager.SaveAsync(1);

            Assert.IsTrue(SaveManager.SlotExists(1), "保存后槽位应存在");
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
        public async Task DeleteSlot_RemovesFile()
        {
            await SaveManager.SaveAsync(1);

            SaveManager.DeleteSlot(1);

            Assert.IsFalse(SaveManager.SlotExists(1), "删除后槽位不应存在");
        }

        [Test]
        public async Task DeleteAllSlots_RemovesAllIncludingTmp()
        {
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(2);
            // 手动制造残留 .tmp 文件（模拟写入中途崩溃后遗留）
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_9.save.tmp", Encoding.UTF8.GetBytes("partial"));

            await SaveManager.DeleteAllSlots();

            Assert.IsFalse(SaveManager.SlotExists(1));
            Assert.IsFalse(SaveManager.SlotExists(2));
            Assert.IsFalse(FileManager.Exists(FileDomain.SaveData, "slot_9.save.tmp"), "残留 tmp 文件应被清理");
        }

        [Test]
        public async Task GetSlotMetas_ReturnsAllMetas()
        {
            await SaveManager.SaveAsync(1);
            await SaveManager.SaveAsync(3);

            var metas = await SaveManager.GetSlotMetas();

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
            var meta = await SaveManager.GetSlotMeta(1);

            Assert.IsNull(meta, "不存在的槽位应返回 null");
        }

        [Test]
        public async Task GetSlotMetas_CorruptedFile_SkipsWithWarning()
        {
            await SaveManager.SaveAsync(1);
            // 直接写入损坏文件（不走 SaveAsync）
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "slot_2.save", Encoding.UTF8.GetBytes("corrupted"));

            LogAssert.Expect(LogType.Warning, new Regex("解析存档元数据失败"));
            var metas = await SaveManager.GetSlotMetas();

            Assert.AreEqual(1, metas.Count, "损坏文件应被跳过且不打崩列表");
            Assert.AreEqual(1, metas[0].slot);
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
            Assert.IsFalse(SaveManager.SlotExists(1), "根目录槽位不应存在");

            SaveManager.SetCurrentPlayer("Alice");
            await SaveManager.LoadAsync(1);

            Assert.AreEqual(10, wallet.Gold, "应恢复 Alice 玩家子目录下的独立存档");
        }

        [Test]
        public async Task SaveAsync_InvalidPlayerId_Throws()
        {
            // 路径穿越注入:playerId 含分隔符或 .. 时应拒绝,防止存档写到域根之外
            SaveManager.SetCurrentPlayer("../../outside");

            await AssertThrowsAsync<ArgumentException>(() => SaveManager.SaveAsync(1),
                "含路径分隔符的 playerId 应抛出 ArgumentException");
        }

        [Test]
        public async Task SaveAsync_PlayerIdSingleDotDot_Throws()
        {
            SaveManager.SetCurrentPlayer("..");

            await AssertThrowsAsync<ArgumentException>(() => SaveManager.SaveAsync(1),
                "playerId 为 .. 时应抛出 ArgumentException");
        }

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
