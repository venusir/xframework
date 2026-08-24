using System;
using NUnit.Framework;
using XFramework.XData;
using XFramework.XSerialize;

namespace XFramework.XData.Tests
{
    /// <summary>
    /// RegisterBlock 覆盖同名 Block 时的清理行为测试。
    /// <para>覆盖:旧实例 OnClear 被调用、旧实例脏标记被清除。</para>
    /// </summary>
    [TestFixture]
    public class RegisterBlockTests
    {
        [Serializable]
        private sealed class WalletData : IDataBlock
        {
            public string BlockName => "Wallet";
            public int Gold;
            public int ClearCount;
            public int DataVersion => 0;
            public object OnSave() => Gold;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { if (data is int i) Gold = i; }
            public void OnClear() { Gold = 0; ClearCount++; }
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
        public void RegisterBlock_Overwrite_CallsOldOnClear()
        {
            var old = new WalletData { Gold = 10 };
            DataManager.RegisterBlock(old);

            var replacement = new WalletData { Gold = 20 };
            DataManager.RegisterBlock(replacement);

            Assert.AreEqual(1, old.ClearCount, "旧实例应被 OnClear 清理一次");
            Assert.AreEqual(0, old.Gold, "旧实例数据应被清空");
            Assert.AreSame(replacement, DataManager.GetOrCreateBlock<WalletData>(), "注册应指向新实例");
        }

        [Test]
        public void RegisterBlock_Overwrite_ClearsOldDirtyFlag()
        {
            var old = new WalletData();
            DataManager.RegisterBlock(old);
            DataManager.MarkDirty<WalletData>();
            Assert.IsTrue(DataManager.HasDirtyBlocks, "前置:旧实例已标记为脏");

            DataManager.RegisterBlock(new WalletData()); // 覆盖:旧实例脏标记应被清除

            Assert.IsFalse(DataManager.HasDirtyBlocks, "覆盖后旧实例的脏标记不应残留");
        }
    }
}
