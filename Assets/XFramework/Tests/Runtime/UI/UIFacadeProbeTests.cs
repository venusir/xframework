using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XUI.View;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 门面探测型读接口的契约测试：<b>未初始化时它们回答「什么都没有」，而不是抛异常</b>。
    /// <para><b>为什么必须有这一条</b>：这条契约此前只写在两处散文里——<see cref="IUIManager.GetState"/>
    /// 与 <see cref="IUIManager.OpenCount"/> 的 XML 文档（「未初始化时返回 0，不抛异常，便于在场景加载
    /// 早期探测」），以及 <c>UIManagerImpl</c> 查询属性上方那句「它们读的是始终有效的内存状态」——
    /// 而静态门面当时给这些成员统一套了 <c>EnsureGlobalInitialized()</c>，实测行为与两处文档都相反。
    /// 散文约束不住的行为，就用测试钉住。</para>
    /// <para><b>与操作类的分界</b>：读「现在有没有面板/遮罩」不抛；真的去开/关/推面板仍然照抛——
    /// 那才是「还没准备好就用」的真实错误。两者都在本 fixture 里钉住，免得修一边倒掉另一边。</para>
    /// </summary>
    [TestFixture]
    public class UIFacadeProbeTests
    {
        #region 前置状态

        [SetUp]
        public void SetUp()
        {
            // 强制回到「未初始化」：本 fixture 全部断言都以它为前置，若沿用上一个 fixture 残留的
            // 实例，这些用例会变成在测另一种状态。Destroy 幂等，且对未初始化的门面安全
            // （其内部各段都有 null 守卫），故不需要先判 IsInitialized。
            UIManager.Destroy();
        }

        // 刻意没有 TearDown：本 fixture 从不 Initialize，也没有可泄漏的驱动器，
        // 收尾反而要依赖 SetUp 的那句 Destroy——多一个 TearDown 只会给出「它会留下什么」的错觉。

        #endregion

        #region 门面生命周期自身的探测

        [Test]
        public void LifecycleProbes_BeforeInitialize_AreEmpty()
        {
            Assert.IsFalse(UIManager.IsInitialized);
            Assert.IsNull(UIManager.UIRoot, "未初始化时 UIRoot 为 null 而非抛异常");
        }

        #endregion

        #region 探测型读接口

        [Test]
        public void GetState_BeforeInitialize_ReturnsAllZero()
        {
            var state = UIManager.GetState();

            Assert.AreEqual(0, state.OpenCount);
            Assert.AreEqual(0, state.InFlightOpenCount);
            Assert.AreEqual(0, state.MaskRefCount);
            Assert.IsFalse(state.IsMaskShowing);
            Assert.IsFalse(state.CanGoBack);
        }

        [Test]
        public void DumpState_BeforeInitialize_ReportsUninitializedInsteadOfThrowing()
        {
            var dump = UIManager.DumpState();

            Assert.IsNotNull(dump);
            StringAssert.Contains("尚未初始化", dump, "调试导出应当自己说清「还没初始化」，而不是抛给窗口去接");
        }

        [Test]
        public void QueryProbes_BeforeInitialize_AreEmpty()
        {
            Assert.AreEqual(0, UIManager.OpenCount);
            Assert.IsFalse(UIManager.IsAnyOpen);
            Assert.IsFalse(UIManager.CanGoBack);
            Assert.IsFalse(UIManager.IsMaskShowing);
        }

        [Test]
        public void Panels_BeforeInitialize_IsEmptyViewNotNull()
        {
            var panels = UIManager.Panels;

            Assert.IsNotNull(panels, "返回空视图而非 null：调用方不该被逼着先判 IsInitialized 再判 null");
            Assert.AreEqual(0, panels.Count);
        }

        #endregion

        #region 操作类仍然照抛

        [Test]
        public void TopPanelQuery_BeforeInitialize_Throws()
        {
            // GetTopPanel / CopyPanels 与上面那几个不同：它们的实现会先剪枝再取，
            // 属「操作」而非纯读，故门面与实现都保持抛异常
            Assert.Throws<InvalidOperationException>(() => UIManager.GetTopPanel());
            Assert.Throws<InvalidOperationException>(() => UIManager.CopyPanels(new List<UIPanelBase>()));
            Assert.Throws<InvalidOperationException>(() => UIManager.CopyPanelsInLayer(100, new List<UIPanelBase>()));
        }

        [Test]
        public void PanelLookup_BeforeInitialize_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => UIManager.IsOpen<FakePanel>());
            Assert.Throws<InvalidOperationException>(() => UIManager.GetPanel<FakePanel>());
        }

        [Test]
        public void Operations_BeforeInitialize_Throw()
        {
            // 门面转发体不是 async 方法，异常在同步段抛出，故用 Assert.Throws 而非 ThrowsAsync
            Assert.Throws<InvalidOperationException>(() => UIManager.OpenAsync<FakePanel>("ui/a"));
            Assert.Throws<InvalidOperationException>(() => UIManager.CloseAllAsync());
            Assert.Throws<InvalidOperationException>(() => UIManager.PopAsync());
            Assert.Throws<InvalidOperationException>(() => UIManager.ShowTipAsync("hello"));
            Assert.Throws<InvalidOperationException>(() => UIManager.SetLayerVisibility(100, false));
            Assert.Throws<InvalidOperationException>(() => UIManager.Update(0.016f, 0f));
        }

        #endregion
    }
}
