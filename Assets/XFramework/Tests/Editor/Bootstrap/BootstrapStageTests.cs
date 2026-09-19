using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XAsset;
using XFramework.XData;
using XFramework.XPipeline;
using XFramework.XSave;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 框架内置引导阶段直连测试：不经管线，注入上下文执行 <see cref="IPipelineStage.ExecuteAsync"/>，
    /// 断言描述/终态写入与模块门面初始化（Asset 经假实现注入；静态门面在 finally 清理）。
    /// <para>对应旧节点树时代的 <c>BootstrapStageTests</c>——阶段改成纯 C# 类后，用例语义不变。</para>
    /// </summary>
    class BootstrapStageTests
    {
        [Test]
        public void AssetBootstrapStage_AlreadyInitialized_SkipsInit()
        {
            var fake = new FakeAssetManager();
            AssetManager.SetInstance(fake);
            try
            {
                var stage = new AssetBootstrapStage();
                var ctx = new PipelineStageContext();

                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

                Assert.AreEqual(PipelineStageState.Completed, ctx.State, "已初始化跳过路径也应写完成终态");
                Assert.AreEqual(0, fake.InitCallCount, "已初始化不得重复初始化");
            }
            finally
            {
                AssetManager.Destroy();
            }
        }

        [Test]
        public void AssetBootstrapStage_Uninitialized_InitializesWithProgressRelay()
        {
            AssetManager.Destroy();
            var fake = new FakeAssetManager();
            AssetManager.ImplFactory = () => fake;
            try
            {
                var stage = new AssetBootstrapStage();
                var ctx = new PipelineStageContext();

                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

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
        public void DataBootstrapStage_CompletesSynchronously()
        {
            var stage = new DataBootstrapStage();
            var ctx = new PipelineStageContext();
            try
            {
                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

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
        public async Task SaveBootstrapStage_CompletesAfterRecovery()
        {
            var stage = new SaveBootstrapStage();
            var ctx = new PipelineStageContext();
            try
            {
                // 阶段会 await 恢复扫描，因此必须 await 而不是阻塞主线程等待：
                // 阶段末尾要切回主线程写上下文（PipelineStageContext 有越线程写入检测），
                // 阻塞等待会与其主线程恢复语义冲突而死锁。
                //
                // 刻意不注入 Provider：FileManager 零配置自初始化为桌面实现，于是这条用例是真实
                // DesktopFileProvider 的唯一集成覆盖——而恢复扫描是「全程不切回主线程」的路径，
                // 正是「域根不能在子线程解析」这类缺陷唯一会露头的地方（替身 Provider 在构造函数里
                // 就把根路径定死、结构性绕开 Unity API，这一整类缺陷在它上面不可见）。历史上它确实
                // 因此独自红了 7 次。代价是恢复扫描作用在真实的 persistentDataPath 上，对合规存档
                // 它是幂等的（载荷在则只清 .tmp 残留并补齐侧车）。
                await stage.ExecuteAsync(ctx, default);

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
