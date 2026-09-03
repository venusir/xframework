using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XNode;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 预置启动管线集成测试:装配执行(Collect + 相位分组 + Start)、相位收集与序、
    /// 进度快照、空树、失败与取消即停。
    /// </summary>
    class StartupPipelineTests
    {
        #region Private Methods

        /// <summary>构建含相位探针(Phase 0)与启动探针节点的根树。</summary>
        private static (RootNode root, PhaseProbeNode probe, StartProbeNode start) CreateTree()
        {
            var root = RootNode.Create();
            var probe = root.AddNode<PhaseProbeNode>();
            var start = root.AddNode<StartProbeNode>();
            return (root, probe, start);
        }

        #endregion

        #region 预置管线执行

        [Test]
        public void PresetPipeline_RunsStagesInOrder()
        {
            var (root, probe, start) = CreateTree();
            var log = new List<string>();
            probe.Log = log;
            start.Log = log;

            root.StartupAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, probe.ExecuteCount, "收集阶段应收敛到相位阶段节点并调度");
            Assert.IsTrue(start.Started, "启动阶段应执行节点 OnStart");
            Assert.AreEqual(new List<string> { "probe", "start" }, log, "相位阶段应先于启动阶段执行");
        }

        [Test]
        public void StartupAsync_ZeroArgs_Completes()
        {
            // 唯一重载(progress 默认 null):零参调用编译通过即锁定调用面
            var (root, probe, start) = CreateTree();

            root.StartupAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, probe.ExecuteCount);
            Assert.IsTrue(start.Started);
        }

        [Test]
        public void EmptyTree_CompletesWithWarning()
        {
            var root = RootNode.Create();
            var start = root.AddNode<StartProbeNode>();

            // 无相位阶段节点 → 装配期收集为空,打警告并直接完成(不阻塞启动阶段)
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Startup\] no phase stages found"));
            root.StartupAsync().GetAwaiter().GetResult();

            Assert.IsTrue(start.Started, "空相位列表不应阻塞启动阶段");
        }

        [Test]
        public void PhaseFailure_StopsStartStage()
        {
            var (root, probe, start) = CreateTree();
            probe.ThrowOnExecute = true;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Parallel stage failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            root.StartupAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, probe.ExecuteCount, "相位阶段应已执行");
            Assert.IsFalse(start.Started, "相位失败应中断后续阶段,启动阶段不得执行");
        }

        #endregion

        #region 相位收集与序

        [Test]
        public void CollectsNestedPhaseStages_AndRunsByPhaseOrder()
        {
            var root = RootNode.Create();
            var log = new List<string>();

            // 根直挂 Phase 0 + 嵌套于子容器内的 Phase 1:收集应跨层,执行应按相位升序
            var p0 = root.AddNode<PhaseProbeNode>();
            p0.Phase = 0;
            p0.StageName = "phase0";
            p0.Log = log;

            var container = root.AddNode<ContainerProbeNode>();
            var p1 = container.AddNode<PhaseProbeNode>();
            p1.Phase = 1;
            p1.StageName = "phase1";
            p1.Log = log;

            var start = root.AddNode<StartProbeNode>();
            start.Log = log;

            root.StartupAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, p0.ExecuteCount, "嵌套相位节点应被收集并调度");
            Assert.AreEqual(1, p1.ExecuteCount);
            CollectionAssert.AreEqual(new[] { "phase0", "phase1", "start" }, log,
                "相位按升序串行执行(同相位并行由组装配承担)");
        }

        [Test]
        public void SamePhase_ExecutesConcurrently()
        {
            var root = RootNode.Create();
            // 同一父节点按类型缓存节点:同相位并行断言需要两个独立类型的探针
            var a = root.AddNode<PhaseProbeNode>();
            a.Phase = 0;
            a.Gate = new UniTaskCompletionSource();
            var b = root.AddNode<SecondPhaseProbeNode>();
            b.Phase = 0;
            b.StageName = "probe2";
            b.Gate = new UniTaskCompletionSource();
            var start = root.AddNode<StartProbeNode>();

            var task = root.StartupAsync();

            Assert.AreEqual(1, a.ExecuteCount, "同相位节点应并行启动");
            Assert.AreEqual(1, b.ExecuteCount, "同相位节点不被兄弟挂起阻塞");
            Assert.IsFalse(start.Started, "相位未收敛前启动阶段不得执行");

            a.Gate.TrySetResult();
            b.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
            Assert.IsTrue(start.Started, "同相位全部放行后应收敛到启动");
        }

        #endregion

        #region 失败与取消即停

        [Test]
        public void CancellationDuringPhase_RaisesCancelled_AndSkipsStart()
        {
            var (root, probe, start) = CreateTree();
            probe.Gate = new UniTaskCompletionSource();

            var cts = new CancellationTokenSource();
            bool cancelled = false;
            bool completed = false;
            string failedReason = null;
            var pipeline = root.BuildStartupPipeline();
            pipeline.OnCancelled += () => cancelled = true;
            pipeline.OnCompleted += () => completed = true;
            pipeline.OnFailed += r => failedReason = r;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Pipeline\] Pipeline cancelled"));
            var task = pipeline.RunAsync(cts.Token);
            cts.Cancel();
            task.GetAwaiter().GetResult();
            pipeline.Destroy();

            Assert.IsTrue(cancelled, "相位挂起中取消应触发管线取消");
            Assert.IsFalse(completed, "取消不得触发完成");
            Assert.IsNull(failedReason, "取消不得触发失败");
            Assert.IsFalse(start.Started, "取消应阻止启动阶段");
            Assert.IsTrue(probe.LastToken.IsCancellationRequested, "执行中的相位节点应收割取消令牌");
        }

        #endregion

        #region 进度快照

        [Test]
        public void StartupAsync_ReportsPipelineSnapshots()
        {
            var (root, probe, _) = CreateTree();
            var progress = new List<PipelineProgress>();
            root.StartupAsync(new SyncProgress<PipelineProgress>(p => progress.Add(p))).GetAwaiter().GetResult();

            Assert.GreaterOrEqual(progress.Count, 2, "应至少广播收集与完成两次进度");
            Assert.AreEqual(0f, progress[0].OverallProgress, 0.001f, "首帧应为 0(收集阶段不占进度)");

            bool foundScanning = false;
            bool foundStarting = false;
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i].Description == "Scanning nodes...") foundScanning = true;
                if (progress[i].Description == "Starting nodes...") foundStarting = true;
            }
            Assert.IsTrue(foundScanning, "应广播收集阶段描述");
            Assert.IsTrue(foundStarting, "应广播启动阶段描述");

            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "完成终局应广播 1");
            Assert.AreEqual(3, progress[progress.Count - 1].TotalStageCount,
                "预置管线应为三阶段(Collect + 相位组 + Start)");
            Assert.AreEqual(1, probe.ExecuteCount);
        }

        #endregion

        /// <summary>
        /// 同步进度回调包装。EditMode 测试中 <see cref="System.Progress{T}"/> 在非默认
        /// SynchronizationContext 下会把 Report Post 到异步上下文,同步断言会竞态——本实现保证同步执行。
        /// </summary>
        private sealed class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;

            public SyncProgress(Action<T> handler)
            {
                _handler = handler;
            }

            public void Report(T value) => _handler(value);
        }

        #region 测试辅助节点

        /// <summary>实现 <see cref="IPhaseStage"/> 的探针节点:相位号/名称/挂起/抛异常可配,记录执行与取消令牌。
        /// (非 sealed:同一父节点按类型缓存节点,需派生子类型获得同相位并行第二实例。)</summary>
        private class PhaseProbeNode : EntityNode, IPhaseStage
        {
            public int Phase { get; set; }
            public string StageName = "probe";
            public int ExecuteCount;
            public bool ThrowOnExecute;
            public UniTaskCompletionSource Gate;
            public List<string> Log;
            public CancellationToken LastToken;

            string IPipelineStage.Name => StageName;

            float IPipelineStage.Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                ExecuteCount++;
                LastToken = cancellationToken;
                Log?.Add(StageName);
                context.SetDescription(StageName);

                if (Gate != null)
                    await Gate.Task.AttachExternalCancellation(cancellationToken);

                if (ThrowOnExecute)
                    throw new InvalidOperationException("boom");

                context.SetProgress(1f);
            }
        }

        /// <summary>无行为容器节点:承载嵌套相位探针,验证跨层收集。</summary>
        private sealed class ContainerProbeNode : EntityNode
        {
        }

        /// <summary>同类型第二探针变体:同一父节点按类型缓存,需独立类型获得同相位并行第二实例。</summary>
        private sealed class SecondPhaseProbeNode : PhaseProbeNode
        {
        }

        /// <summary>启动探针节点:记录 OnStart 执行。</summary>
        private sealed class StartProbeNode : EntityNode
        {
            public bool Started;
            public List<string> Log;

            protected override void OnStart()
            {
                Started = true;
                Log?.Add("start");
                base.OnStart();
            }
        }

        #endregion
    }
}
