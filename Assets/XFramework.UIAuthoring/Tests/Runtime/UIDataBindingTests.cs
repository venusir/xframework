using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using XFramework.XReactive;
using XFramework.XUI.Authoring;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 阶段 3 数据绑定：Panel / HUD / Dialog 三形态按定义资产订阅 <see cref="IUIDataProvider"/>。
    /// <para>所有用例在 SetUp/TearDown 复位静态注册表；属性与视图都在 TearDown 释放，避免跨 fixture 污染。</para>
    /// </summary>
    [TestFixture]
    public sealed class UIDataBindingTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<UIPanelDefinition> _definitions = new List<UIPanelDefinition>();
        private readonly List<Object> _assets = new List<Object>();
        private readonly List<System.IDisposable> _disposables = new List<System.IDisposable>();

        [SetUp]
        public void SetUp()
        {
            UIDataProviderRegistry.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            UIDataProviderRegistry.ResetForNewSession();

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

            for (int i = _assets.Count - 1; i >= 0; i--)
            {
                if (_assets[i] != null)
                    Object.DestroyImmediate(_assets[i]);
            }
            _assets.Clear();

            for (int i = _disposables.Count - 1; i >= 0; i--)
                _disposables[i]?.Dispose();
            _disposables.Clear();
        }

        #region Helpers

        private FakeDataProvider CreateProvider()
        {
            var provider = new FakeDataProvider();
            UIDataProviderRegistry.Register(provider);
            return provider;
        }

        private ReactiveProperty<T> Track<T>(ReactiveProperty<T> property)
        {
            _disposables.Add(property);
            return property;
        }

        private ReadOnlyReactiveProperty<T> Track<T>(ReadOnlyReactiveProperty<T> property)
        {
            _disposables.Add(property);
            return property;
        }

        private UIDeclarativePanel CreatePanel()
        {
            UIDeclarativePanel panel = UIAuthoringTestFactory.CreatePanel();
            _created.Add(panel.gameObject);
            return panel;
        }

        private UIDeclarativeDialogPanel CreateDialogPanel()
        {
            UIDeclarativeDialogPanel panel = UIAuthoringTestFactory.CreateDialogPanel();
            _created.Add(panel.gameObject);
            return panel;
        }

        private UIDeclarativeHudItem CreateHud()
        {
            UIDeclarativeHudItem hud = UIAuthoringTestFactory.CreateHud();
            _created.Add(hud.gameObject);
            return hud;
        }

        private UIPanelDefinition CreateDefinition(UIPanelDefinitionKind kind = UIPanelDefinitionKind.Panel)
        {
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition(kind);
            _definitions.Add(definition);
            return definition;
        }

        private Text CreateText(Transform parent, string name = "txt_name")
        {
            return UIAuthoringTestFactory.CreateChild(parent, name, typeof(Text)).GetComponent<Text>();
        }

        private Image CreateImage(Transform parent, string name = "img_icon")
        {
            return UIAuthoringTestFactory.CreateChild(parent, name, typeof(Image)).GetComponent<Image>();
        }

        private Slider CreateSlider(Transform parent, string name = "sld_progress")
        {
            return UIAuthoringTestFactory.CreateChild(parent, name, typeof(Slider)).GetComponent<Slider>();
        }

        private Toggle CreateToggle(Transform parent, string name = "tgl_flag")
        {
            return UIAuthoringTestFactory.CreateChild(parent, name, typeof(Toggle)).GetComponent<Toggle>();
        }

        private Sprite CreateSprite()
        {
            var texture = new Texture2D(4, 4);
            _assets.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _assets.Add(sprite);
            return sprite;
        }

        private static void ExpectWarning(string pattern)
        {
            LogAssert.Expect(LogType.Warning, new Regex(pattern));
        }

        #endregion

        #region Text

        [Test]
        public async Task Open_TextBinding_BindsInitialValueAndUpdates()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<string> title = Track(new ReactiveProperty<string>("First"));
            provider.Values["demo.title"] = title;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title",
                Kind = UIDataBindingKind.Text,
                DataKey = "demo.title",
            });
            Text text = CreateText(panel.transform, "txt_title");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo("First"));

            title.Value = "Second";
            Assert.That(text.text, Is.EqualTo("Second"));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
            Assert.That(title.SubscriptionCount, Is.EqualTo(0), "回池后订阅必须释放");
        }

        [Test]
        public async Task Open_TextBinding_AppliesFormat()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<string> hp = Track(new ReactiveProperty<string>("75"));
            provider.Values["demo.hp"] = hp;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_hp",
                Kind = UIDataBindingKind.Text,
                DataKey = "demo.hp",
                Format = "HP {0}",
            });
            Text text = CreateText(panel.transform, "txt_hp");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo("HP 75"));

            hp.Value = "50";
            Assert.That(text.text, Is.EqualTo("HP 50"));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        #endregion

        #region Failures

        [Test]
        public async Task Open_NoProvider_SkipsWithSingleWarning()
        {
            ExpectWarning("No IUIDataProvider registered; 2 data binding\\(s\\) skipped");

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_a", Kind = UIDataBindingKind.Text, DataKey = "a",
            });
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_b", Kind = UIDataBindingKind.Text, DataKey = "b",
            });
            Text textA = CreateText(panel.transform, "txt_a");
            CreateText(panel.transform, "txt_b");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(textA.text, Is.EqualTo(string.Empty), "无 provider 时静态文本保持原样");

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_NodeMissing_WarnsAndSkips()
        {
            CreateProvider();
            ExpectWarning("Data binding: node 'missing_node' not found");

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "missing_node", Kind = UIDataBindingKind.Text, DataKey = "demo.title",
            });

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_KeyMissing_WarnsAndSkips()
        {
            CreateProvider();
            ExpectWarning("Data binding: key 'missing.key' not found or type mismatch");

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title", Kind = UIDataBindingKind.Text, DataKey = "missing.key",
            });
            Text text = CreateText(panel.transform, "txt_title");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_TypeMismatch_WarnsAndSkips()
        {
            FakeDataProvider provider = CreateProvider();
            provider.Values["demo.title"] = Track(new ReactiveProperty<int>(7));
            ExpectWarning("Data binding: key 'demo.title' not found or type mismatch");

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title", Kind = UIDataBindingKind.Text, DataKey = "demo.title",
            });
            Text text = CreateText(panel.transform, "txt_title");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_EmptyKey_WarnsAndSkips()
        {
            CreateProvider();
            ExpectWarning("Data binding: node 'txt_title' has an empty data key");

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title", Kind = UIDataBindingKind.Text, DataKey = string.Empty,
            });
            CreateText(panel.transform, "txt_title");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_ProviderThrows_IsIsolatedAndFallsBack()
        {
            var provider = new FakeDataProvider { ThrowOnTryGet = new System.InvalidOperationException("boom") };
            UIDataProviderRegistry.Register(provider);
            ExpectWarning("Data provider threw while resolving key 'demo.title': boom");
            ExpectWarning("Data binding: key 'demo.title' not found or type mismatch");

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title", Kind = UIDataBindingKind.Text, DataKey = "demo.title",
            });
            Text text = CreateText(panel.transform, "txt_title");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        #endregion

        #region Components

        [Test]
        public async Task Open_SpriteBinding_AppliesSprite()
        {
            FakeDataProvider provider = CreateProvider();
            Sprite first = CreateSprite();
            Sprite second = CreateSprite();
            ReactiveProperty<Sprite> icon = Track(new ReactiveProperty<Sprite>(first));
            provider.Values["demo.icon"] = icon;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "img_icon", Kind = UIDataBindingKind.Sprite, DataKey = "demo.icon",
            });
            Image image = CreateImage(panel.transform);

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(image.sprite, Is.EqualTo(first));

            icon.Value = second;
            Assert.That(image.sprite, Is.EqualTo(second));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_ColorBinding_AppliesColor()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<Color> tint = Track(new ReactiveProperty<Color>(Color.red));
            provider.Values["demo.tint"] = tint;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "img_tint", Kind = UIDataBindingKind.Color, DataKey = "demo.tint",
            });
            Image image = CreateImage(panel.transform, "img_tint");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(image.color, Is.EqualTo(Color.red));

            tint.Value = Color.blue;
            Assert.That(image.color, Is.EqualTo(Color.blue));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_FillAmountBinding_AppliesValue()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<float> hp = Track(new ReactiveProperty<float>(0.8f));
            provider.Values["demo.hp"] = hp;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "img_hp", Kind = UIDataBindingKind.FillAmount, DataKey = "demo.hp",
            });
            Image image = CreateImage(panel.transform, "img_hp");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(image.fillAmount, Is.EqualTo(0.8f).Within(0.0001f));

            hp.Value = 0.3f;
            Assert.That(image.fillAmount, Is.EqualTo(0.3f).Within(0.0001f));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_ActiveBinding_TogglesNode()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<bool> visible = Track(new ReactiveProperty<bool>(true));
            provider.Values["demo.badge"] = visible;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "obj_badge", Kind = UIDataBindingKind.Active, DataKey = "demo.badge",
            });
            Transform badge = UIAuthoringTestFactory.CreateChild(panel.transform, "obj_badge");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(badge.gameObject.activeSelf, Is.True);

            visible.Value = false;
            Assert.That(badge.gameObject.activeSelf, Is.False);

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        #endregion

        #region Two-way

        [Test]
        public async Task Open_SliderOneWay_BindsAndUpdates()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<float> progress = Track(new ReactiveProperty<float>(0.4f));
            provider.Values["demo.progress"] = progress;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "sld_progress", Kind = UIDataBindingKind.Slider, DataKey = "demo.progress",
            });
            Slider slider = CreateSlider(panel.transform);

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(slider.value, Is.EqualTo(0.4f).Within(0.0001f));

            progress.Value = 0.9f;
            Assert.That(slider.value, Is.EqualTo(0.9f).Within(0.0001f));

            slider.value = 0.1f;
            Assert.That(progress.Value, Is.EqualTo(0.9f).Within(0.0001f), "单向绑定不得写回");

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_SliderTwoWay_WritesBackAndUpdates()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<float> progress = Track(new ReactiveProperty<float>(0.4f));
            provider.Values["demo.progress"] = progress;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "sld_progress",
                Kind = UIDataBindingKind.Slider,
                DataKey = "demo.progress",
                TwoWay = true,
            });
            Slider slider = CreateSlider(panel.transform);

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(slider.value, Is.EqualTo(0.4f).Within(0.0001f));

            slider.value = 0.7f;
            Assert.That(progress.Value, Is.EqualTo(0.7f).Within(0.0001f));

            progress.Value = 0.2f;
            Assert.That(slider.value, Is.EqualTo(0.2f).Within(0.0001f));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_SliderTwoWay_ReadOnlySource_DegradesToOneWay()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<float> raw = Track(new ReactiveProperty<float>(0.3f));
            ReadOnlyReactiveProperty<float> readOnly = Track(raw.Select(value => value));
            provider.Values["demo.progress"] = readOnly;
            ExpectWarning("Data binding: key 'demo.progress' is read-only; 'TwoWay' degraded to one-way");

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "sld_progress",
                Kind = UIDataBindingKind.Slider,
                DataKey = "demo.progress",
                TwoWay = true,
            });
            Slider slider = CreateSlider(panel.transform);

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(slider.value, Is.EqualTo(0.3f).Within(0.0001f));

            raw.Value = 0.6f;
            Assert.That(slider.value, Is.EqualTo(0.6f).Within(0.0001f));

            slider.value = 0.1f;
            Assert.That(raw.Value, Is.EqualTo(0.6f).Within(0.0001f), "降级后不得写回");

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_ToggleTwoWay_WritesBackAndUpdates()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<bool> mute = Track(new ReactiveProperty<bool>(false));
            provider.Values["demo.mute"] = mute;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "tgl_mute",
                Kind = UIDataBindingKind.Toggle,
                DataKey = "demo.mute",
                TwoWay = true,
            });
            Toggle toggle = CreateToggle(panel.transform, "tgl_mute");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(toggle.isOn, Is.False);

            toggle.isOn = true;
            Assert.That(mute.Value, Is.True);

            mute.Value = false;
            Assert.That(toggle.isOn, Is.False);

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        #endregion

        #region Registry

        [Test]
        public async Task Open_LaterProviderWins()
        {
            var early = new FakeDataProvider();
            early.Values["demo.title"] = Track(new ReactiveProperty<string>("Early"));
            UIDataProviderRegistry.Register(early);

            var late = new FakeDataProvider();
            late.Values["demo.title"] = Track(new ReactiveProperty<string>("Late"));
            UIDataProviderRegistry.Register(late);

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title", Kind = UIDataBindingKind.Text, DataKey = "demo.title",
            });
            Text text = CreateText(panel.transform, "txt_title");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo("Late"));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Unregister_StopsResolution()
        {
            FakeDataProvider provider = CreateProvider();
            provider.Values["demo.title"] = Track(new ReactiveProperty<string>("First"));
            UIDataProviderRegistry.Unregister(provider);
            ExpectWarning("No IUIDataProvider registered; 1 data binding\\(s\\) skipped");

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title", Kind = UIDataBindingKind.Text, DataKey = "demo.title",
            });
            Text text = CreateText(panel.transform, "txt_title");

            panel.SetDefinitionForTest(definition);
            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Reopen_AfterPoolRecycle_DoesNotLeakSubscriptions()
        {
            FakeDataProvider provider = CreateProvider();
            ReactiveProperty<string> title = Track(new ReactiveProperty<string>("First"));
            provider.Values["demo.title"] = title;

            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title", Kind = UIDataBindingKind.Text, DataKey = "demo.title",
            });
            Text text = CreateText(panel.transform, "txt_title");
            panel.SetDefinitionForTest(definition);

            await panel.OpenForTestAsync();
            Assert.That(title.SubscriptionCount, Is.EqualTo(1));
            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
            Assert.That(title.SubscriptionCount, Is.EqualTo(0));

            title.Value = "Second";
            await panel.OpenForTestAsync();
            Assert.That(text.text, Is.EqualTo("Second"));
            Assert.That(title.SubscriptionCount, Is.EqualTo(1));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
            Assert.That(title.SubscriptionCount, Is.EqualTo(0));
        }

        #endregion

        #region Other views

        [Test]
        public async Task Open_DialogDataBinding_Applies()
        {
            FakeDataProvider provider = CreateProvider();
            provider.Values["demo.title"] = Track(new ReactiveProperty<string>("Dialog"));

            UIDeclarativeDialogPanel dialog = CreateDialogPanel();
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Dialog);
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title", Kind = UIDataBindingKind.Text, DataKey = "demo.title",
            });
            Text text = CreateText(dialog.transform, "txt_title");

            dialog.SetDefinitionForTest(definition);
            await dialog.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo("Dialog"));

            await dialog.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(dialog);
        }

        [Test]
        public async Task Open_HudDataBinding_Applies()
        {
            FakeDataProvider provider = CreateProvider();
            provider.Values["demo.hudName"] = Track(new ReactiveProperty<string>("NPC"));

            UIDeclarativeHudItem hud = CreateHud();
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Hud);
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_name", Kind = UIDataBindingKind.Text, DataKey = "demo.hudName",
            });
            Text text = CreateText(hud.transform, "txt_name");

            hud.SetDefinitionForTest(definition);
            await hud.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo("NPC"));

            await hud.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(hud);
        }

        #endregion
    }
}
