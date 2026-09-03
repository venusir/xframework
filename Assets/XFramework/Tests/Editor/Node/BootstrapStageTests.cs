using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XAsset;
using XFramework.XData;
using XFramework.XNode;
using XFramework.XPipeline;
using XFramework.XSave;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 引导节点相位阶段直连测试:不经管线,注入上下文执行 <see cref="IPipelineStage.ExecuteAsync"/>,
    /// 断言描述/终态写入与模块门面初始化(Asset 经假实现注入;静态门面在 finally 清理)。
    /// </summary>
    class BootstrapStageTests
    {
        [Test]
        public void AssetBootstrapNode_AlreadyInitialized_SkipsInit()
        {
            var fake = new FakeAssetManager();
            AssetManager.SetInstance(fake);
            try
            {
                var node = new AssetBootstrapNode();
                var ctx = new PipelineStageContext();

                node.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

                Assert.AreEqual(PipelineStageState.Completed, ctx.State, "已初始化跳过路径也应写完成终态");
                Assert.AreEqual(0, fake.InitCallCount, "已初始化不得重复初始化");
            }
            finally
            {
                AssetManager.Destroy();
            }
        }

        [Test]
        public void AssetBootstrapNode_Uninitialized_InitializesWithProgressRelay()
        {
            AssetManager.Destroy();
            var fake = new FakeAssetManager();
            AssetManager.ImplFactory = () => fake;
            try
            {
                var node = new AssetBootstrapNode();
                var ctx = new PipelineStageContext();

                node.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

                Assert.AreEqual(1, fake.InitCallCount, "未初始化应经门面初始化底层实例");
                Assert.IsNotNull(fake.LastInitProgress, "应把进度直写桥(AssetInitReport 接收方)传给初始化");
                Assert.IsTrue(AssetManager.IsInitialized);
                Assert.AreEqual(PipelineStageState.Completed, ctx.State);
                Assert.AreEqual(1f, ctx.Progress, 0.001f);
            }
            finally
            {
                AssetManager.ImplFactory = null;
                AssetManager.Destroy();
            }
        }

        [Test]
        public void GameDataNode_CompletesSynchronously()
        {
            var node = new GameDataNode();
            var ctx = new PipelineStageContext();
            try
            {
                node.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

                Assert.AreEqual(PipelineStageState.Completed, ctx.State);
                Assert.AreEqual(1f, ctx.Progress, 0.001f);
            }
            finally
            {
                // 清理 DataManager 静态态,避免影响其他测试
                DataManager.Shutdown();
            }
        }

        [Test]
        public void SaveBootstrapNode_CompletesSynchronously()
        {
            var node = new SaveBootstrapNode();
            var ctx = new PipelineStageContext();
            try
            {
                node.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

                Assert.AreEqual(PipelineStageState.Completed, ctx.State);
                Assert.AreEqual(1f, ctx.Progress, 0.001f);
            }
            finally
            {
                // 清理 SaveManager 静态态,避免影响其他测试
                SaveManager.Shutdown();
            }
        }
    }
}
