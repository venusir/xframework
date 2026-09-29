using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLocalization;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="LocalizationBootstrapStage"/> 相位阶段直连测试：不经管线，注入上下文执行
    /// <see cref="IPipelineStage.ExecuteAsync"/>，断言状态收敛到终态（无数据跳过路径 / 有数据初始化路径），
    /// 以及 <see cref="IBootstrapStage.Shutdown"/> 的反向清理。
    /// </summary>
    class LocalizationBootstrapStageTests
    {
        [SetUp]
        public void SetUp()
        {
            // 复位静态门面：用例之间共享静态态，不复位即互相污染
            LocalizationManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationManager.Destroy();
        }

        [Test]
        public void ExecuteAsync_WithoutInitData_CompletesWithWarning()
        {
            var stage = new LocalizationBootstrapStage();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[LocalizationBootstrapStage\] ExecuteAsync called but _initData is null"));

            var ctx = new PipelineStageContext();
            stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Completed, ctx.State, "无数据跳过路径也应写完成终态");
            Assert.AreEqual(1f, ctx.Progress, 0.001f);
        }

        [Test]
        public void ExecuteAsync_WithInitData_Completes()
        {
            var stage = new LocalizationBootstrapStage("zh_Hans", new Dictionary<string, string> { { "title", "你好" } });
            var ctx = new PipelineStageContext();

            try
            {
                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();
            }
            finally
            {
                // 清理 LocalizationManager 静态态，避免影响其他测试
                LocalizationManager.Destroy();
            }

            Assert.AreEqual(PipelineStageState.Completed, ctx.State);
            Assert.AreEqual(1f, ctx.Progress, 0.001f);
        }

        [Test]
        public void Shutdown_AfterInitialize_DestroysManager()
        {
            var stage = new LocalizationBootstrapStage("zh_Hans", new Dictionary<string, string> { { "title", "你好" } });

            stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();
            Assert.IsTrue(LocalizationManager.IsInitialized, "执行后门面应已就绪");

            stage.Shutdown();
            Assert.IsFalse(LocalizationManager.IsInitialized, "Shutdown 应让门面回到未初始化");
        }

        [Test]
        public void Shutdown_WithoutInitData_LeavesManuallyInitializedManagerAlone()
        {
            // 使用方自己初始化，随后登记一个没有数据的引导阶段——它会警告并空转，什么都没建。
            // 旧实现的 Shutdown 无条件 Destroy，会把使用方这份管理器一并销毁（改前必红）
            LocalizationManager.Initialize("en", new Dictionary<string, string> { { "title", "hello" } });

            var stage = new LocalizationBootstrapStage();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[LocalizationBootstrapStage\] ExecuteAsync called but _initData is null"));

            stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();
            stage.Shutdown();

            Assert.IsTrue(LocalizationManager.IsInitialized, "空转阶段不得销毁非自己初始化的管理器");
            Assert.AreEqual("en", LocalizationManager.CurrentLanguage, "使用方那份数据应原封不动");
        }

        [Test]
        public void Shutdown_WithoutRunningExecuteAsync_IsNoOp()
        {
            var stage = new LocalizationBootstrapStage();

            stage.Shutdown();

            Assert.IsFalse(LocalizationManager.IsInitialized);
        }

        [Test]
        public void Shutdown_IsIdempotent()
        {
            var stage = new LocalizationBootstrapStage("zh_Hans", new Dictionary<string, string> { { "title", "你好" } });
            stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();

            stage.Shutdown();
            stage.Shutdown(); // 第二次应是空操作，不抛

            Assert.IsFalse(LocalizationManager.IsInitialized);
        }

        [Test]
        public void ExecuteAsync_AfterShutdown_Reinitializes()
        {
            var stage = new LocalizationBootstrapStage("zh_Hans", new Dictionary<string, string> { { "title", "你好" } });

            stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();
            stage.Shutdown();
            stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();

            Assert.IsTrue(LocalizationManager.IsInitialized, "清理后重跑应能重新初始化");
            Assert.AreEqual("你好", LocalizationManager.Get("title"));
        }
    }
}
