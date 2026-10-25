using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.XData;
using XFramework.XLocalization;
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
        public void AssetBootstrapStage_WithOptions_PassesThemToInitialize()
        {
            AssetManager.Destroy();
            var fake = new FakeAssetManager();
            AssetManager.ImplFactory = () => fake;
            try
            {
                var options = new AssetInitOptions { PackageName = "MyPack" };
                var stage = new AssetBootstrapStage(options);
                var ctx = new PipelineStageContext();

                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

                Assert.AreSame(options, stage.Options, "字段面注入的应该就是同一个实例");
                Assert.AreSame(options, fake.LastInitOptions,
                    "阶段携带的 options 必须真的传下去——否则字段面就是摆设");
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
                // DesktopFileProvider 的唯一集成覆盖——而恢复扫描的文件原语在池线程执行（读侧车时
                // 切回主线程），正是「域根不能在子线程解析」这类缺陷唯一会露头的地方（替身 Provider 在构造函数里
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

        /// <summary>
        /// 所有权契约：阶段执行时发现门面已初始化（使用方手动注入 / 上一轮运行留下），
        /// <c>Shutdown</c> 不得销毁它——否则会把使用方的管理器一起拆掉。
        /// </summary>
        [Test]
        public void AssetBootstrapStage_AlreadyInitialized_ShutdownDoesNotDestroy()
        {
            var fake = new FakeAssetManager();
            AssetManager.SetInstance(fake);
            try
            {
                var stage = new AssetBootstrapStage();
                var ctx = new PipelineStageContext();

                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();
                stage.Shutdown();

                Assert.IsTrue(AssetManager.IsInitialized,
                    "阶段未初始化它，Shutdown 不得销毁使用方注入的实例");
            }
            finally
            {
                AssetManager.Destroy();
            }
        }

        [Test]
        public void AssetBootstrapStage_InitializedByStage_ShutdownDestroys()
        {
            AssetManager.Destroy();
            var fake = new FakeAssetManager();
            AssetManager.ImplFactory = () => fake;
            try
            {
                var stage = new AssetBootstrapStage();
                var ctx = new PipelineStageContext();

                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();
                Assert.IsTrue(AssetManager.IsInitialized, "前提：本阶段应已完成初始化");

                stage.Shutdown();

                Assert.IsFalse(AssetManager.IsInitialized, "本阶段初始化的服务应由本阶段清理");
            }
            finally
            {
                AssetManager.ImplFactory = null;
                AssetManager.Destroy();
            }
        }

        [Test]
        public void AssetBootstrapStage_InitializedByStage_ThenSetInstance_ShutdownDoesNotDestroyCustom()
        {
            // R4-AST-002：阶段初始化成功后使用方换入自定义实例，Shutdown 不得销毁它。
            AssetManager.Destroy();
            AssetManager.ImplFactory = () => new FakeAssetManager();
            try
            {
                var stage = new AssetBootstrapStage();
                stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();

                var custom = new FakeAssetManager();
                AssetManager.SetInstance(custom);

                stage.Shutdown();

                Assert.IsTrue(AssetManager.IsInitialized, "使用方换入的实例不得被阶段销毁");
                Assert.IsFalse(custom.Disposed);
            }
            finally
            {
                AssetManager.ImplFactory = null;
                AssetManager.Destroy();
            }
        }

        [Test]
        public void AssetBootstrapStage_JoinedInflight_ShutdownDoesNotDestroyExternalInit()
        {
            // R4-AST-002：另一调用方已在途初始化时，本阶段只是加入，Shutdown 不得销毁。
            AssetManager.Destroy();
            var tcs = new UniTaskCompletionSource();
            var fake = new FakeAssetManager { InitTask = tcs.Task };
            AssetManager.ImplFactory = () => fake;
            try
            {
                var manual = AssetManager.InitializeAsync();
                var stage = new AssetBootstrapStage();
                var stageTask = stage.ExecuteAsync(new PipelineStageContext(), default);

                tcs.TrySetResult();
                manual.GetAwaiter().GetResult();
                stageTask.GetAwaiter().GetResult();

                stage.Shutdown();

                Assert.IsTrue(AssetManager.IsInitialized, "阶段只是加入在途初始化，Shutdown 不得销毁别人的实例");
                Assert.IsFalse(fake.Disposed);
            }
            finally
            {
                AssetManager.ImplFactory = null;
                AssetManager.Destroy();
            }
        }

        [Test]
        public void AssetBootstrapStage_InitFails_ShutdownDoesNotDestroyLaterManualInit()
        {
            AssetManager.Destroy();
            var failing = new FakeAssetManager
            {
                InitTask = UniTask.FromException(new System.InvalidOperationException("boom")),
            };
            AssetManager.ImplFactory = () => failing;
            try
            {
                var stage = new AssetBootstrapStage();
                Assert.Throws<System.InvalidOperationException>(() =>
                    stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult(),
                    "初始化失败应上抛");

                // 失败后使用方手动注入另一份实例；本阶段的 Shutdown 不得销毁它
                AssetManager.SetInstance(new FakeAssetManager());
                stage.Shutdown();

                Assert.IsTrue(AssetManager.IsInitialized,
                    "失败路径必须复位所有权标记，Shutdown 不得销毁使用方手动初始化的实例（AST-004）");
            }
            finally
            {
                AssetManager.ImplFactory = null;
                AssetManager.Destroy();
            }
        }

        [Test]
        public void DataBootstrapStage_AlreadyInitialized_ShutdownDoesNotShutdown()
        {
            DataManager.Initialize(new DataManagerImpl());
            try
            {
                var stage = new DataBootstrapStage();
                var ctx = new PipelineStageContext();

                LogAssert.Expect(LogType.Warning,
                    new Regex(@"\[Data\] DataManager\.Initialize 被重复调用"));
                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();

                stage.Shutdown();

                Assert.IsTrue(DataManager.IsInitialized,
                    "阶段未初始化它，Shutdown 不得注销使用方注入的实现");
            }
            finally
            {
                DataManager.Shutdown();
            }
        }

        [Test]
        public void DataBootstrapStage_InitializedByStage_ShutdownShutsDown()
        {
            DataManager.Shutdown();
            try
            {
                var stage = new DataBootstrapStage();
                var ctx = new PipelineStageContext();

                stage.ExecuteAsync(ctx, default).GetAwaiter().GetResult();
                Assert.IsTrue(DataManager.IsInitialized, "前提：本阶段应已完成初始化");

                stage.Shutdown();

                Assert.IsFalse(DataManager.IsInitialized, "本阶段初始化的服务应由本阶段清理");
            }
            finally
            {
                DataManager.Shutdown();
            }
        }

        [Test]
        public void DataBootstrapStage_RunTwice_ShutdownStillShutsDown()
        {
            DataManager.Shutdown();
            try
            {
                var stage = new DataBootstrapStage();
                stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();

                // 重跑：第二次 wasInitialized 为真，所有权标记不得被覆写成 false
                stage.ExecuteAsync(new PipelineStageContext(), default).GetAwaiter().GetResult();

                stage.Shutdown();

                Assert.IsFalse(DataManager.IsInitialized,
                    "重跑后 Shutdown 仍应注销第一次由本阶段初始化的门面（DATA-002）");
            }
            finally
            {
                DataManager.Shutdown();
            }
        }

        [Test]
        public async Task SaveBootstrapStage_AlreadyInitialized_ShutdownDoesNotShutdown()
        {
            SaveManager.Shutdown();
            SaveManager.Initialize();
            try
            {
                var stage = new SaveBootstrapStage();
                var ctx = new PipelineStageContext();

                LogAssert.Expect(LogType.Warning,
                    new Regex(@"\[Save\] SaveManager\.Initialize 被重复调用"));
                await stage.ExecuteAsync(ctx, default);

                stage.Shutdown();

                Assert.IsTrue(SaveManager.IsInitialized,
                    "阶段未初始化它，Shutdown 不得注销使用方注入的实现");
            }
            finally
            {
                SaveManager.Shutdown();
            }
        }

        [Test]
        public async Task SaveBootstrapStage_InitializedByStage_ShutdownShutsDown()
        {
            SaveManager.Shutdown();
            try
            {
                var stage = new SaveBootstrapStage();
                var ctx = new PipelineStageContext();

                await stage.ExecuteAsync(ctx, default);
                Assert.IsTrue(SaveManager.IsInitialized, "前提：本阶段应已完成初始化");

                stage.Shutdown();

                Assert.IsFalse(SaveManager.IsInitialized, "本阶段初始化的服务应由本阶段清理");
            }
            finally
            {
                SaveManager.Shutdown();
            }
        }

        [Test]
        public async Task SaveBootstrapStage_RunTwice_ShutdownStillShutsDown()
        {
            SaveManager.Shutdown();
            try
            {
                var stage = new SaveBootstrapStage();
                await stage.ExecuteAsync(new PipelineStageContext(), default);

                // 重跑：第二次 wasInitialized 为真（重复 Initialize 告警），所有权标记不得被覆写成 false
                LogAssert.Expect(LogType.Warning,
                    new Regex(@"\[Save\] SaveManager\.Initialize 被重复调用"));
                await stage.ExecuteAsync(new PipelineStageContext(), default);

                stage.Shutdown();

                Assert.IsFalse(SaveManager.IsInitialized,
                    "重跑后 Shutdown 仍应注销第一次由本阶段初始化的门面（SAVE-005）");
            }
            finally
            {
                SaveManager.Shutdown();
            }
        }

        [Test]
        public void BuiltInStages_PhaseMatchesConstants()
        {
            // 防「改了常量、忘了改阶段」的单边漂移——两处必须同源
            Assert.AreEqual(BootstrapPhases.Asset, new AssetBootstrapStage().Phase);
            Assert.AreEqual(BootstrapPhases.Data, new DataBootstrapStage().Phase);
            Assert.AreEqual(BootstrapPhases.Save, new SaveBootstrapStage().Phase);
            Assert.AreEqual(BootstrapPhases.Localization, new LocalizationBootstrapStage().Phase);

            // Input / UI 两个阶段需要构造参数（资产 / 场景 Transform），各自的相位断言在它们的
            // 夹具里（InputBootstrapStageTests.Phase_IsInput / UIBootstrapStageTests.Phase_IsUI）——
            // 放在那里就不必在 EditMode 里造一个场景对象
        }

        [Test]
        public void RegisterDefaults_ThenUnregister_ThenRegister_ReplacesBuiltInOutOfOrder()
        {
            Bootstrap.Clear();
            try
            {
                // README 推荐的替换姿势：与调用顺序解耦（DefaultGameLauncher 在 Awake 里登记默认组合也照用）
                Bootstrap.RegisterDefaults();
                Assert.AreEqual(1, Bootstrap.Unregister<SaveBootstrapStage>(), "应摘掉内置的 Save 阶段");

                var custom = new SaveBootstrapStage();
                Bootstrap.Register(custom);

                IBootstrapStage found = null;
                for (int i = 0; i < Bootstrap.Stages.Count; i++)
                {
                    if (Bootstrap.Stages[i] is SaveBootstrapStage)
                    {
                        found = Bootstrap.Stages[i];
                        break;
                    }
                }

                Assert.AreEqual(3, Bootstrap.Stages.Count, "替换后仍是三件（Asset + Data + 自定义 Save）");
                Assert.AreSame(custom, found, "同类型只应剩自定义那一个");
            }
            finally
            {
                Bootstrap.Clear();
            }
        }
    }
}
