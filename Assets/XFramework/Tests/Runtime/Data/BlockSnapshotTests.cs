using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XData;
using XFramework.XSerialize;

namespace XFramework.XData.Tests
{
    /// <summary>
    /// 单 Block 快照测试(增量保存)。
    /// <para>覆盖:单块往返不影响其他块、未注册返回 null、OnSave null 返回 null、未知 blockName 拒绝、
    /// 旧版本快照走迁移、恢复成功只清目标块脏标记。</para>
    /// </summary>
    [TestFixture]
    public class BlockSnapshotTests
    {
        /// <summary>
        /// 带版本控制的 Block:OnSave 返回 Gold(int);0→1 迁移语义为「分」→「元」。
        /// </summary>
        [Serializable]
        private sealed class WalletData : IDataBlock
        {
            public string BlockName => "Wallet";
            public int Gold;
            public int CurrentVersion;
            public int DataVersion => CurrentVersion;
            public object OnSave() => Gold;
            public object OnMigrate(object saveData, int fromVersion)
            {
                // 旧存档(v0)单位是「分」,迁移到「元」
                if (saveData is int cents)
                    return cents / 10;
                return saveData;
            }
            public void OnLoad(object data) { if (data is int i) Gold = i; }
            public void OnClear() => Gold = 0;
        }

        [Serializable]
        private sealed class QuestData : IDataBlock
        {
            public string BlockName => "Quest";
            public int Progress;
            public int DataVersion => 0;
            public object OnSave() => Progress;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { if (data is int i) Progress = i; }
            public void OnClear() => Progress = 0;
        }

        /// <summary>OnSave 返回 null 的 Block,不参与快照。</summary>
        [Serializable]
        private sealed class NullData : IDataBlock
        {
            public string BlockName => "Null";
            public int DataVersion => 0;
            public object OnSave() => null;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { }
            public void OnClear() { }
        }

        [SetUp]
        public void SetUp()
        {
            Serializer.Initialize();
            DataManager.Initialize(new DataManagerImpl());
        }

        [TearDown]
        public void TearDown()
        {
            DataManager.Shutdown();
        }

        [Test]
        public void CreateApply_RoundTrip_RestoresOnlyTargetBlock()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            var quest = DataManager.GetOrCreateBlock<QuestData>();
            wallet.Gold = 100;
            quest.Progress = 7;

            var snap = DataManager.CreateBlockSnapshot<WalletData>();

            // 快照后改动两个块的内存数据
            wallet.Gold = 999;
            quest.Progress = 999;

            Assert.IsTrue(DataManager.ApplyBlockSnapshot(snap));

            Assert.AreEqual(100, wallet.Gold, "目标块应恢复");
            Assert.AreEqual(999, quest.Progress, "其他块不应受影响");
        }

        [Test]
        public void CreateBlockSnapshot_Unregistered_ReturnsNullWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("未注册"));
            Assert.IsNull(DataManager.CreateBlockSnapshot<WalletData>(), "未注册的块应返回 null");
        }

        [Test]
        public void CreateBlockSnapshot_OnSaveNull_ReturnsNull()
        {
            DataManager.GetOrCreateBlock<NullData>();
            Assert.IsNull(DataManager.CreateBlockSnapshot<NullData>(), "OnSave 返回 null 的块应返回 null(不警告)");
        }

        [Test]
        public void ApplyBlockSnapshot_UnknownBlockName_ReturnsFalse()
        {
            var snap = new DataBlockSnapshot { blockName = "Unknown", data = "" };

            LogAssert.Expect(LogType.Warning, new Regex("未注册的数据块"));
            Assert.IsFalse(DataManager.ApplyBlockSnapshot(snap), "未知 blockName 应返回 false");

            Assert.IsFalse(DataManager.HasBlock<WalletData>(), "未知块不应被创建实例");
        }

        [Test]
        public void ApplyBlockSnapshot_OldVersionSnapshot_RunsMigration()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.CurrentVersion = 1;

            // 模拟旧存档(v0):单位是「分」
            var raw = Serializer.Default.Serialize(500, typeof(int));
            var oldSnap = new DataBlockSnapshot
            {
                blockName = "Wallet",
                version = 0,
                saveType = typeof(int).AssemblyQualifiedName,
                data = Convert.ToBase64String(raw),
            };

            Assert.IsTrue(DataManager.ApplyBlockSnapshot(oldSnap), "旧版本快照应恢复成功");
            Assert.AreEqual(50, wallet.Gold, "0→1 迁移(分→元)应生效");
        }

        [Test]
        public void ApplyBlockSnapshot_Success_ClearsOnlyTargetDirty()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            var quest = DataManager.GetOrCreateBlock<QuestData>();
            DataManager.MarkDirty<WalletData>();
            DataManager.MarkDirty<QuestData>();

            var snap = DataManager.CreateBlockSnapshot<WalletData>();
            Assert.IsTrue(DataManager.ApplyBlockSnapshot(snap));

            Assert.IsFalse(DataManager.IsDirty<WalletData>(), "恢复成功的目标块应清除脏标记");
            Assert.IsTrue(DataManager.IsDirty<QuestData>(), "其他块脏标记应保留");
        }

        [Test]
        public void ApplyBlockSnapshot_EmptyData_FailsWithWarning()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 10;
            DataManager.MarkDirty<WalletData>();

            // 空字节 → 反序列化结果为 null：不得把 null 传进 OnLoad，应失败并打警告
            var snap = new DataBlockSnapshot
            {
                blockName = "Wallet",
                data = Convert.ToBase64String(Array.Empty<byte>()),
            };

            LogAssert.Expect(LogType.Warning, new Regex("反序列化结果为空"));
            Assert.IsFalse(DataManager.ApplyBlockSnapshot(snap), "空数据快照应恢复失败");

            Assert.AreEqual(0, wallet.Gold, "恢复前已 OnClear 清空目标块");
            Assert.IsTrue(DataManager.IsDirty<WalletData>(), "失败应保留脏标记(可能半恢复,保守标记)");
        }
    }
}
