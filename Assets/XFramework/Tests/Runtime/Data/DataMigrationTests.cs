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
    /// 每 Block 独立版本迁移测试。
    /// <para>覆盖:旧档无 version 触发迁移、多步迁移链顺序、快照写入版本、未来版本跳过、迁移异常隔离。</para>
    /// </summary>
    [TestFixture]
    public class DataMigrationTests
    {
        /// <summary>
        /// 版本可控的 Block:OnMigrate 将 value 逐版本 ×10,并用 MigrationCalls 记录迁移调用顺序。
        /// </summary>
        [Serializable]
        private sealed class VersionedData : IDataBlock
        {
            public string BlockName => "Versioned";
            public int Value;
            public int CurrentVersion = 1;
            public readonly List<int> MigrationCalls = new();

            public int DataVersion => CurrentVersion;

            [Serializable]
            private struct SaveSnap
            {
                public int value;
            }

            public object OnSave() => new SaveSnap { value = Value };

            public object OnMigrate(object saveData, int fromVersion)
            {
                MigrationCalls.Add(fromVersion);
                // 值类型快照:拆箱副本迁移后重新装箱返回
                if (saveData is SaveSnap s)
                {
                    s.value *= 10;
                    return s;
                }
                return saveData;
            }

            public void OnLoad(object data)
            {
                if (data is SaveSnap s)
                    Value = s.value;
            }

            public void OnClear() => Value = 0;
        }

        /// <summary>
        /// 迁移抛异常的 Block,用于验证迁移失败不影响其他块。
        /// </summary>
        [Serializable]
        private sealed class ThrowingData : IDataBlock
        {
            public string BlockName => "Throwing";
            public int Value;
            public int DataVersion => 1;
            public object OnSave() => Value;
            public object OnMigrate(object saveData, int fromVersion) => throw new InvalidOperationException("迁移失败模拟");
            public void OnLoad(object data) { if (data is int i) Value = i; }
            public void OnClear() => Value = 0;
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

        /// <summary>
        /// 构造单个 Block 快照条目(手动指定版本,用于模拟旧/未来存档)。
        /// <para>按真实写档格式写入 saveType:迁移测试聚焦版本机制,
        /// saveType 缺失的回退语义(要求 OnSave 返回类型与 Block 一致)由 DataManagerTests 的旧档回退用例覆盖。</para>
        /// </summary>
        private static DataBlockSnapshot BuildBlockSnap(string blockName, int version, object saveObj)
        {
            var raw = Serializer.Default.Serialize(saveObj, saveObj.GetType());
            return new DataBlockSnapshot
            {
                blockName = blockName,
                version = version,
                saveType = saveObj.GetType().AssemblyQualifiedName,
                data = Convert.ToBase64String(raw),
            };
        }

        [Test]
        public void Migrate_OldSaveNoVersion_RunsSingleMigration()
        {
            // 旧存档无 version 字段:反序列化得 0,自动进入迁移链执行 0→1
            var block = DataManager.GetOrCreateBlock<VersionedData>();
            block.CurrentVersion = 1;

            var snapshot = new DataSnapshot();
            snapshot.blocks.Add(BuildBlockSnap("Versioned", 0, new VersionedData { Value = 5 }.OnSave()));
            DataManager.ApplySnapshot(snapshot);

            Assert.AreEqual(50, block.Value, "0→1 迁移应将 value ×10");
            CollectionAssert.AreEqual(new[] { 0 }, block.MigrationCalls, "应恰好执行一次 fromVersion=0 的迁移");
        }

        [Test]
        public void Migrate_MultiStep_ChainInOrder()
        {
            // 快照 version=0,当前版本 3:迁移链按 0→1→2 顺序执行三次
            var block = DataManager.GetOrCreateBlock<VersionedData>();
            block.CurrentVersion = 3;

            var snapshot = new DataSnapshot();
            snapshot.blocks.Add(BuildBlockSnap("Versioned", 0, new VersionedData { Value = 5 }.OnSave()));
            DataManager.ApplySnapshot(snapshot);

            Assert.AreEqual(5000, block.Value, "三次 ×10 迁移应累积生效");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, block.MigrationCalls, "迁移链应按版本顺序逐级调用");
        }

        [Test]
        public void CreateSnapshot_WritesBlockDataVersion()
        {
            var block = DataManager.GetOrCreateBlock<VersionedData>();
            block.CurrentVersion = 2;

            var snapshot = DataManager.CreateSnapshot();

            Assert.AreEqual(2, snapshot.blocks[0].version, "快照应写入写档时的 DataVersion");
        }

        [Test]
        public void Migrate_FutureVersion_SkipsBlockWithWarning()
        {
            // 先以 v3 写档,再模拟代码回滚到 v1:快照版本高于当前代码版本,跳过该块防止污染
            var block = DataManager.GetOrCreateBlock<VersionedData>();
            block.CurrentVersion = 3;
            block.Value = 42;
            var snapshot = DataManager.CreateSnapshot();

            block.CurrentVersion = 1;
            LogAssert.Expect(LogType.Warning, new Regex("存档版本.*高于当前代码版本"));
            DataManager.ApplySnapshot(snapshot);

            Assert.AreEqual(0, block.Value, "未来版本快照应被跳过,Block 保持清空状态");
            Assert.IsEmpty(block.MigrationCalls, "跳过时不应执行任何迁移");
        }

        [Test]
        public void Migrate_OnMigrateThrows_BlockEmptyOthersUnaffected()
        {
            var good = DataManager.GetOrCreateBlock<VersionedData>();
            var bad = DataManager.GetOrCreateBlock<ThrowingData>();

            var snapshot = new DataSnapshot();
            snapshot.blocks.Add(BuildBlockSnap("Versioned", 0, new VersionedData { Value = 7 }.OnSave()));
            snapshot.blocks.Add(BuildBlockSnap("Throwing", 0, new ThrowingData { Value = 9 }.OnSave()));

            LogAssert.Expect(LogType.Warning, new Regex("恢复数据块 Throwing 失败"));
            DataManager.ApplySnapshot(snapshot);

            Assert.AreEqual(70, good.Value, "正常块应完成迁移并恢复");
            Assert.AreEqual(0, bad.Value, "迁移异常块应保持清空,不影响其他块");
        }
    }
}
