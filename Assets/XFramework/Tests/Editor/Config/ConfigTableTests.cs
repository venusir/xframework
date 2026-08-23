using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="ConfigTable{T}"/> 与 <see cref="ConfigIndexView{T, TIndex}"/> 单元测试。
    /// <para>纯 C# 无依赖：构造字典直接生成包装器，覆盖主键查询、键类型不匹配、全量遍历与索引构建。</para>
    /// </summary>
    class ConfigTableTests
    {
        [Serializable]
        private struct TestItemRow : IConfigRow<int>
        {
            public int Id { get; set; }
            public string Name;
            public int Quality;
        }

        private static ConfigTable<TestItemRow> MakeTable()
        {
            return new ConfigTable<TestItemRow>(new Dictionary<int, TestItemRow>
            {
                [1] = new TestItemRow { Id = 1, Name = "Sword", Quality = 3 },
                [2] = new TestItemRow { Id = 2, Name = "Shield", Quality = 1 },
                [3] = new TestItemRow { Id = 3, Name = "Potion", Quality = 3 },
            });
        }

        #region Query

        [Test]
        public void Get_Hit_ReturnsRow()
        {
            var table = MakeTable();
            Assert.AreEqual("Sword", table.Get(1).Name);
        }

        [Test]
        public void Get_Miss_ThrowsConfigException()
        {
            var table = MakeTable();
            Assert.Throws<ConfigException>(() => table.Get(999));
        }

        [Test]
        public void Get_KeyTypeMismatch_ThrowsConfigException()
        {
            var table = MakeTable();
            Assert.Throws<ConfigException>(() => table.Get("not-an-int"));
        }

        [Test]
        public void TryGet_Hit_ReturnsTrue()
        {
            var table = MakeTable();
            Assert.IsTrue(table.TryGet(2, out var row));
            Assert.AreEqual("Shield", row.Name);
        }

        [Test]
        public void TryGet_Miss_ReturnsFalse()
        {
            var table = MakeTable();
            Assert.IsFalse(table.TryGet(999, out _));
        }

        [Test]
        public void TryGet_KeyTypeMismatch_ReturnsFalseWithWarning()
        {
            var table = MakeTable();
            LogAssert.Expect(LogType.Warning, new Regex("key type mismatch"));
            Assert.IsFalse(table.TryGet("not-an-int", out _));
        }

        [Test]
        public void Contains_Hit_ReturnsTrue()
        {
            var table = MakeTable();
            Assert.IsTrue(table.Contains(1));
        }

        [Test]
        public void Contains_Miss_ReturnsFalse()
        {
            var table = MakeTable();
            Assert.IsFalse(table.Contains(999));
        }

        #endregion

        #region Enumeration

        [Test]
        public void GetAll_And_Count_MatchDictionary()
        {
            var table = MakeTable();
            var all = table.GetAll();

            Assert.AreEqual(3, table.Count);
            Assert.AreEqual(3, all.Length, "GetAll 应返回全部行");
            CollectionAssert.AreEquivalent(
                new[] { "Sword", "Shield", "Potion" },
                Array.ConvertAll(all, r => r.Name));
        }

        #endregion

        #region Index

        [Test]
        public void BuildIndex_GroupsBySelector()
        {
            var table = MakeTable();
            var byQuality = table.BuildIndex("Quality", r => r.Quality);

            Assert.AreEqual(2, byQuality.Get(3).Count, "Quality=3 应有 2 行");
            Assert.AreEqual(1, byQuality.Get(1).Count);
            Assert.AreEqual("Sword", byQuality.Get(3)[0].Name);
        }

        [Test]
        public void BuildIndex_SameName_ReturnsCachedView()
        {
            var table = MakeTable();
            var first = table.BuildIndex("Quality", r => r.Quality);
            var second = table.BuildIndex("Quality", r => r.Quality);

            Assert.AreSame(first, second, "同一索引名应复用缓存视图");
        }

        [Test]
        public void BuildIndex_GetMissingKey_ReturnsEmptyNonNull()
        {
            var table = MakeTable();
            var byQuality = table.BuildIndex("Quality", r => r.Quality);

            var empty = byQuality.Get(999);
            Assert.IsNotNull(empty, "不存在的索引键应返回空数组而非 null");
            Assert.AreEqual(0, empty.Count);
            Assert.IsFalse(byQuality.TryGet(999, out _));
        }

        #endregion

        #region Condition Query

        [Test]
        public void TryGet_ConditionHit_ReturnsTrueWithRow()
        {
            var table = MakeTable();
            Assert.IsTrue(table.TryGet(r => r.Name == "Potion", out var row));
            Assert.AreEqual("Potion", row.Name);
        }

        [Test]
        public void TryGet_ConditionMiss_ReturnsFalseWithDefault()
        {
            var table = MakeTable();
            Assert.IsFalse(table.TryGet(r => r.Quality > 100, out var row));
            Assert.AreEqual(default(TestItemRow), row, "未找到时应输出 default 行");
        }

        [Test]
        public void TryGet_ConditionNullPredicate_Throws()
        {
            var table = MakeTable();
            Assert.Throws<ArgumentNullException>(() => table.TryGet(null, out _));
        }

        [Test]
        public void GetRows_WithResult_AppendsMatchingRows()
        {
            var table = MakeTable();
            var result = new List<TestItemRow>();
            table.GetRows(r => r.Quality == 3, result);

            Assert.AreEqual(2, result.Count);
            CollectionAssert.AreEquivalent(
                new[] { "Sword", "Potion" },
                result.ConvertAll(r => r.Name));
        }

        [Test]
        public void GetRows_WithResult_PreservesExistingItems()
        {
            var table = MakeTable();
            var result = new List<TestItemRow> { table.Get(1) };
            table.GetRows(r => r.Quality == 3, result);

            Assert.AreEqual(3, result.Count, "追加语义：不应清空已有元素");
            Assert.AreEqual("Sword", result[0].Name, "已有元素应保留在开头");
        }

        [Test]
        public void GetRows_Convenience_ReturnsNewList()
        {
            var table = MakeTable();
            var result = table.GetRows(r => r.Quality == 3);

            Assert.AreEqual(2, result.Count);
            CollectionAssert.AreEquivalent(
                new[] { "Sword", "Potion" },
                result.ConvertAll(r => r.Name));
        }

        [Test]
        public void GetRows_NullPredicate_Throws()
        {
            var table = MakeTable();
            Assert.Throws<ArgumentNullException>(() => table.GetRows(null, new List<TestItemRow>()));
        }

        [Test]
        public void GetRows_NullResult_Throws()
        {
            var table = MakeTable();
            Assert.Throws<ArgumentNullException>(() => table.GetRows(r => r.Quality == 3, null));
        }

        [Test]
        public void Exists_Hit_ReturnsTrue()
        {
            var table = MakeTable();
            Assert.IsTrue(table.Exists(r => r.Quality >= 3));
        }

        [Test]
        public void Exists_Miss_ReturnsFalse()
        {
            var table = MakeTable();
            Assert.IsFalse(table.Exists(r => r.Quality > 100));
        }

        [Test]
        public void Exists_NullPredicate_Throws()
        {
            var table = MakeTable();
            Assert.Throws<ArgumentNullException>(() => table.Exists(null));
        }

        #endregion
    }
}
