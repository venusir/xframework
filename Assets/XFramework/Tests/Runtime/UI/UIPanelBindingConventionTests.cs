using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using XFramework.XReactive;
using XFramework.XUI.Data;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 约定绑定节点的命名/类型检查（R3-UIB-009/013）。
    /// <para>此前 `img_` 挂在没有 Image 的节点上会静默跳过（与 README「未找到会告警」相反）；
    /// 面板内同名约定节点后者覆盖前者，先出现的永久未绑且无提示。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelBindingConventionTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Panel", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
        }

        private UIPanelBinding CreateBinding() => _root.AddComponent<UIPanelBinding>();

        [Test]
        public void ImgNodeWithoutImage_Warns()
        {
            var child = new GameObject("img_icon", typeof(RectTransform));
            child.transform.SetParent(_root.transform, false);

            var binding = CreateBinding();
            LogAssert.Expect(LogType.Warning, new Regex("未找到 Image 组件"));

            binding.CacheComponents(_root.transform);
        }

        [Test]
        public void ImgNodeWithUnsupportedSourceType_Warns()
        {
            // R4-UIB-005：组件存在但源值类型不受支持时此前完全静默。
            var child = new GameObject("img_icon", typeof(RectTransform));
            child.transform.SetParent(_root.transform, false);
            child.AddComponent<Image>();

            var binding = CreateBinding();
            binding.CacheComponents(_root.transform);
            var source = new ReactiveProperty<int>(1);

            LogAssert.Expect(LogType.Warning, new Regex("不受支持"));
            binding.BindByConvention("icon", source);
        }

        [Test]
        public void DuplicateConventionNode_WarnsAndKeepsFirst()
        {
            var first = new GameObject("txt_title", typeof(RectTransform));
            first.transform.SetParent(_root.transform, false);
            var firstText = first.AddComponent<Text>();

            var second = new GameObject("txt_title", typeof(RectTransform));
            second.transform.SetParent(_root.transform, false);
            var secondText = second.AddComponent<Text>();
            secondText.text = "second";

            var binding = CreateBinding();
            LogAssert.Expect(LogType.Warning, new Regex("重复的约定节点名"));
            binding.CacheComponents(_root.transform);

            var source = new ReactiveProperty<int>(1);
            binding.BindByConvention("title", source);
            source.Value = 2;

            Assert.AreEqual("2", firstText.text, "第一个同名节点应被绑定");
            Assert.AreEqual("second", secondText.text, "第二个同名节点不应被覆盖绑定");
            source.Dispose();
        }
    }
}
