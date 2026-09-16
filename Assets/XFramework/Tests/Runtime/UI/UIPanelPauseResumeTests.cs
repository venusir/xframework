using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XMessage;
using XFramework.XUI.Data;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 交互维度与更新维度的正交性测试。
    /// <para>此前 <c>OnBlur</c> 一件事管两维：既关交互、又置 <c>IsPaused</c>。于是子类无法表达
    /// 「失焦但仍要按低频更新」（倒计时）这类需求，也分不清自己收到的是哪个信号。
    /// 现拆为 <c>OnFocus/OnBlur</c>（交互）与 <c>OnPause/OnResume</c>（更新）。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelPauseResumeTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_PauseTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<UpdateRecordingPanel>();
            _factory.RegisterPanel<FakePanelB>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #region 两个维度的信号

        [Test]
        public async Task Push_PausesPreviousTop_ResumeOnPop()
        {
            var covered = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            Assert.IsTrue(covered.IsFocused, "刚打开的面板在栈顶，应有焦点");
            Assert.IsFalse(covered.IsPaused);

            await UIManager.PushAsync<FakePanelB>("ui/b");

            Assert.IsFalse(covered.IsFocused, "被覆盖后失焦");
            Assert.IsTrue(covered.IsPaused, "被覆盖后暂停每帧更新");
            Assert.IsTrue(covered.Logged("OnBlur"), "交互维度：OnBlur");
            Assert.IsTrue(covered.Logged("OnPause"), "更新维度：OnPause");

            await UIManager.PopAsync();

            Assert.IsTrue(covered.IsFocused, "回到栈顶后恢复焦点");
            Assert.IsFalse(covered.IsPaused, "恢复每帧更新");
            Assert.IsTrue(covered.Logged("OnResume"), "更新维度：OnResume");
        }

        [Test]
        public async Task FirstOpen_FiresOnFocus_AndIsFocused()
        {
            // 回归：OnOpenImpl 原先直接设 IsPaused/Raycaster 字段，子类重写的 OnFocus
            // 在首次打开时不会被调用——「首次打开」与「Push/Pop 恢复焦点」是两条路径。
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");

            Assert.IsTrue(panel.IsFocused);
            Assert.IsTrue(panel.Logged("OnFocus"), "首次打开也应走 OnFocus");
            Assert.IsFalse(panel.Logged("OnResume"),
                "对一个从未暂停过的面板调「恢复」是语义错位，不应触发");
        }

        #endregion

        #region 暂停只影响更新维度

        [Test]
        public async Task PausedPanel_IsStillOpen()
        {
            var covered = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");

            Assert.IsTrue(covered.IsPaused);
            Assert.IsTrue(covered.IsOpen, "暂停的是每帧更新，不是面板本身");
            Assert.IsTrue(UIManager.IsOpen<UpdateRecordingPanel>());
        }

        [Test]
        public async Task PausedPanel_StillReceivesLanguageChanged()
        {
            var covered = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");
            Assert.AreEqual(0, covered.LanguageChangedCount, "前置条件：尚未收到语言切换");

            MessageManager.Publish(new XLocalization.LanguageChangedMessage("en"));

            Assert.AreEqual(1, covered.LanguageChangedCount,
                "暂停只关掉每帧派发；被覆盖的面板仍应刷新文本，否则 Pop 回来会是旧语言");
        }

        [Test]
        public async Task PausedPanel_NotDrivenByUpdate()
        {
            var covered = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            UpdateManager.Tick(0.016f);

            int before = covered.UpdateCount;
            Assert.Greater(before, 0, "前置条件：正在被驱动");

            await UIManager.PushAsync<FakePanelB>("ui/b");
            for (int i = 0; i < 5; i++)
                UpdateManager.Tick(0.016f * (i + 2));

            Assert.AreEqual(before, covered.UpdateCount, "暂停后不该再被派发");
        }

        #endregion

        #region 交互维度

        [Test]
        public async Task Resume_RestoresInteractivity()
        {
            var covered = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");
            Assert.IsFalse(covered.Raycaster.enabled, "失焦后交互关闭");

            await UIManager.PopAsync();

            Assert.IsTrue(covered.Raycaster.enabled, "恢复焦点后交互打开");
        }

        #endregion
    }
}
