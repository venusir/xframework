using System;
using NUnit.Framework;
using UnityEngine;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 坐实 README 行类型示例的前提：<see cref="JsonUtility"/> 只序列化**字段**，不序列化属性。
    /// <para><b>为什么值得一条常驻用例</b>：Json 与 ScriptableObject 两条内置加载路径都走 Unity 序列化，
    /// 行类型的主键若是自动属性会恒为 <c>default</c>——多行 JSON 撞 <c>Duplicate Id '0'</c>，
    /// 单行则静默产出键 0 的表；SO 路径在 Inspector 里根本没有地方填写。CSV 走反射写成员，
    /// 属性可用（见 <c>CsvLoaderTests</c> 的 struct 行），所以这个坑只在 Json / SO 路径显形。
    /// 本用例钉住的是 Unity 序列化事实，不是产品回归——它保证 README 的示例不会再改回属性。</para>
    /// </summary>
    [TestFixture]
    public class JsonRowSerializationTests
    {
        /// <summary>旧示例的写法：主键是自动属性。</summary>
        [Serializable]
        private struct PropertyRow
        {
            public int Id { get; set; }

            // JsonUtility 反射填充：赋值发生在 Unity 序列化里，编译器看不到 → CS0649 是假阳性
#pragma warning disable CS0649
            public string Name;
#pragma warning restore CS0649
        }

        /// <summary>修正后的写法：主键是字段。</summary>
        [Serializable]
        private struct FieldRow
        {
            // JsonUtility 反射填充：赋值发生在 Unity 序列化里，编译器看不到 → CS0649 是假阳性
#pragma warning disable CS0649
            public int Id;
            public string Name;
#pragma warning restore CS0649
        }

        [Test]
        public void FromJson_PropertyRow_PrimaryKeyStaysDefault()
        {
            var row = JsonUtility.FromJson<PropertyRow>("{\"Id\":1001,\"Name\":\"alpha\"}");

            Assert.AreEqual(0, row.Id,
                "JsonUtility 不序列化属性——按旧 README 示例建表会得到主键恒 0 的行");
            Assert.AreEqual("alpha", row.Name, "字段部分仍应正常反序列化，用来区分「整行都没读到」");
        }

        [Test]
        public void FromJson_FieldRow_PrimaryKeyIsDeserialized()
        {
            // 负向控制：字段版必须读回 1001，证明上一条的 0 不是输入本身的问题
            var row = JsonUtility.FromJson<FieldRow>("{\"Id\":1001,\"Name\":\"alpha\"}");

            Assert.AreEqual(1001, row.Id, "字段版主键必须被反序列化——这是 README 修正后的写法");
            Assert.AreEqual("alpha", row.Name);
        }

        #region JsonLoader 解析段（CONF-012）

        /// <summary>JsonLoader 正路径用例的行类型：字段供 JsonUtility，接口由显式实现兑现。</summary>
        [Serializable]
        private struct LoaderRow : IConfigRow<int>
        {
            // JsonUtility 反射填充：赋值发生在 Unity 序列化里，编译器看不到 → CS0649 是假阳性
#pragma warning disable CS0649
            public int Id;
            public string Name;
#pragma warning restore CS0649

            int IConfigRow<int>.Id => Id;
        }

        [Serializable]
        private sealed class LoaderGlobal
        {
#pragma warning disable CS0649
            public int Value;
#pragma warning restore CS0649
        }

        [Test]
        public void JsonLoader_ParseTable_PositivePath_BuildsTable()
        {
            // CONF-012：Json 正路径此前零直接覆盖（全部 fixture 注入 FakeConfigLoader）
            var table = JsonLoader.ParseTable<LoaderRow, int>(
                "{\"Items\":[{\"Id\":1,\"Name\":\"a\"},{\"Id\":2,\"Name\":\"b\"}]}", "config/items");

            Assert.AreEqual(2, table.Count);
            Assert.AreEqual("a", table.Get(1).Name);
            Assert.AreEqual("b", table.Get(2).Name);
        }

        [Test]
        public void JsonLoader_ParseTable_DuplicateId_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => JsonLoader.ParseTable<LoaderRow, int>(
                "{\"Items\":[{\"Id\":1},{\"Id\":1}]}", "config/items"));
            StringAssert.Contains("Duplicate Id", ex.Message);
        }

        [Test]
        public void JsonLoader_ParseTable_NullResult_Throws()
        {
            Assert.Throws<ConfigException>(() =>
                JsonLoader.ParseTable<LoaderRow, int>("null", "config/items"));
        }

        [Test]
        public void JsonLoader_ParseGlobal_PositivePath()
        {
            var config = JsonLoader.ParseGlobal<LoaderGlobal>("{\"Value\":42}", "config/global");

            Assert.AreEqual(42, config.Value);
        }

        [Test]
        public void JsonLoader_ParseGlobal_NullResult_Throws()
        {
            Assert.Throws<ConfigException>(() =>
                JsonLoader.ParseGlobal<LoaderGlobal>("null", "config/global"));
        }

        #endregion
    }
}
