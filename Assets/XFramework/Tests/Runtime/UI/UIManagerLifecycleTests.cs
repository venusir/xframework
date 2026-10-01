using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUI.Controller;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 门面生命周期与「注入非自带实现」的降级语义测试。
    /// <para>这组行为此前只写在文档与注释里，零断言：<c>SetController</c> 对注入实现的降级分支、
    /// 「注入的实例不归门面所有」、重复 <c>Initialize</c> 的告警、<c>SetInstance(null)</c> 的参数防御、
    /// 以及 <c>Destroy</c> 之后各成员的归零与「可重新初始化」。<see cref="UIFacadeProbeTests"/> 覆盖的是
    /// 「从未初始化」，本 fixture 覆盖的是「初始化过、又销毁」——两者是不同状态，不能互相代偿。</para>
    /// </summary>
    [TestFixture]
    public class UIManagerLifecycleTests
    {
        #region 前置状态

        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_LifecycleTest", typeof(RectTransform));
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #endregion

        #region 注入非自带实现的降级

        /// <summary>
        /// 注入自定义实现后调 <c>SetController</c>：只告警，不生效也不抛。
        /// <para>控制器需要一个 <c>UIManagerImpl</c> 的注入点，自定义实现没有——此前这条分支零用例，
        /// 照 <c>README</c> §12 的示例（先 SetInstance(mock) 再 SetController）写会静默拿不到拦截。</para>
        /// </summary>
        [Test]
        public void SetController_WithInjectedCustomManager_WarnsInsteadOfApplying()
        {
            UIManager.SetInstance(new FakeUIManager());

            LogAssert.Expect(LogType.Warning, new Regex("Current instance is not UIManagerImpl"));

            Assert.DoesNotThrow(() => UIManager.SetController(new UIDefaultController()),
                "控制器只对自带实现生效——注入实现时留一条告警即可，不该把调用方打崩");
        }

        /// <summary>
        /// 换掉一个**注入**的实例时，不得销毁它：它不归门面所有，替调用方拆掉属于越权。
        /// <para>对照的另一半（换掉门面自建实例时要销毁，连同它的面板回池）已在
        /// <c>UIPanelTierTests.SetInstance_ReleasesPreviousInstanceAndItsTierDrivers</c> 里钉住。</para>
        /// </summary>
        [Test]
        public void SetInstance_ReplacingInjectedInstance_DoesNotDisposeIt()
        {
            var first = new FakeUIManager();
            UIManager.SetInstance(first);

            var second = new FakeUIManager();
            UIManager.SetInstance(second);

            Assert.IsFalse(first.Disposed, "注入的实例归调用方管：换实例只摘驱动器，不许销毁它");
            Assert.IsFalse(second.Disposed, "新换上的实例同样不动");
        }

        #endregion

        #region 生命周期

        /// <summary>重复 <c>Initialize</c>：告警并忽略，根节点保持第一次的那个。</summary>
        [Test]
        public void Initialize_SecondCall_WarnsAndKeepsFirstRoot()
        {
            var firstRoot = UIManager.UIRoot;
            var other = new GameObject("UIRoot_Second", typeof(RectTransform));

            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("called more than once"));

                UIManager.Initialize(other.transform);

                Assert.AreSame(firstRoot, UIManager.UIRoot, "重复调用只告警并忽略，不该把根换掉");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        /// <summary><c>SetInstance(null)</c> 是参数误用，按仓内惯例抛 <see cref="ArgumentNullException"/>。</summary>
        [Test]
        public void SetInstance_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => UIManager.SetInstance(null));
        }

        /// <summary>
        /// <c>Destroy</c> 之后：操作类照抛、探测类归零、分档驱动器记账清零。
        /// <para>「归零」这半此前没有任何用例（唯一被断过的是 <c>IsInitialized == false</c>）；
        /// 而 <c>TierDriverCount</c> 若不清零，调用方会看到与实况相反的诊断值。</para>
        /// </summary>
        [Test]
        public void AfterDestroy_OperationsThrow_AndProbesGoEmpty()
        {
            UIManager.Destroy();

            Assert.Throws<InvalidOperationException>(() => UIManager.OpenAsync<FakePanel>("ui/a"));
            Assert.Throws<InvalidOperationException>(() => UIManager.Update(0.016f, 0f));

            Assert.IsFalse(UIManager.IsInitialized);
            Assert.IsNull(UIManager.UIRoot);
            Assert.AreEqual(0, UIManager.TierDriverCount, "驱动器记账必须随实例一起清零");
            Assert.AreEqual(0, UIManager.OpenCount);
            Assert.IsFalse(UIManager.IsAnyOpen);
            Assert.IsFalse(UIManager.CanGoBack);
        }

        /// <summary>
        /// <c>Destroy</c> 不是单向闩锁：重新 <c>Initialize</c> 即可再次使用，驱动器也重新接上。
        /// <para>异常消息里教用户做的正是这件事（「请先调用 UIManager.Initialize(uiRoot)」），
        /// 照做必须能成功——仓内有过反面先例（Settings 的销毁后不可用）。</para>
        /// </summary>
        [Test]
        public void AfterDestroy_CanInitializeAgain()
        {
            UIManager.Destroy();

            UIManager.Initialize(_root.transform);

            Assert.IsTrue(UIManager.IsInitialized);
            Assert.AreSame(_root.transform, UIManager.UIRoot);
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0), "重新初始化后每帧驱动器重新接上");
        }

        #endregion
    }
}
