using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="ScriptableObjectLoader.BuildTable{T,TKey}"/> 的资产形状测试（R3-CONF-002）。
    /// <para>走与资源加载解耦的内部入口：测试进程里 <c>AssetManager</c> 未初始化，真实的 SO 加载路径
    /// 不可达；「资产是否继承公开基类 <see cref="ScriptableObjectTable{TRow,TKey}"/>」与行/主键构建
    /// 正是可直接单测的那一段。</para>
    /// </summary>
    [TestFixture]
    public class ScriptableObjectTableTests
    {
        #region 测试资产

        [Serializable]
        private struct SettingsRow : IConfigRow<int>
        {
            public int Id;
            public int Volume;

            int IConfigRow<int>.Id => Id;
        }

        /// <summary>正确形状：继承公开基类的 SO 子类。</summary>
        private sealed class ValidTable : ScriptableObjectTable<SettingsRow, int>
        {
        }

        /// <summary>错误形状：普通 SO。</summary>
        private sealed class PlainSo : ScriptableObject
        {
        }

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private T Create<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _created.Add(asset);
            return asset;
        }

        private ValidTable CreateValidTable(params SettingsRow[] rows)
        {
            var table = Create<ValidTable>();

            // Items 的 setter 是 private（正常由 Unity 序列化填充）；测试用 backing field 注入。
            // 刻意不改公开面：把 setter 放开会让使用方能在运行期改写资产数据。
            var field = typeof(ScriptableObjectTable<SettingsRow, int>)
                .GetField("<Items>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Items 的 backing field 不存在（属性改名了？）");
            field.SetValue(table, new List<SettingsRow>(rows));
            return table;
        }

        #endregion

        [Test]
        public void BuildTable_PlainScriptableObject_ThrowsWithBaseClassMessage()
        {
            var plain = Create<PlainSo>();

            var ex = Assert.Throws<ConfigException>(() =>
                ScriptableObjectLoader.BuildTable<SettingsRow, int>(plain, "config/settings"));

            StringAssert.Contains("must inherit ScriptableObjectTable", ex.Message,
                "普通 SO 必须得到「继承基类」的明确消息，而不是 AssetManager 初始化/资源不存在的误归因");
            StringAssert.Contains("SettingsRow", ex.Message);
        }

        [Test]
        public void BuildTable_ValidSubclass_BuildsRows()
        {
            var table = CreateValidTable(new SettingsRow { Id = 1, Volume = 50 });

            var built = ScriptableObjectLoader.BuildTable<SettingsRow, int>(table, "config/settings");

            Assert.AreEqual(1, built.Count);
            Assert.AreEqual(50, built.Get(1).Volume);
        }

        [Test]
        public void BuildTable_EmptyItems_ThrowsContainsNoItems()
        {
            var table = CreateValidTable();

            var ex = Assert.Throws<ConfigException>(() =>
                ScriptableObjectLoader.BuildTable<SettingsRow, int>(table, "config/settings"));

            StringAssert.Contains("contains no items", ex.Message);
        }

        [Test]
        public void BuildTable_DuplicateId_Throws()
        {
            var table = CreateValidTable(
                new SettingsRow { Id = 7, Volume = 1 },
                new SettingsRow { Id = 7, Volume = 2 });

            var ex = Assert.Throws<ConfigException>(() =>
                ScriptableObjectLoader.BuildTable<SettingsRow, int>(table, "config/settings"));

            StringAssert.Contains("Duplicate Id '7'", ex.Message);
        }
    }
}
