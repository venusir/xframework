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
