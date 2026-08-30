using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLoader;
using XFramework.XNode;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 预置启动管线集成测试:三阶段装配执行(Collect + Load + Start)、进度映射、空树、失败即停。
    /// </summary>
    class StartupPipelineTests
    {
        #region Private Methods

        /// <summary>构建含可加载节点与启动探针节点的根树。</summary>
        private static (RootNode root, LoadableProbeNode loadable, StartProbeNode start) CreateTree()
        {
            var root = RootNode.Create();
            var loadable = root.AddNode<LoadableProbeNode>();
            var start = root.AddNode<StartProbeNode>();
            return (root, loadable, start);
        }

        #endregion

        #region 预置管线执行

        [Test]
        public void PresetPipeline_RunsStagesInOrder()
        {
            var (root, loadable, start) = CreateTree();
            var log = new List<string>();
            loadable.Log = log;
            start.Log = log;

            root.StartupAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, loadable.LoadCount, "装载阶段应收集到 ILoadable 节点并调度");
            Assert.IsTrue(start.Started, "启动阶段应执行节点 OnStart");
            Assert.AreEqual(new List<string> { "load", "start" }, log, "加载阶段应先于启动阶段执行");
        }

        [Test]
        public void StartupAsync_ZeroArgs_CompilesToLegacyOverload()
        {
            // 无参调用在双重载下无歧义(唯一适用旧重载),编译通过即锁定该兼容面
            var (root, loadable, start) = CreateTree();

            root.StartupAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, loadable.LoadCount);
            Assert.IsTrue(start.Started);
        }

        [Test]
        public void EmptyTree_CompletesWithWarning()
        {
            var root = RootNode.Create();
            var start = root.AddNode<StartProbeNode>();

            // 无 ILoadable 节点 → 装配期收集为空,打警告并直接完成(不阻塞启动阶段)
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Loader\] no loadable tasks found"));
            root.StartupAsync().GetAwaiter().GetResult();

            Assert.IsTrue(start.Started, "空加载列表不应阻塞启动阶段");
        }

        [Test]
        public void LoadFailure_StopsStartStage()
        {
            var (root, loadable, start) = CreateTree();
            loadable.ThrowOnLoad = true;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Loader\] Load failed:"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pipeline\] Pipeline failed:"));
            root.StartupAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, loadable.LoadCount, "加载阶段应已执行");
            Assert.IsFalse(start.Started, "加载失败应中断后续阶段,启动阶段不得执行");
        }

        #endregion

        #region 进度映射

        [Test]
        public void StartupAsync_ProgressMapsToLoadProgress()
        {
            var (root, loadable, _) = CreateTree();
            var progress = new List<LoadProgress>();
            // 用同步 IProgress 而非 System.Progress<T>(后者在非默认 SynchronizationContext 下 Post 异步执行,断言会竞态)
            root.StartupAsync(new SyncProgress<LoadProgress>(p => progress.Add(p))).GetAwaiter().GetResult();

            Assert.GreaterOrEqual(progress.Count, 2, "应至少广播装载与完成两次进度");
            Assert.AreEqual(0f, progress[0].OverallProgress, 0.001f, "首帧应为装载阶段 0 进度");

            // 装载描述广播在 Executing 瞬间之后发出(描述写入触发),遍历确认
            bool foundScanning = false;
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i].Description == "Scanning nodes...")
                {
                    foundScanning = true;
                    break;
                }
            }
            Assert.IsTrue(foundScanning, "应广播装载阶段描述");

            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "完成终局应广播 1");
            Assert.AreEqual(1, loadable.LoadCount);
        }

        [Test]
        public void StartupAsync_PipelineProgressOverload_ReportsPipelineSnapshot()
        {
            var (root, _, _) = CreateTree();
            var progress = new List<PipelineProgress>();
            root.StartupAsync(new SyncProgress<PipelineProgress>(p => progress.Add(p))).GetAwaiter().GetResult();

            Assert.GreaterOrEqual(progress.Count, 2, "管线进度重载应持续广播");
            Assert.AreEqual(1f, progress[progress.Count - 1].OverallProgress, 0.001f, "完成终局应广播 1");
            Assert.AreEqual(3, progress[progress.Count - 1].TotalStageCount, "预置管线应为三阶段(Collect + Load + Start)");
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

        /// <summary>实现 <see cref="ILoadable"/> 的探针节点:记录加载执行、可选抛异常。</summary>
        private sealed class LoadableProbeNode : EntityNode, ILoadable
        {
            public int LoadCount;
            public bool ThrowOnLoad;
            public List<string> Log;

            public int Phase => 0;

            public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
            {
                LoadCount++;
                Log?.Add("load");
                progress.SetDescription("loadable");

                if (ThrowOnLoad)
                    throw new InvalidOperationException("boom");

                await UniTask.CompletedTask;
            }
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
