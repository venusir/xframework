using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XLocalization;
using XFramework.XUI.Authoring;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 本地化静态绑定（阶段 2a/2b 能力，阶段 3 补测）：UGUI Text 随语言切换刷新，
    /// 回池后订阅释放、不再改写文本。
    /// <para>TMP 路径不在此自动化：TMP Essentials 未导入的项目里行为不同，留作一次性手工探针（P5）。</para>
    /// </summary>
    [TestFixture]
    public sealed class UIDeclarativeLocalizationTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<UIPanelDefinition> _definitions = new List<UIPanelDefinition>();

        [SetUp]
        public void SetUp()
        {
            LocalizationManager.Destroy();
            LocalizationManager.Initialize("zh",
                new Dictionary<string, string> { ["ui.title"] = "标题" });
            LocalizationManager.SetLanguageData("en",
                new Dictionary<string, string> { ["ui.title"] = "Title" });
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();

            for (int i = _definitions.Count - 1; i >= 0; i--)
            {
                if (_definitions[i] != null)
                    Object.DestroyImmediate(_definitions[i]);
            }
            _definitions.Clear();

            LocalizationManager.Destroy();
        }

        [Test]
        public async Task Open_LocalizedText_FollowsLanguageSwitch()
        {
            UIDeclarativePanel panel = UIAuthoringTestFactory.CreatePanel();
            _created.Add(panel.gameObject);

            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition();
            _definitions.Add(definition);
            definition.TextBindings.Add(new UITextBinding
            {
                NodeName = "txt_title",
                UseLocalization = true,
                LocalizationKey = "ui.title",
            });

            Text text = UIAuthoringTestFactory
                .CreateChild(panel.transform, "txt_title", typeof(Text))
                .GetComponent<Text>();

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo("标题"));

            LocalizationManager.SetLanguage("en");
            Assert.That(text.text, Is.EqualTo("Title"));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);

            LocalizationManager.SetLanguage("zh");
            Assert.That(text.text, Is.EqualTo("Title"), "回池后订阅必须释放，语言切换不再改写文本");
        }
    }
}
