using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAsset;
using XFramework.XAsset.Tests;
using XFramework.XPipeline;

namespace XFramework.XBootstrap.Tests
{
    /// <summary>
    /// <see cref="GameLauncher"/> 的**真生命周期** PlayMode 覆盖：真 <c>AddComponent</c> 走完
    /// <c>Awake → Start → OnDestroy</c>，让 <see cref="Bootstrap.RunAsync"/> 真的跨帧跑起来。
    /// <para><b>为什么只能在 PlayMode</b>：三条用例的主语全是 Unity 生命周期——<c>Awake/Start</c> 的真触发、
    /// 跨帧让出、<c>OnDestroy</c> 与在途启动的交叠；EditMode 里 <c>Awake</c> 不跑、帧不存在，这些形态造不出来。
    /// 既有 <c>GameLauncherTests</c> 刻意在 <c>Start</c> 真跑 <c>RunAsync</c> 之前销毁对象（怕真去初始化 YooAsset），
    /// 本 fixture 补的正是那半边：覆写 <c>ConfigureStages</c> **只登记假阶段**，把 YooAsset 完全绕开。</para>
    /// <para><b>用例 2/3 覆盖两条不同的边界（2026-10-13 裁定「彻底修」后重写）</b>：
    /// <c>Shutdown</c> 会先中止在途 <c>RunAsync</c>（Bootstrap 内部令牌链），因此销毁发生在启动途中时，
    /// <b>后置相位不再被执行</b>——用例 2 断言即此语义（已从「钉现状」翻转）。但中止是<b>协作式</b>的：
    /// 不响应取消的在途阶段仍会跑完自己的沉降点并走失败链——用例 3 以真实 <c>AssetBootstrapStage</c>
    /// 加忽略令牌的假实现为证，<b>原样保留</b>；它是修复覆盖不到的边界，不是回归。</para>
    /// </summary>
    [TestFixture]
    public class GameLauncherFrameTests
    {
        #region Test Doubles

        /// <summary>
        /// 只登记静态字段里的阶段。<b>刻意不调 <c>Bootstrap.RegisterDefaults</c></b>：那会装上
        /// Asset / Data / Save 三件——Asset 阶段真走 YooAsset、Save 阶段扫真实 <c>persistentDataPath</c>。
        /// 静态字段是刻意的：<c>Awake</c> 在 <c>AddComponent</c> 时同步跑，实例字段来不及设。
        /// </summary>
        private sealed class FrameLauncher : GameLauncher
        {
            public static IBootstrapStage[] Stages;

            protected override void ConfigureStages()
            {
                if (Stages == null) return;
                for (int i = 0; i < Stages.Length; i++) Bootstrap.Register(Stages[i]);
            }
        }

        /// <summary>
        /// 可观测的假阶段：逐帧完成（<see cref="Frames"/>）或挂死（<see cref="Gate"/>）。
        /// <para><see cref="Gate"/> 路径**故意不挂令牌**——代表「不响应取消的在途工作」，
        /// 交叠用例要测的正是这种工作与 <c>Shutdown</c> 的相遇。</para>
        /// </summary>
        private sealed class GateStage : IBootstrapStage
        {
            public string StageName = "stage";
            public int PhaseValue;
            public int Frames;
            public UniTaskCompletionSource Gate;

            public int ExecuteCount;
            public bool Started;
            public int CompletedCount;
            public int ShutdownCount;

            /// <summary><see cref="ExecuteAsync"/> 开始那一刻已发生的 Shutdown 次数——交叠的直接证据。</summary>
            public int ShutdownCountAtStart = -1;

            public int Phase => PhaseValue;
            public string Name => StageName;
            public float Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                ExecuteCount++;
                Started = true;
                ShutdownCountAtStart = ShutdownCount;
                context.SetDescription(StageName);

                if (Gate != null)
                {
                    await Gate.Task; // 不响应取消
                }
                else
                {
                    for (int i = 0; i < Frames; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await UniTask.Yield();
                    }
                }

                CompletedCount++;
            }

