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
    /// 显式脏标记测试。
    /// <para>覆盖:标记生命周期、CreateSnapshot 清脏、ApplySnapshot 清脏、RemoveBlock 联动清脏、未注册警告。</para>
    /// </summary>
    [TestFixture]
    public class DirtyTrackingTests
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

        /// <summary>
        /// <see cref="IDataBlock.OnSave"/> 恒返回 null 的块：按既有语义它**不进快照的块列表**。
        /// 用来钉住「脏集合的还原必须发生在空快照早退之前」——写错位置的实现在另一条用例上反倒会绿。
        /// </summary>
        [Serializable]
        private sealed class SaveNothingData : IDataBlock
        {
            public string BlockName => "SaveNothing";
            public int DataVersion => 0;
            public object OnSave() => null;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data)
            {
            }

            public void OnClear()
            {
            }
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
        public void MarkDirty_TracksLifecycle()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            Assert.IsFalse(DataManager.IsDirty<WalletData>(), "初始不应为脏");
            Assert.IsFalse(DataManager.HasDirtyBlocks, "初始不应有脏块");

            DataManager.MarkDirty<WalletData>();

            Assert.IsTrue(DataManager.IsDirty<WalletData>(), "标记后应为脏");
            Assert.IsTrue(DataManager.HasDirtyBlocks, "存在脏块");
            var dirty = DataManager.GetDirtyBlocks();
            Assert.AreEqual(1, dirty.Count, "应恰好一个脏块");
            Assert.AreSame(wallet, dirty[0], "脏块应为被标记的实例");
        }

        [Test]
        public void CreateSnapshot_ClearsDirtyFlags()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 10;
            DataManager.MarkDirty<WalletData>();

            DataManager.CreateSnapshot();

            Assert.IsFalse(DataManager.IsDirty<WalletData>(), "快照导出成功即视为已保存,应清空脏标记");
        }

        [Test]
        public void ApplySnapshot_ClearsAllDirtyFlags()
        {
            DataManager.GetOrCreateBlock<WalletData>();
            DataManager.MarkDirty<WalletData>();

            DataManager.ApplySnapshot(new DataSnapshot());

            Assert.IsFalse(DataManager.HasDirtyBlocks, "恢复即干净,应清空全部脏标记(空快照也成立)");
        }

        /// <summary>
        /// 回滚点必须把「数据」与「脏集合」**一起**还原：留点 → 应用别的快照 → 应用回滚点。
        /// <para>这是 Save 的 <c>TryApplySnapshot</c> 依赖的语义：加载失败回滚之后，玩家在加载前
        /// 的未保存改动必须仍然是「未保存」，否则将来做增量保存会漏掉本该保存的块（数据丢失）。</para>
        /// </summary>
        [Test]
        public void ApplySnapshot_RollbackSnapshot_RestoresDirtyFlags()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 10;
            DataManager.MarkDirty<WalletData>();

            var rollback = DataManager.CreateSnapshot(); // 留回滚点

            wallet.Gold = 0;                             // 模拟「正在加载别的存档」把内存改掉
            DataManager.ApplySnapshot(new DataSnapshot()); // 磁盘读回的快照形态：不携带脏集合
            Assert.IsFalse(DataManager.IsDirty<WalletData>(), "应用不携带脏集合的快照之后应为干净（既有语义）");

            DataManager.ApplySnapshot(rollback);

            Assert.AreEqual(10, wallet.Gold, "回滚应恢复数据");
            Assert.IsTrue(DataManager.IsDirty<WalletData>(),
                "回滚必须把脏集合一并还原，否则增量保存会漏块");
        }

        /// <summary>
        /// 与上一条互补：块列表为空的回滚点（该类型的 <c>OnSave</c> 返回 null）也必须还原脏集合——
        /// 钉住「还原发生在空快照早退之前」这个位置约束。
        /// </summary>
        [Test]
        public void ApplySnapshot_RollbackSnapshotWithNoBlocks_StillRestoresDirtyFlags()
        {
            DataManager.GetOrCreateBlock<SaveNothingData>();
            DataManager.MarkDirty<SaveNothingData>();

            var rollback = DataManager.CreateSnapshot();
            Assert.AreEqual(0, rollback.blocks.Count, "前提：OnSave 返回 null 的块不进快照的块列表");

            DataManager.ApplySnapshot(new DataSnapshot());
            Assert.IsFalse(DataManager.HasDirtyBlocks, "前提：先被清空");

            DataManager.ApplySnapshot(rollback);

            Assert.IsTrue(DataManager.IsDirty<SaveNothingData>(),
                "块列表为空的回滚点也必须还原脏集合——还原要发生在空快照早退之前");
        }

        /// <summary>
        /// 不变量锁（**不是红基线**——改前改后都绿，靠负向控制证明有判别力）：
        /// 脏集合是进程内回滚元数据，依赖「默认序列化器只写 public 成员」这条既有契约，
        /// **不得进入存档字节**。把字段改成 <c>public</c> 或换用会写 non-public 成员的序列化器即红。
        /// </summary>
        [Test]
        public void SnapshotDirtyCarry_DoesNotEnterSerializedBytes()
        {
            var wallet = DataManager.GetOrCreateBlock<WalletData>();
            wallet.Gold = 10;
            DataManager.MarkDirty<WalletData>();

            var snapshot = DataManager.CreateSnapshot(); // 此刻携带 {"Wallet"}
            var bytes = Serializer.Default.Serialize(snapshot, snapshot.GetType());
            var json = System.Text.Encoding.UTF8.GetString(bytes);

            StringAssert.DoesNotContain("dirtyBlockNames", json,
                "脏集合是进程内回滚元数据，不得进入存档（字段必须保持 non-public）");

            var fromDisk = (DataSnapshot)Serializer.Default.Deserialize(bytes, typeof(DataSnapshot));
            DataManager.MarkDirty<WalletData>(); // 先弄脏，再验证「磁盘快照不还原」
            DataManager.ApplySnapshot(fromDisk);

            Assert.IsFalse(DataManager.HasDirtyBlocks,
                "从磁盘读回的快照不携带脏信息 → 应用即干净（既有语义不变）");
        }

        [Test]
        public void RemoveBlock_ClearsDirtyFlag()
        {
            DataManager.GetOrCreateBlock<WalletData>();
            DataManager.MarkDirty<WalletData>();

            DataManager.RemoveBlock<WalletData>();

            Assert.IsFalse(DataManager.HasDirtyBlocks, "移除 Block 应联动清理其脏标记");
        }

        [Test]
        public void MarkDirty_UnregisteredBlock_WarnsAndIgnores()
        {
            LogAssert.Expect(LogType.Warning, new Regex("未注册"));
            DataManager.MarkDirty<WalletData>();

            Assert.IsFalse(DataManager.HasDirtyBlocks, "未注册的标记应被忽略");
        }
    }
}
