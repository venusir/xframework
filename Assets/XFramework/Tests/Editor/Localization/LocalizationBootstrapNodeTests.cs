using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XPipeline;
using XFramework.XLocalization;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// LocalizationBootstrapNode 相位阶段直连测试:不经管线,注入上下文执行 <see cref="IPipelineStage.ExecuteAsync"/>,
    /// 断言状态收敛到终态(无 initData 跳过路径 / 有 initData 初始化路径)。
    /// </summary>
    class LocalizationBootstrapNodeTests
    {
        [Test]
        public void ExecuteAsync_WithoutInitData_CompletesWithWarning()
        {
            var node = new LocalizationBootstrapNode();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[LocalizationBootstrapNode\] ExecuteAsync called but _initData is null"));

            var ctx = new PipelineStageContext();
            node.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

            Assert.AreEqual(PipelineStageState.Completed, ctx.State, "无数据跳过路径也应写完成终态");
            Assert.AreEqual(1f, ctx.Progress, 0.001f);
        }

        [Test]
        public void ExecuteAsync_WithInitData_Completes()
        {
            var node = new LocalizationBootstrapNode();
            node.SetInitData("zh_Hans", new Dictionary<string, string> { { "title", "你好" } });
            var ctx = new PipelineStageContext();

            try
            {
                node.ExecuteAsync(ctx, default).GetAwaiter().GetResult();
            }
            finally
            {
                // 清理 LocalizationManager 静态态，避免影响其他测试
                LocalizationManager.Destroy();
            }

            Assert.AreEqual(PipelineStageState.Completed, ctx.State);
            Assert.AreEqual(1f, ctx.Progress, 0.001f);
        }
    }
}
