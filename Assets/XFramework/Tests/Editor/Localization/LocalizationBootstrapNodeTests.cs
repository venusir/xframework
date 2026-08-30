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
    /// LocalizationBootstrapNode 加载状态写入测试：补足 LoadProgress 契约后，被 Loader 调度时状态必须收敛到终态。
    /// </summary>
    class LocalizationBootstrapNodeTests
    {
        [Test]
        public void LoadAsync_WithoutInitData_CompletesWithWarning()
        {
            var node = new LocalizationBootstrapNode();
            var progress = new LoadProgress();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[LocalizationBootstrapNode\] LoadAsync called but _initData is null"));

            node.LoadAsync(progress, default).GetAwaiter().GetResult();

            Assert.AreEqual(LoadState.Completed, progress.State, "无数据跳过路径也应写完成状态");
            Assert.AreEqual(1f, progress.Progress, 0.001f);
        }

        [Test]
        public void LoadAsync_WithInitData_Completes()
        {
            var node = new LocalizationBootstrapNode();
            node.SetInitData("zh_Hans", new Dictionary<string, string> { { "title", "你好" } });
            var progress = new LoadProgress();

            try
            {
                node.LoadAsync(progress, default).GetAwaiter().GetResult();
            }
            finally
            {
                // 清理 LocalizationManager 静态态，避免影响其他测试
                LocalizationManager.Destroy();
            }

            Assert.AreEqual(LoadState.Completed, progress.State);
            Assert.AreEqual(1f, progress.Progress, 0.001f);
        }
    }
}
