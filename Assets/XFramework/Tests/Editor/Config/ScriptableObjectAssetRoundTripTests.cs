using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// SO Table 的资产往返（R4-CONF-003）：经 <see cref="AssetDatabase"/> 真实保存并重载，
    /// 验证泛型基类的 <c>[field: SerializeField] List&lt;TRow&gt;</c> 在 Unity 序列化里确实落盘。
    /// <para>此前 <c>ScriptableObjectTableTests</c> 只经反射写 backing field，从未经过资产保存/重载；
    /// 若某 Unity 版本不序列化泛型基类字段，所有使用方 SO 表都会稳定得到「contains no items」而测试全绿。</para>
    /// </summary>
    [TestFixture]
    public class ScriptableObjectAssetRoundTripTests
    {
        private const string RootFolder = "Assets/__XFrameworkRoundTripTest__";

        [Serializable]
        private struct RoundTripRow : IConfigRow<int>
        {
            public int Id;
            public int Volume;

            int IConfigRow<int>.Id => Id;
        }

        private sealed class RoundTripTable : ScriptableObjectTable<RoundTripRow, int>
        {
        }

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.DeleteAsset(RootFolder);
            AssetDatabase.CreateFolder("Assets", "__XFrameworkRoundTripTest__");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.DeleteAsset(RootFolder);
        }

        [Test]
        public void SavedAsset_LoadsBackWithItemsAndBuildsTable()
        {
            string path = RootFolder + "/RoundTrip.asset";
            var table = ScriptableObject.CreateInstance<RoundTripTable>();

            var serialized = new SerializedObject(table);
            // [field: SerializeField] 作用于自动属性的 backing field：Unity 的序列化路径可能是
            // 编译器生成名，也可能是属性名（版本相关），两者都试。
            var items = serialized.FindProperty("Items")
                        ?? serialized.FindProperty("<Items>k__BackingField");
            Assert.IsNotNull(items, "Items 的序列化属性不存在——基类字段没有 [SerializeField]？");

            items.arraySize = 2;
            var first = items.GetArrayElementAtIndex(0);
            first.FindPropertyRelative("Id").intValue = 1;
            first.FindPropertyRelative("Volume").intValue = 50;
            var second = items.GetArrayElementAtIndex(1);
            second.FindPropertyRelative("Id").intValue = 2;
            second.FindPropertyRelative("Volume").intValue = 75;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(table, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var loaded = AssetDatabase.LoadAssetAtPath<RoundTripTable>(path);
            Assert.IsNotNull(loaded, "资产应能重新加载");
            Assert.AreEqual(2, loaded.Items.Count,
                "泛型基类的 [field: SerializeField] 列表必须经 Unity 资产往返保留");

            var built = ScriptableObjectLoader.BuildTable<RoundTripRow, int>(loaded, path);
            Assert.AreEqual(2, built.Count);
            Assert.AreEqual(50, built.Get(1).Volume);
            Assert.AreEqual(75, built.Get(2).Volume);
        }
    }
}
