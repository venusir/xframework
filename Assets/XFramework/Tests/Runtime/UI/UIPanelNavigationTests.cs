using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 显示栈与导航语义测试。
    /// <para>核心回归：早先只有 <c>PushAsync</c> 会写导航栈，<c>OpenAsync</c> 不写。于是
    /// 「Open 开主界面 + Push 开二级页」之后栈深恒为 1，<c>PopAsync</c>、<c>CanGoBack</c>
    /// 与遮罩点击关闭会同时失效——而那恰是最常见的用法组合。</para>
    /// <para>另一处回归：<c>UIManager.Update</c> 原先在 <c>foreach</c> 里遍历活动面板并调用
    /// <c>OnUpdate</c>，面板自关时同步改集合，当场抛 InvalidOperationException。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelNavigationTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_NavTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();
            _factory.RegisterPanel<FakePanelC>();
            _factory.RegisterPanel<SelfClosingPanel>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #region 栈语义

        [Test]
        public async Task OpenThenPush_CanGoBackIsTrue()
        {
            Assert.IsFalse(UIManager.CanGoBack, "未打开任何面板时不能退回");

            await UIManager.OpenAsync<FakePanel>("ui/first");
            Assert.IsFalse(UIManager.CanGoBack, "只有栈底面板时不能退回");

            await UIManager.PushAsync<FakePanelB>("ui/second");
            Assert.IsTrue(UIManager.CanGoBack,
                "Open 打开的面板也应在显示栈里，否则此处栈深恒为 1");
        }

        [Test]
        public async Task OpenThenPush_PopReturnsToOpenedPanel()
        {
            var first = await UIManager.OpenAsync<FakePanel>("ui/first");
            await UIManager.PushAsync<FakePanelB>("ui/second");

            await UIManager.PopAsync();

            Assert.IsFalse(UIManager.IsOpen<FakePanelB>(), "Pop 应关掉 Push 进来的面板");
            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "先 Open 的面板应保持打开");
            Assert.IsTrue(first.Logged("OnBlur"), "Push 时前一个面板应失焦");
            Assert.IsTrue(first.Logged("OnFocus"), "Pop 后前一个面板应恢复焦点");
        }

        [Test]
        public async Task PopAsync_OnStackBottom_IsNoOp()
        {
            await UIManager.OpenAsync<FakePanel>("ui/first");

            await UIManager.PopAsync();

            Assert.IsTrue(UIManager.IsOpen<FakePanel>(),
                "栈底面板不参与弹出，栈深为 1 时 PopAsync 应为 no-op");
        }

        [Test]
        public async Task OpenAsync_SameTypeTwice_DoesNotDuplicateInStack()
        {
            var first = await UIManager.OpenAsync<FakePanel>("ui/first");
            var second = await UIManager.OpenAsync<FakePanel>("ui/first");

            Assert.AreSame(first, second, "同类型面板应复用已打开的实例");
            Assert.AreEqual(1, _factory.CreateCount, "不应重复实例化");
            Assert.IsFalse(UIManager.CanGoBack, "重复打开不应在栈中留下重复条目");
        }

        [Test]
        public async Task PopToAsync_ClosesIntermediatePanels()
        {
            await UIManager.OpenAsync<FakePanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");
            await UIManager.PushAsync<FakePanelC>("ui/c");

            await UIManager.PopToAsync<FakePanelB>();

            Assert.IsTrue(UIManager.IsOpen<FakePanelB>(), "目标是新栈顶");
            Assert.IsFalse(UIManager.IsOpen<FakePanelC>(), "目标之上的面板应被关掉");
            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "目标之下的面板不应受影响");
        }

        [Test]
        public async Task PopToRootAsync_KeepsOnlyBottomPanel()
        {
            await UIManager.OpenAsync<FakePanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");
            await UIManager.PushAsync<FakePanelC>("ui/c");

            await UIManager.PopToRootAsync();

            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "只保留最早打开的那一个");
            Assert.IsFalse(UIManager.IsOpen<FakePanelB>());
            Assert.IsFalse(UIManager.IsOpen<FakePanelC>());
            Assert.IsFalse(UIManager.CanGoBack);
        }

        #endregion

        #region 遍历安全

        [Test]
        public async Task Update_OnUpdateClosesSelf_DoesNotThrow()
        {
            var panel = await UIManager.OpenAsync<SelfClosingPanel>("ui/self");

            // 修复前：Update 内 foreach 遍历活动面板，面板自关时同步改集合
            // → InvalidOperationException（经 Forget 通路变成 LogException，测试判失败）
            UIManager.Update(0.016f, 0f);

            Assert.IsTrue(panel.WasUpdated, "OnUpdate 应被驱动过");
            Assert.IsFalse(UIManager.IsOpen<SelfClosingPanel>(), "面板应在自己的 OnUpdate 里被关掉");
        }

        #endregion
    }
}
