using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using NUnit.Framework;
using XFramework.XConfig;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// <see cref="LubanConfigBridge"/> 的零拷贝 / 复制 / 索引重建与卸载用例。
    /// </summary>
    [TestFixture]
    public sealed class LubanConfigBridgeTests
    {
        [SetUp]
        public void SetUp()
        {
            LubanConfig.Destroy();
            ConfigManager.Destroy();
            ConfigManager.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            LubanConfig.Destroy();
            ConfigManager.Destroy();
        }

        [Test]
        public void RegisterTable_Dictionary_ReusesSameInstance()
        {
            var source = new Dictionary<int, FakeRow>
            {
                [1] = new FakeRow(1),
            };

            LubanConfigBridge.RegisterTable<FakeRow, int>(source);

            var table = ConfigManager.GetTable<FakeRow>();
            Assert.That(((IConfigTable)table).Data, Is.SameAs(source));
            Assert.That(table.Get(1).Id, Is.EqualTo(1));
        }

        [Test]
        public void RegisterTable_ReadOnlyDictionary_Copies()
        {
            var source = new Dictionary<int, FakeRow>
            {
                [1] = new FakeRow(1),
            };
            var readOnly = new ReadOnlyDictionary<int, FakeRow>(source);

            LubanConfigBridge.RegisterTable<FakeRow, int>(readOnly);

            var table = ConfigManager.GetTable<FakeRow>();
            Assert.That(((IConfigTable)table).Data, Is.Not.SameAs(source));
            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.Get(1).Id, Is.EqualTo(1));
        }

        [Test]
        public void RegisterTable_IndexedList_RebuildsDictionary()
        {
            var rows = new List<FakeRow>
            {
                new FakeRow(1),
                new FakeRow(2),
            };

            LubanConfigBridge.RegisterTable<FakeRow, int>(rows, row => row.Id);

            var table = ConfigManager.GetTable<FakeRow>();
            Assert.That(table.Count, Is.EqualTo(2));
            Assert.That(table.Get(2).Id, Is.EqualTo(2));
        }

        [Test]
        public void RegisterTable_IndexedList_DuplicateKey_Throws()
        {
            var rows = new List<FakeRow>
            {
                new FakeRow(1),
                new FakeRow(1),
            };

            var exception = Assert.Throws<ArgumentException>(
                () => LubanConfigBridge.RegisterTable<FakeRow, int>(rows, row => row.Id));

            Assert.That(exception.Message, Does.Contain(nameof(FakeRow)));
            Assert.That(exception.Message, Does.Contain("1"));
        }

        [Test]
        public void RegisterTable_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(
                () => LubanConfigBridge.RegisterTable<FakeRow, int>(null));
            Assert.Throws<ArgumentNullException>(
                () => LubanConfigBridge.RegisterTable<FakeRow, int>(null, row => row.Id));
            Assert.Throws<ArgumentNullException>(
                () => LubanConfigBridge.RegisterTable<FakeRow, int>(
                    new List<FakeRow>(), null));
        }

        [Test]
        public void RegisterGlobal_AndUnload_RoundTrip()
        {
            var global = new FakeGlobal { Name = "x" };

            LubanConfigBridge.RegisterGlobal(global);
            Assert.That(ConfigManager.GetGlobal<FakeGlobal>(), Is.SameAs(global));

            LubanConfigBridge.Unload<FakeGlobal>();
            Assert.That(ConfigManager.IsLoaded<FakeGlobal>(), Is.False);
        }

        [Test]
        public void Unload_ConfigManagerDestroyed_IsSafe()
        {
            ConfigManager.Destroy();

            Assert.DoesNotThrow(() => LubanConfigBridge.Unload<FakeRow>());
        }
    }
}
