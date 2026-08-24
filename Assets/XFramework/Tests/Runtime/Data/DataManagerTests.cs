using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XData;
using XFramework.XSerialize;

namespace XFramework.XData.Tests
{
    /// <summary>
    /// DataManager 快照序列化/恢复测试。
    /// <para>覆盖:私有 SaveSnap 模式恢复(saveType 修复)、恢复前清空语义、旧存档 saveType 回退兼容、空快照清空。</para>
    /// </summary>
    [TestFixture]
    public class DataManagerTests
    {
        [Serializable]
        private sealed class BagItem
        {
            public int id;
            public string name;
            public int count;
        }

        /// <summary>
        /// 按 README 推荐模式定义:OnSave 返回私有嵌套 struct,与 Block 自身类型不同。
        /// <para>用于验证 saveType 按原类型反序列化后,OnLoad 中的 <c>is</c> 判断能命中(修复前静默失效)。</para>
        /// </summary>
        [Serializable]
        private sealed class BagData : IDataBlock
        {
            public string BlockName => "Bag";

            public List<BagItem> Items = new();
            public int Gold;

            [Serializable]
            private struct SaveSnap
            {
                public List<BagItem> items;
                public int gold;
            }

            public object OnSave() => new SaveSnap { items = Items, gold = Gold };

            public void OnLoad(object data)
            {
                if (data is SaveSnap s)
                {
                    Items = s.items ?? new List<BagItem>();
                    Gold = s.gold;
                }
            }

            public void OnClear()
            {
                Items.Clear();
                Gold = 0;
            }
        }

        /// <summary>
        /// OnSave 返回 null 的 Block,不参与快照;用于验证快照外 Block 的清空语义与注册保留。
        /// </summary>
        [Serializable]
        private sealed class QuestData : IDataBlock
        {
            public string BlockName => "Quest";
            public int Progress;

            public object OnSave() => null;
            public void OnLoad(object data) { }
            public void OnClear() => Progress = 0;
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
        public void ApplySnapshot_RestoresPrivateStructSaveData()
        {
            var bag = DataManager.GetOrCreateBlock<BagData>();
            bag.Items.Add(new BagItem { id = 1001, name = "铁剑", count = 1 });
            bag.Gold = 100;

            var snapshot = DataManager.CreateSnapshot();

            // 快照后改动内存数据,验证恢复
            bag.Items.Clear();
            bag.Gold = 999;

            DataManager.ApplySnapshot(snapshot);

            Assert.AreEqual(100, bag.Gold, "快照中的 Gold 应恢复");
            Assert.AreEqual(1, bag.Items.Count, "快照中的 Items 应恢复");
            Assert.AreEqual(1001, bag.Items[0].id);
            Assert.AreEqual("铁剑", bag.Items[0].name);
        }

        [Test]
        public void ApplySnapshot_ClearsBlocksNotInSnapshot_KeepsRegistration()
        {
            DataManager.GetOrCreateBlock<BagData>();
            var quest = DataManager.GetOrCreateBlock<QuestData>();
            quest.Progress = 5;

            // QuestData.OnSave 返回 null,不参与快照
            var snapshot = DataManager.CreateSnapshot();

            DataManager.ApplySnapshot(snapshot);

            Assert.AreEqual(0, quest.Progress, "快照外 Block 的数据应被清空");
            Assert.IsTrue(DataManager.HasBlock<QuestData>(), "清空不应注销注册");
        }

        [Test]
        public void ApplySnapshot_PreservesBlockIdentity()
        {
            var bag = DataManager.GetOrCreateBlock<BagData>();
            DataManager.ApplySnapshot(DataManager.CreateSnapshot());
            Assert.AreSame(bag, DataManager.GetOrCreateBlock<BagData>(), "恢复不应破坏名称索引/注册");
        }

        [Test]
        public void ApplySnapshot_MissingSaveType_FallsBackToBlockType()
        {
            // 旧存档无 saveType 字段:回退使用 Block 自身类型反序列化。
            // 此用法要求 OnSave 返回类型与 Block 类型一致才可恢复;
            // 本 Block 返回私有 struct,恢复命中失败属预期,重点验证不抛异常且输出回退警告。
            var bag = DataManager.GetOrCreateBlock<BagData>();
            var raw = Convert.ToBase64String(
                new JsonSerializer().Serialize(new BagData { Gold = 7 }, typeof(BagData)));

            var oldSnapshot = new DataSnapshot();
            oldSnapshot.blocks.Add(new DataBlockSnapshot { blockName = "Bag", data = raw });

            LogAssert.Expect(LogType.Warning, new Regex("saveType 无法解析"));
            DataManager.ApplySnapshot(oldSnapshot);
        }

        [Test]
        public void ApplySnapshot_EmptySnapshot_ClearsAllBlocks()
        {
            var bag = DataManager.GetOrCreateBlock<BagData>();
            bag.Gold = 10;

            DataManager.ApplySnapshot(new DataSnapshot());

            Assert.AreEqual(0, bag.Gold, "空快照也应清空所有已注册 Block 的数据");
        }
    }
}
