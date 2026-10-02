using System;
using NUnit.Framework;
using UnityEngine;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// <see cref="UIBootstrapStage"/>：初始化、<b>已初始化即早退不接管</b>、以及「谁初始化谁清理」。
    /// <para><b>夹具纪律</b>：<c>UIManager</c> 是全局静态门面，PlayMode 下所有用例共享一个 player——
    /// SetUp/TearDown 都 <c>Destroy()</c>，否则下一页用例会被上一页的实例污染。</para>
    /// </summary>
    class UIBootstrapStageTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            UIManager.Destroy();
            _root = new GameObject("ui-root", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void ExecuteAsync_Uninitialized_InitializesManager()
        {
            var stage = new UIBootstrapStage(_root.transform);
            var ctx = new PipelineStageContext();

            stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

            Assert.IsTrue(UIManager.IsInitialized);
            Assert.AreEqual(PipelineStageState.Completed, ctx.State);
            Assert.AreEqual(1f, ctx.Progress, 0.001f);
        }

        [Test]
        public void ExecuteAsync_AlreadyInitialized_DoesNotTakeOver()
        {
            // UIRootNode.Awake 或使用方手动初始化过的那种情形
            UIManager.Initialize(_root.transform);
            var other = new GameObject("other-root", typeof(RectTransform));
            try
            {
                var stage = new UIBootstrapStage(other.transform);
                var ctx = new PipelineStageContext();

                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

                Assert.AreEqual(PipelineStageState.Completed, ctx.State, "已初始化也要正常写完成终态");
                Assert.AreSame(_root.transform, UIManager.UIRoot,
                    "已经初始化时不该重复 Initialize（只会打重复告警）或改用另一个根");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void Shutdown_WithoutHavingInitialized_LeavesManagerAlone()
        {
            UIManager.Initialize(_root.transform);
            var stage = new UIBootstrapStage(_root.transform);

            stage.Shutdown();   // 从未 Execute（或 Execute 走的是早退分支）

            Assert.IsTrue(UIManager.IsInitialized,
                "空转阶段不得销毁别人初始化的管理器——这是 Localization 那个守卫用例的同族");
        }

        [Test]
        public void Shutdown_AfterInitialize_DestroysManager()
        {
            var stage = new UIBootstrapStage(_root.transform);
            stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();

            stage.Shutdown();

            Assert.IsFalse(UIManager.IsInitialized);
        }

        [Test]
        public void Shutdown_IsIdempotent()
        {
            var stage = new UIBootstrapStage(_root.transform);
            stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();

            stage.Shutdown();
            stage.Shutdown();   // 第二次是空操作，不抛
        }

        [Test]
        public void Ctor_NullRoot_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new UIBootstrapStage(null));
        }

        [Test]
        public void Phase_IsUI()
        {
            Assert.AreEqual(BootstrapPhases.UI, new UIBootstrapStage(_root.transform).Phase);
        }
    }
}
