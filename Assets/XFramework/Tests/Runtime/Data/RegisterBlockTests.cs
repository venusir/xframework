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

        /// <summary>
        /// 与 <see cref="WalletData"/> **同名但类型不同**的块——用于撞名用例。
        /// 两个互不相关的作者取同一个名字是这个模块最容易踩的静默陷阱（见下条用例）。
        /// </summary>
        [Serializable]
        private sealed class WalletAliasData : IDataBlock
        {
            public string BlockName => "Wallet";
            public int DataVersion => 0;
            public object OnSave() => 0;
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

        [Test]
        public void RegisterBlock_Null_Throws_AndDoesNotCorruptRegistry()
        {
            // 原先：null 会先被写进 _blocks、再在取 BlockName 时抛 NRE——容器里留下一个 null 条目，
            // 此后 HasBlock 返回 true 而 TryGetBlock 给出 null、遍历时炸
            Assert.Throws<ArgumentNullException>(() => DataManager.RegisterBlock<WalletData>(null));

            Assert.IsFalse(DataManager.HasBlock<WalletData>(), "参数校验失败不应在注册表里留下任何条目");
            Assert.DoesNotThrow(() => DataManager.GetOrCreateBlock<WalletData>(), "注册表应仍然可用");
        }

        [Test]
        public void RegisterBlock_NameCollision_LogsError()
        {
            // 两个**不同类型**的块返回同一个 BlockName：名称索引只能指向一个实例，
            // 先注册的那个按名恢复时永远命中不了（静默丢数据）。必须留痕。
            DataManager.RegisterBlock(new WalletData());

            LogAssert.Expect(LogType.Error, new Regex(@"\[Data\] 数据块名称冲突"));
            DataManager.RegisterBlock(new WalletAliasData());
        }
    }
}
