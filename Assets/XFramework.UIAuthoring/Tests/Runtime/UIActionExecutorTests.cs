using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XMessage;
using XFramework.XUI;

namespace XFramework.XUI.Authoring.Tests
{
    [TestFixture]
    public sealed class UIActionExecutorTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            MessageManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                UIAuthoringTestFactory.Destroy(_created[i]);
            _created.Clear();
            MessageManager.Clear();
        }

        private (UIActionExecutor executor, FakeActionRuntime runtime, UIActionSession session,
            UIDeclarativePanel panel, UIPanelDefinition definition) Create()
        {
            UIDeclarativePanel panel = UIAuthoringTestFactory.CreatePanel();
            _created.Add(panel.gameObject);
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition();
            var binder = new UIDeclarativeBinder(panel, definition);
            var runtime = new FakeActionRuntime();
            var session = new UIActionSession(runtime, panel);
            var executor = new UIActionExecutor(session, binder);
            return (executor, runtime, session, panel, definition);
        }

        private (UIActionExecutor executor, FakeActionRuntime runtime, UIActionSession session,
            UIDeclarativeDialogPanel dialog, UIPanelDefinition definition) CreateDialog()
        {
            UIDeclarativeDialogPanel dialog = UIAuthoringTestFactory.CreateDialogPanel();
            _created.Add(dialog.gameObject);
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition(UIPanelDefinitionKind.Dialog);
            var binder = new UIDeclarativeBinder(dialog, definition);
            var runtime = new FakeActionRuntime();
            var session = new UIActionSession(runtime, dialog);
            var executor = new UIActionExecutor(session, binder);
            return (executor, runtime, session, dialog, definition);
        }

        private static UIActionBinding Binding(params UIAction[] actions)
        {
            return new UIActionBinding { Actions = new List<UIAction>(actions) };
        }

        private static UIAction Action(UIActionKind kind)
        {
            return new UIAction { Kind = kind };
        }

        [Test]
        public async Task CloseSelf_ClosesAndInvalidatesSession()
        {
            var (executor, runtime, session, _, _) = Create();

            await executor.ExecuteAsync(Binding(Action(UIActionKind.CloseSelf)));

            Assert.That(runtime.CloseCalls, Is.EqualTo(1));
            Assert.That(session.IsValid, Is.False);
        }

        [Test]
        public async Task CloseSelf_ControllerBlocksClose_KeepsSessionAndContinuesChain()
        {
            var (executor, runtime, session, _, _) = Create();
            runtime.CloseSucceeds = false;

            await executor.ExecuteAsync(Binding(
                Action(UIActionKind.CloseSelf),
                new UIAction { Kind = UIActionKind.ShowTip, Text = "still here" }));

            Assert.That(runtime.CloseCalls, Is.EqualTo(1));
            Assert.That(session.IsValid, Is.True,
                "控制器拦下关闭时面板仍开着，动作会话不得失效");
            Assert.That(runtime.ShowTipCalls, Is.EqualTo(1),
                "面板仍在时后续动作应继续执行");
        }

        [Test]
        public async Task Open_UsesCSharpBaseRuntime()
        {
            var (executor, runtime, _, _, _) = Create();

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.Open,
                AssetPath = "ui/csharp",
                Layer = 120,
            }));

            Assert.That(runtime.OpenCSharpCalls, Is.EqualTo(1));
            Assert.That(runtime.LastOpenAssetPath, Is.EqualTo("ui/csharp"));
            Assert.That(runtime.LastOpenLayer, Is.EqualTo(120));
        }

        [Test]
        public async Task Push_And_Pop_Forward()
        {
            var (executor, runtime, _, _, _) = Create();

            await executor.ExecuteAsync(Binding(
                new UIAction { Kind = UIActionKind.Push, AssetPath = "ui/pushed", Layer = 130 },
                Action(UIActionKind.Pop)));

            Assert.That(runtime.PushCSharpCalls, Is.EqualTo(1));
            Assert.That(runtime.PopCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task Replace_UsesRuntimeReplace()
        {
            var (executor, runtime, session, _, _) = Create();

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.Replace,
                AssetPath = "ui/next",
                Layer = 140,
            }));

            Assert.That(runtime.ReplaceCalls, Is.EqualTo(1));
            Assert.That(runtime.LastReplaceTarget, Is.EqualTo("ui/next"));
            Assert.That(session.IsValid, Is.False);
        }

        [Test]
        public async Task Replace_ControllerBlocksClose_KeepsSessionValid()
        {
            var (executor, runtime, session, _, _) = Create();
            runtime.ReplaceSucceeds = false;

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.Replace,
                AssetPath = "ui/next",
                Layer = 140,
            }));

            Assert.That(runtime.ReplaceCalls, Is.EqualTo(1));
            Assert.That(session.IsValid, Is.True,
                "置换被拦下时当前面板仍在，动作会话不得失效");
        }

        [Test]
        public async Task ShowTip_PassesConvertedConfig()
        {
            var (executor, runtime, _, _, _) = Create();

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.ShowTip,
                Text = "hello",
                TipConfig = new UITipActionConfig { Duration = 3.5f, FloatDistance = 20f },
            }));

            Assert.That(runtime.ShowTipCalls, Is.EqualTo(1));
            Assert.That(runtime.LastTipText, Is.EqualTo("hello"));
            Assert.That(runtime.LastTipConfig.Duration, Is.EqualTo(3.5f));
            Assert.That(runtime.LastTipConfig.FloatDistance, Is.EqualTo(20f));
        }

        [Test]
        public async Task SetLayerVisibility_And_Interactive_Forward()
        {
            var (executor, runtime, _, _, _) = Create();

            await executor.ExecuteAsync(Binding(
                new UIAction { Kind = UIActionKind.SetLayerVisibility, Layer = 200, BoolValue = false },
                new UIAction { Kind = UIActionKind.SetLayerInteractive, Layer = 200, BoolValue = true }));

            Assert.That(runtime.SetLayerVisibilityCalls, Is.EqualTo(1));
            Assert.That(runtime.SetLayerInteractiveCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task SetActive_TogglesNode()
        {
            var (executor, _, _, panel, _) = Create();
            Transform child = UIAuthoringTestFactory.CreateChild(panel.transform, "sub");

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.SetActive,
                NodeName = "sub",
                BoolValue = false,
            }));

            Assert.That(child.gameObject.activeSelf, Is.False);
        }

        [Test]
        public async Task SelectTab_ShowsOnlyIndex()
        {
            var (executor, _, _, panel, definition) = Create();
            Transform tab0 = UIAuthoringTestFactory.CreateChild(panel.transform, "tab0");
            Transform tab1 = UIAuthoringTestFactory.CreateChild(panel.transform, "tab1");
            definition.TabGroups.Add(new UITabGroup
            {
                GroupName = "tabs",
                NodeNames = new List<string> { "tab0", "tab1" },
            });

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.SelectTab,
                GroupName = "tabs",
                Index = 1,
            }));

            Assert.That(tab0.gameObject.activeSelf, Is.False);
            Assert.That(tab1.gameObject.activeSelf, Is.True);
        }

        [Test]
        public async Task Custom_PublishesKeyedMessage()
        {
            var (executor, _, _, _, _) = Create();
            UIAuthoringActionMessage received = default;
            bool called = false;
            using (MessageManager.Subscribe<string, UIAuthoringActionMessage>("shop.buy", msg =>
                   {
                       received = msg;
                       called = true;
                   }))
            {
                await executor.ExecuteAsync(Binding(new UIAction
                {
                    Kind = UIActionKind.Custom,
                    ActionId = "shop.buy",
                    Payload = "42",
                }));
            }

            Assert.That(called, Is.True);
            Assert.That(received.ActionId, Is.EqualTo("shop.buy"));
            Assert.That(received.Payload, Is.EqualTo("42"));
        }

        [Test]
        public async Task ShowDialog_ForwardsToRuntime()
        {
            var (executor, runtime, _, _, _) = Create();

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.ShowDialog,
                AssetPath = "ui/dialog",
                ActionId = "shop.confirm",
            }));

            Assert.That(runtime.ShowDeclarativeDialogCalls, Is.EqualTo(1));
            Assert.That(runtime.LastDialogAddress, Is.EqualTo("ui/dialog"));
            Assert.That(runtime.LastDialogActionId, Is.EqualTo("shop.confirm"));
        }

        [Test]
        public async Task CloseWithResult_OnDialog_SetsResultOnClose()
        {
            var (executor, runtime, _, dialog, definition) = CreateDialog();
            dialog.SetDefinitionForTest(definition);
            dialog.SetRuntimeForTest(runtime);
            await dialog.OpenForTestAsync();

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.CloseWithResult,
                ResultId = "confirm",
            }));

            Assert.That(runtime.CloseCalls, Is.EqualTo(1));

            await dialog.CloseForTestAsync();
            string result = await dialog.WaitResultAsync();
            Assert.That(result, Is.EqualTo("confirm"));
        }

        [Test]
        public async Task CloseWithResult_ControllerBlocksClose_ClearsPendingAndKeepsSession()
        {
            var (executor, runtime, session, dialog, definition) = CreateDialog();
            dialog.SetDefinitionForTest(definition);
            dialog.SetRuntimeForTest(runtime);
            await dialog.OpenForTestAsync();
            runtime.CloseSucceeds = false;

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.CloseWithResult,
                ResultId = "confirm",
            }));

            Assert.That(session.IsValid, Is.True,
                "关闭被拦下时对话框仍开着，动作会话不得失效");

            // 之后由别的途径关闭（CloseAll / 父面板 / 回池）：过期结果不得兑现
            await dialog.CloseForTestAsync();
            Assert.That(dialog.WaitResultAsync().Status, Is.EqualTo(UniTaskStatus.Pending),
                "被拦下的选择不得在后续关闭时误兑现");
        }

        [Test]
        public void Exception_StopsChain()
        {
            var (executor, runtime, _, _, _) = Create();
            runtime.CloseException = new InvalidOperationException("boom");
            LogAssert.Expect(LogType.Error, new Regex("Action chain failed"));

            Assert.DoesNotThrowAsync(async () =>
                await executor.ExecuteAsync(Binding(
                    Action(UIActionKind.CloseSelf),
                    new UIAction { Kind = UIActionKind.Custom, ActionId = "never" })));
        }

        [Test]
        public async Task ConcurrentChain_IsIgnored()
        {
            var (executor, runtime, _, _, _) = Create();
            runtime.Gate = new UniTaskCompletionSource();
            UIActionBinding binding = Binding(new UIAction
            {
                Kind = UIActionKind.Open,
                AssetPath = "ui/csharp",
            });

            UniTask first = executor.ExecuteAsync(binding);
            await executor.ExecuteAsync(binding);

            Assert.That(runtime.OpenCSharpCalls, Is.EqualTo(1));

            runtime.Gate.TrySetResult();
            await first;
        }

        [Test]
        public async Task ShowPopup_ResolvesAnchorAndForwardsOptions()
        {
            var (executor, runtime, _, panel, _) = Create();
            UIDeclarativePopupPanel popup = UIAuthoringTestFactory.CreatePopup();
            _created.Add(popup.gameObject);
            UIAuthoringTestFactory.SetAssetPath(popup, "ui/popups/demo");
            runtime.DeclarativePopup = popup;

            Transform anchor = UIAuthoringTestFactory.CreateChild(panel.transform, "btn_anchor");

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.ShowPopup,
                AssetPath = "ui/popups/demo",
                AnchorNodeName = "btn_anchor",
                Direction = UIPopupDirection.Right,
                Align = UIPopupAlign.End,
                CloseOnClickOutside = false,
            }));

            Assert.That(runtime.ShowDeclarativePopupCalls, Is.EqualTo(1));
            Assert.That(runtime.LastPopupAddress, Is.EqualTo("ui/popups/demo"));
            Assert.That(runtime.LastPopupAnchor, Is.SameAs(anchor));
            Assert.That(runtime.LastPopupDirection, Is.EqualTo(UIPopupDirection.Right));
            Assert.That(runtime.LastPopupAlign, Is.EqualTo(UIPopupAlign.End));
            Assert.That(runtime.LastPopupCloseOnClickOutside, Is.False);
        }

        [Test]
        public async Task ShowPopup_MissingAnchor_WarnsAndSkips()
        {
            var (executor, runtime, _, _, _) = Create();
            LogAssert.Expect(LogType.Warning, new Regex("ShowPopup: anchor node 'missing' not found"));

            await executor.ExecuteAsync(Binding(new UIAction
            {
                Kind = UIActionKind.ShowPopup,
                AssetPath = "ui/popups/demo",
                AnchorNodeName = "missing",
            }));

            Assert.That(runtime.ShowDeclarativePopupCalls, Is.EqualTo(0));
        }
    }
}
