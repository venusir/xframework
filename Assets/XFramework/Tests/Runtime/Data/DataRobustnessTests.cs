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
    /// Data 的失败路径与身份语义（第二轮审计 P2 代码项）：
    /// 脏集合按引用判等（DATA-004）、同名遮蔽者移除不误删索引（DATA-005）、
    /// 清空阶段逐块隔离（DATA-007）、OnSave 内增删块不打断快照（DATA-008）、
    /// 快照里的 null 条目计数而不 NRE（DATA-010）。
    /// </summary>
    [TestFixture]
    public class DataRobustnessTests
    {
        #region Test Doubles

        /// <summary>重写 Equals/GetHashCode 且与另一个类型互相「相等」的块——专打默认比较器的塌缩。</summary>
        private sealed class EqualityBlockA : IDataBlock
        {
            public string BlockName => "EqualityA";
            public int DataVersion => 0;
            public object OnSave() => 0;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { }
            public void OnClear() { }

            public override bool Equals(object obj) => obj is IDataBlock;
            public override int GetHashCode() => 0;
        }

        /// <inheritdoc cref="EqualityBlockA"/>
        private sealed class EqualityBlockB : IDataBlock
        {
            public string BlockName => "EqualityB";
            public int DataVersion => 0;
            public object OnSave() => 0;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { }
            public void OnClear() { }

            public override bool Equals(object obj) => obj is IDataBlock;
            public override int GetHashCode() => 0;
        }

        /// <summary>同名但**类型不同**的两个块：后登记者覆盖索引，先登记者成为被遮蔽者。</summary>
        private sealed class NameBlockA : IDataBlock
        {
            public string BlockName => "Same";
            public int DataVersion => 0;
            public int Loaded;
            public object OnSave() => 0;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { if (data is int i) Loaded = i; }
            public void OnClear() { }
        }

        /// <inheritdoc cref="NameBlockA"/>
        private sealed class NameBlockB : IDataBlock
        {
            public string BlockName => "Same";
            public int DataVersion => 0;
            public int Loaded;
            public object OnSave() => 0;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { if (data is int i) Loaded = i; }
            public void OnClear() { }
        }

        /// <summary>记录 Serialize 调用次数的自定义格式——验证写入端按快照 defaultFormat 取序列化器（DATA-006）。</summary>
        private sealed class RecordingSerializer : ISerializer
        {
            public const string FormatName = "data-probe-format";

            public int SerializeCalls;
            public string Format => FormatName;

            public byte[] Serialize(object obj, Type type)
            {
                SerializeCalls++;
                return new byte[] { 1, 2, 3 };
            }

            public object Deserialize(byte[] data, Type type) => null;
        }

        private sealed class ThrowingClearBlock : IDataBlock
        {
            public string BlockName => "ThrowClear";
            public int DataVersion => 0;
            public object OnSave() => 0;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { }
            public void OnClear() => throw new InvalidOperationException("boom");
        }

        private sealed class TrackingClearBlock : IDataBlock
        {
            public bool Cleared;
            public string BlockName => "TrackingClear";
            public int DataVersion => 0;
            public object OnSave() => 0;
            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { }
            public void OnClear() => Cleared = true;
        }

        /// <summary>OnSave 里注册另一个块——制造「快照遍历中改字典」。</summary>
        private sealed class MutatingSaveBlock : IDataBlock
        {
            public string BlockName => "Mutator";
            public int DataVersion => 0;
            public object OnSave()
            {
                DataManager.RegisterBlock(new TrackingClearBlock());
                return 1;
            }

            public object OnMigrate(object saveData, int fromVersion) => saveData;
            public void OnLoad(object data) { }
            public void OnClear() { }
        }

        #endregion

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
        public void DirtyBlocks_UseReferenceEquality_NotValueEquality()
        {
            DataManager.RegisterBlock(new EqualityBlockA());
            DataManager.RegisterBlock(new EqualityBlockB());
            DataManager.MarkDirty<EqualityBlockA>();
            DataManager.MarkDirty<EqualityBlockB>();

            var dirty = DataManager.GetDirtyBlocks();

            Assert.AreEqual(2, dirty.Count,
                "重写 Equals 的不同块不得被塌成一条——默认比较器会让 GetDirtyBlocks 漏块（DATA-004）");
        }

        [Test]
        public void RemoveShadowedBlock_DoesNotRemoveOccupantsNameIndex()
        {
            DataManager.RegisterBlock(new NameBlockA());
            var occupant = new NameBlockB();

            // 后登记者同名 → 一条名称冲突 Error（保留它只告警不阻止的既有语义）
            LogAssert.Expect(LogType.Error, new Regex("数据块名称冲突"));
            DataManager.RegisterBlock(occupant);

            // 移除**被遮蔽者**：不得把占用者的名称索引一并删掉（DATA-005）
            Assert.IsTrue(DataManager.RemoveBlock<NameBlockA>());

            var snapshot = new DataSnapshot
            {
                defaultFormat = "json",
                blocks = new List<DataBlockSnapshot>
                {
                    new DataBlockSnapshot
                    {
                        blockName = "Same",
                        saveType = typeof(int).AssemblyQualifiedName,
                        version = 0,
                        data = Convert.ToBase64String(
                            Serializer.Default.Serialize(42, typeof(int))),
                    },
                },
            };

            Assert.AreEqual(0, DataManager.ApplySnapshot(snapshot),
                "名称索引应仍指向占用者；被遮蔽者移除不得误删它");
            Assert.AreEqual(42, occupant.Loaded, "按名恢复应命中占用者");
        }

        [Test]
        public void ApplySnapshot_NullBlockEntry_CountsAsFailureWithoutThrowing()
        {
            var snapshot = new DataSnapshot
            {
                defaultFormat = "json",
                blocks = new List<DataBlockSnapshot> { null },
            };

            int failed = -1;
            Assert.DoesNotThrow(() => failed = DataManager.ApplySnapshot(snapshot),
                "null 条目应计为失败并跳过，而不是 NRE（DATA-010）");
            Assert.AreEqual(1, failed);
        }

        [Test]
        public void ApplySnapshot_OnClearThrows_IsolatesAndCounts()
        {
            DataManager.RegisterBlock(new ThrowingClearBlock());
            var good = new TrackingClearBlock();
            DataManager.RegisterBlock(good);

            var snapshot = new DataSnapshot
            {
                defaultFormat = "json",
                blocks = new List<DataBlockSnapshot>(),
            };

            LogAssert.Expect(LogType.Error, new Regex("OnClear 抛出异常"));
            int failed = -1;
            Assert.DoesNotThrow(() => failed = DataManager.ApplySnapshot(snapshot),
                "第三方的 OnClear 异常不应中止整次恢复（DATA-007）");
            Assert.AreEqual(1, failed, "清空失败应计入返回值");
            Assert.IsTrue(good.Cleared, "抛异常块之后的块仍须被清空");
        }

        [Test]
        public void CreateSnapshot_OnSaveMutatesBlocks_DoesNotThrow()
        {
            DataManager.RegisterBlock(new MutatingSaveBlock());

            Assert.DoesNotThrow(() => DataManager.CreateSnapshot(),
                "OnSave 里注册/移除块不应让快照遍历失效（DATA-008）");
        }

        [Test]
        public void CreateSnapshot_UsesSnapshotsDefaultFormat()
        {
            var serializer = new RecordingSerializer();
            Serializer.Register(serializer);
            DataSnapshot.Factory = () => new DataSnapshot { defaultFormat = RecordingSerializer.FormatName };

            try
            {
                DataManager.RegisterBlock(new NameBlockA());

                DataManager.CreateSnapshot();

                Assert.AreEqual(1, serializer.SerializeCalls,
                    "写入端必须使用快照的 defaultFormat，而不是恒用 Serializer.Default（DATA-006）");
            }
            finally
            {
                DataSnapshot.Factory = () => new DataSnapshot();
                Serializer.Unregister(RecordingSerializer.FormatName);
            }
        }
    }
}
