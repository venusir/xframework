using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using XFramework.XBootstrap;
using XFramework.XInput.Default;
using XFramework.XPipeline;

namespace XFramework.XInput.Tests
{
    /// <summary>
    /// <see cref="InputBootstrapStage"/>：初始化、<b>已初始化即早退不接管</b>、以及「谁初始化谁清理」。
    /// <para><b>夹具纪律</b>：<c>InputManager</c> 是全局静态门面，PlayMode 下所有用例共享一个 player——
    /// SetUp/TearDown 都 <c>Destroy()</c>。</para>
    /// </summary>
    class InputBootstrapStageTests
    {
        [SetUp]
        public void SetUp()
        {
            InputManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            InputManager.Destroy();
        }

        /// <summary>构造单 map 单 action 的程序化资产；map 名沿用默认约定 <c>Player</c>。</summary>
        private static InputActionAsset CreateAsset()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Player");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            asset.AddActionMap(map);
            return asset;
        }

        [Test]
        public void ExecuteAsync_Uninitialized_InitializesManager()
        {
            var stage = new InputBootstrapStage(new InputSystemOptions { Asset = CreateAsset() });
            var ctx = new PipelineStageContext();

            stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

            Assert.IsTrue(InputManager.IsInitialized);
            Assert.AreEqual(PipelineStageState.Completed, ctx.State);
            Assert.AreEqual(1f, ctx.Progress, 0.001f);
        }

        [Test]
        public void ExecuteAsync_AlreadyInitialized_DoesNotTakeOver()
        {
            InputManager.Initialize(new InputSystemOptions { Asset = CreateAsset() });
            var providerBefore = InputManager.Provider;

            // 另一个资产：若被调用，Initialize 会新建并接管一个提供者
            var stage = new InputBootstrapStage(new InputSystemOptions { Asset = CreateAsset() });
            var ctx = new PipelineStageContext();

            stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Completed, ctx.State, "已初始化也要正常写完成终态");
            Assert.AreSame(providerBefore, InputManager.Provider,
                "已经初始化时不该重复 Initialize（只会打重复告警）或换掉提供者");
        }

        [Test]
        public void Shutdown_WithoutHavingInitialized_LeavesManagerAlone()
        {
            InputManager.Initialize(new InputSystemOptions { Asset = CreateAsset() });
            var stage = new InputBootstrapStage(new InputSystemOptions { Asset = CreateAsset() });

            stage.Shutdown();   // 从未 Execute（或 Execute 走的是早退分支）

            Assert.IsTrue(InputManager.IsInitialized, "空转阶段不得销毁别人初始化的管理器");
        }

        [Test]
        public void Shutdown_AfterInitialize_DestroysManager()
        {
            var stage = new InputBootstrapStage(new InputSystemOptions { Asset = CreateAsset() });
            stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();

            stage.Shutdown();

            Assert.IsFalse(InputManager.IsInitialized);
        }

        [Test]
        public void Ctor_NullOptions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new InputBootstrapStage(null));
        }

        [Test]
        public void Phase_IsInput()
        {
            Assert.AreEqual(BootstrapPhases.Input,
                new InputBootstrapStage(new InputSystemOptions { Asset = CreateAsset() }).Phase);
        }
    }
}