            public void Shutdown() => ShutdownCount++;
        }

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
            // Clear 不清 _isRunning——失败用例（或上面的交叠用例）会把在途标记留下，
            // 静默吞掉后续 fixture 的 RunAsync；会话复位是兜底（internal，经 InternalsVisibleTo 可达）。
            Bootstrap.ResetForNewSession();
            FrameLauncher.Stages = null;
            AssetManager.Destroy();
            AssetManager.ImplFactory = null;
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
            FrameLauncher.Stages = null;
            AssetManager.Destroy();
            AssetManager.ImplFactory = null;
        }

        /// <summary>有界轮询：最多让出 <paramref name="maxFrames"/> 帧等条件成立（帧数只作上限，不作断言）。</summary>
        private static async UniTask<bool> WaitUntil(Func<bool> condition, int maxFrames = 60)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition()) return true;
                await UniTask.Yield();
            }

            return condition();
        }

        #endregion

        #region 用例

        [Test]
        public async Task Start_RunsRegisteredStagesAcrossFrames()
        {
            var stage = new GateStage { StageName = "跨帧阶段", Frames = 2 };
            FrameLauncher.Stages = new IBootstrapStage[] { stage };

            var go = new GameObject("launcher-frames");
            try
            {
                go.AddComponent<FrameLauncher>();

                // Awake 同步跑（登记已发生）；Start 要下一帧才执行，此刻还没跑
                Assert.AreEqual(1, Bootstrap.Stages.Count, "Awake 应已登记唯一那个假阶段");
                Assert.IsFalse(stage.Started, "AddComponent 当场 Start 尚未执行");

                Assert.IsTrue(await WaitUntil(() => stage.CompletedCount >= 1),
                    "Start 里的 RunAsync 应跨帧跑完登记的阶段");
                Assert.AreEqual(1, stage.ExecuteCount);

                // 首轮已正常收敛（_isRunning 已释放）：再调一次必须真的重跑，而不是被重入守卫吞掉
                await UniTask.Yield();
                await UniTask.Yield();

                bool reran = false;
                for (int i = 0; i < 10 && !reran; i++)
                {
                    await Bootstrap.RunAsync();
                    reran = stage.ExecuteCount >= 2;
                    if (!reran) await UniTask.Yield();
                }

                Assert.IsTrue(reran, "首轮结束后 _isRunning 应已释放——再次 RunAsync 必须真的重跑阶段");
                Assert.IsTrue(await WaitUntil(() => stage.CompletedCount >= 2), "第二轮也应完整跑完");

                UnityEngine.Object.Destroy(go);
                await UniTask.Yield();
                await UniTask.Yield();

                Assert.AreEqual(1, stage.ShutdownCount, "OnDestroy 应按逆序清理登记表里的阶段");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 销毁 × 在途启动交叠：<c>Shutdown</c> 先中止在途运行，<b>后置相位不再执行</b>。
        /// <para>两个阶段是刻意的：阶段 0 挂死造出「在途」（<b>刻意不挂令牌</b>——不响应取消的在途工作，
        /// 放行前谁也叫不停它）；阶段 5 从未执行。用来钉住两件事：<c>Shutdown</c> 会清理<b>整张登记表</b>
        /// （含未执行的后置相位），而放行后的在途管线<b>不会</b>再执行它——取消在阶段边界生效。</para>
        /// </summary>
        [Test]
        public async Task Destroy_DuringInFlightStart_CancelsRunAndLaterPhaseNeverExecutes()
        {
            var gate = new UniTaskCompletionSource();
            var inFlight = new GateStage { StageName = "在途", PhaseValue = 0, Gate = gate };
            var later = new GateStage { StageName = "后置相位", PhaseValue = 5 };
            FrameLauncher.Stages = new IBootstrapStage[] { inFlight, later };

            var go = new GameObject("launcher-overlap");
            try
            {
                go.AddComponent<FrameLauncher>();

                Assert.IsTrue(await WaitUntil(() => inFlight.Started),
                    "前提校验：阶段 0 应进入在途挂起");

                UnityEngine.Object.Destroy(go);
                await UniTask.Yield(); // Destroy 在帧末落地，OnDestroy 随之执行
                await UniTask.Yield();

                Assert.AreEqual(1, inFlight.ShutdownCount, "在途阶段应被 OnDestroy 的 Shutdown 清理");
                Assert.AreEqual(1, later.ShutdownCount, "从未执行过的后置相位同样会被清理——Shutdown 扫的是整张登记表");

                // 放行在途阶段：Shutdown 已取消在途运行，管线应在阶段边界停住，不再进入相位 5
                gate.TrySetResult();

                Assert.IsTrue(await WaitUntil(() => inFlight.CompletedCount >= 1),
                    "放行后阶段 0 应跑完自己的沉降点（中止是协作式的，它不响应取消）");
                await UniTask.Yield();
                await UniTask.Yield();

                Assert.AreEqual(0, later.ExecuteCount,
                    "被 Shutdown 中止的在途启动不得再执行后置相位（取消在阶段边界生效）");
                Assert.AreEqual(0, later.CompletedCount);
                Assert.AreEqual(-1, later.ShutdownCountAtStart, "后置相位从未执行过");

                // 等管线收尾，不给后续 fixture 留在途任务
                await UniTask.Yield();
                await UniTask.Yield();
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 真实 <see cref="AssetBootstrapStage"/> 的端到端复现：销毁点掉 <c>AssetManager.Destroy()</c> 的
        /// 初始化代际，在途初始化完成后代际校验抛异常 → 管线失败 → <c>Start</c> 记「启动失败」。
        /// 即 <c>Modules/Bootstrap.md:26</c> 预言的「最坏一条『启动失败』错误日志」的完整链路。
        /// <para><b>这条是「彻底修」覆盖不到的边界（2026-10-13 裁定后原样保留）</b>：阶段确实把令牌
        /// 透传给了 <c>InitializeAsync</c>，但实现不响应取消（本用例的 <c>FakeAssetManager</c> 忽略令牌），
        /// 于是中止是协作式的——在途初始化照常完成、代际校验照常抛。它不是回归，是如实边界。</para>
        /// </summary>
        [Test]
        public async Task Destroy_DuringRealAssetStageInit_ReproducesStartupFailureLog()
        {
            var initGate = new UniTaskCompletionSource();
            var fake = new FakeAssetManager { InitTask = initGate.Task };
            AssetManager.ImplFactory = () => fake;

            FrameLauncher.Stages = new IBootstrapStage[] { new AssetBootstrapStage() };

            var go = new GameObject("launcher-asset");
            try
            {
                go.AddComponent<FrameLauncher>();

                Assert.IsTrue(await WaitUntil(() => fake.InitCallCount >= 1),
                    "前提校验：真实 Asset 阶段应已调用 InitializeAsync 并挂起在途");

                UnityEngine.Object.Destroy(go);
                await UniTask.Yield();
                await UniTask.Yield();

                Assert.IsFalse(AssetManager.IsInitialized, "销毁后门面应回到未初始化态（代际已递增）");

                // 覆盖边界（原样保留）：假实现忽略令牌 → 在途初始化照常完成 → 代际校验抛异常 →
                // 管线失败 → Start 记错误。中止是协作式的，不响应取消的实现在射程之外。
                // 只锚消息头，不匹配多行异常详情（异常以 ToString() 追加，锚全长必碎）。
                LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed"));
                LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed"));
                LogAssert.Expect(LogType.Error, new Regex(@"\[GameLauncher\] 启动失败"));

                initGate.TrySetResult();

                Assert.IsTrue(await WaitUntil(() => fake.Disposed),
                    "作废路径应释放半初始化的实现（代际校验处 Dispose 后抛出）");

                // 让失败链与 Start 的日志落完
                await UniTask.Yield();
                await UniTask.Yield();

                Assert.IsFalse(AssetManager.IsInitialized, "失败链之后门面不得处于「已初始化」态");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        #endregion
    }
}
