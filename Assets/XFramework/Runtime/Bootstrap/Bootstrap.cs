using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XPipeline;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 框架启动引导的登记与运行入口。
    /// <para>需要异步初始化的模块实现 <see cref="IBootstrapStage"/> 并在此<b>显式登记</b>；
    /// <see cref="RunAsync"/> 按相位装配管线（同相位并行、相位升序串行，复用
    /// <see cref="Pipeline.BuildPhaseGroups"/>），<see cref="Shutdown"/> 按登记顺序的逆序反向清理。</para>
    /// <para><b>为什么是显式登记而不是反射发现：</b>零反射、顺序可控、可测试，且使用方一眼能看出
    /// 到底有哪些东西会在这个启动流程里跑。框架不替使用方决定该初始化什么——
    /// <see cref="RegisterDefaults"/> 只是把最常用的三件（Asset / Data / Save）打包好，用不用随你。</para>
    /// <para><b>未登记任何阶段不是错误</b>：<see cref="RunAsync"/> 打一条警告后直接返回，
    /// 不抛异常（零配置使用静态服务的项目本就不需要引导流程）。这与门面模板里
    /// <c>EnsureInitialized</c> 抛 <see cref="InvalidOperationException"/> 的取向不同，是有意为之。</para>
    /// <para>登记表是静态的，测试须在 SetUp/TearDown 里调 <see cref="Clear"/> 复位。</para>
    /// </summary>
    public static class Bootstrap
    {
        #region Private Fields

        /// <summary>按登记顺序保存的引导阶段。清理时反向遍历。</summary>
        private static readonly List<IBootstrapStage> StageList = new List<IBootstrapStage>();

        #endregion

        #region Public Properties

        /// <summary>
        /// 当前已登记的引导阶段（按登记顺序）。
        /// <para><b>这是实时视图</b>，不是快照——后续登记会反映出来。只读用途，勿缓存后假定其不变。</para>
        /// </summary>
        public static IReadOnlyList<IBootstrapStage> Stages => StageList;

        #endregion

        #region Registration

        /// <summary>
        /// 登记一个引导阶段。
        /// <para><b>按实例去重，不按类型</b>：同一个实例重复登记会被忽略，但<b>同类型的多个实例可以共存</b>——
        /// 登记表是「初始化步骤列表」，不是「每类型一个的容器」。参数化的阶段（如带不同配置的
        /// <c>SaveBootstrapStage</c>）用同一类型登记多次是合法的。</para>
        /// </summary>
        /// <param name="stage">要登记的阶段。</param>
        /// <exception cref="ArgumentNullException"><paramref name="stage"/> 为 null 时抛出。</exception>
        public static void Register(IBootstrapStage stage)
        {
            if (stage == null)
                throw new ArgumentNullException(nameof(stage));

            for (int i = 0; i < StageList.Count; i++)
            {
                if (ReferenceEquals(StageList[i], stage))
                {
                    Debug.LogWarning($"[Bootstrap] 阶段 {stage.GetType().Name} 的同一实例已登记，忽略重复登记。");
                    return;
                }
            }

            StageList.Add(stage);
        }

        /// <summary>
        /// 登记框架内置的三个引导阶段：Asset(Phase 0) → Data(Phase 3) → Save(Phase 4)。
        /// <para>这是「开箱可用」的默认组合，不是强制——只想要其中一部分就自己逐个 <see cref="Register"/>。
        /// Localization 不在默认组合内：它需要语言数据，由使用方自行构造并登记。</para>
        /// <para><b>可重复调用</b>：它按类型跳过已存在的内置阶段，因此重复调用不会叠加
        /// （这是与 <see cref="Register"/> 唯一的语义差异——后者按实例去重，见其说明）。</para>
        /// </summary>
        public static void RegisterDefaults()
        {
            RegisterDefaultsEntry(new XAsset.AssetBootstrapStage());
            RegisterDefaultsEntry(new XData.DataBootstrapStage());
            RegisterDefaultsEntry(new XSave.SaveBootstrapStage());
        }

        /// <summary>
        /// <see cref="RegisterDefaults"/> 专用：同类型已存在则跳过，不打警告（重复调用是正常用法）。
        /// </summary>
        private static void RegisterDefaultsEntry(IBootstrapStage stage)
        {
            Type type = stage.GetType();
            for (int i = 0; i < StageList.Count; i++)
            {
                if (StageList[i].GetType() == type)
                {
                    return;
                }
            }

            StageList.Add(stage);
        }

        /// <summary>
        /// 清空登记表。测试复位用。
        /// <para>不影响已初始化的服务——它只管登记表。</para>
        /// </summary>
        public static void Clear()
        {
            StageList.Clear();
        }

        #endregion

        #region Run / Shutdown

        /// <summary>
        /// 按相位装配并运行引导管线。
        /// <para>失败与取消<b>会抛出</b>，不像旧节点树启动路径那样只留一条日志：
        /// 启动失败是致命的，调用方必须能感知。</para>
        /// </summary>
        /// <param name="progress">进度接收者，可为 null。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>管线跑完的异步任务。</returns>
        /// <exception cref="InvalidOperationException">任一阶段失败时抛出，消息含失败原因。</exception>
        /// <exception cref="OperationCanceledException">启动被取消时抛出。</exception>
        public static async UniTask RunAsync(IProgress<PipelineProgress> progress = null,
                                             CancellationToken cancellationToken = default)
        {
            if (StageList.Count == 0)
            {
                Debug.LogWarning("[Bootstrap] 未登记任何引导阶段，RunAsync 直接返回。");
                return;
            }

            IPipeline pipeline = BuildPipeline();

            // 终局状态必须自己记：PipelineImpl.RunAsync 在失败/取消时都"正常返回"，
            // 单看返回值分不出成功与失败（这正是旧 StartupAsync 把失败吞成日志的原因）
            string failureReason = null;
            bool cancelled = false;

            pipeline.OnFailed += reason => failureReason = reason;
            pipeline.OnCancelled += () => cancelled = true;
            if (progress != null)
            {
                pipeline.OnProgressUpdate += value => progress.Report(value);
            }

            try
            {
                await pipeline.RunAsync(cancellationToken);
            }
            finally
            {
                pipeline.Destroy();
            }

            if (failureReason != null)
            {
                throw new InvalidOperationException($"[Bootstrap] 启动失败：{failureReason}");
            }

            if (cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        /// <summary>
        /// 按登记顺序的<b>逆序</b>清理各阶段。
        /// <para>逆序的理由：后初始化的可能依赖先初始化的（例如 Save 依赖 Data），
        /// 先拆依赖再拆被依赖者。</para>
        /// <para>单个阶段抛异常不会阻断其余阶段的清理——清理路径上一个模块的问题
        /// 不应导致别的模块泄漏。</para>
        /// </summary>
        public static void Shutdown()
        {
            for (int i = StageList.Count - 1; i >= 0; i--)
            {
                IBootstrapStage stage = StageList[i];
                try
                {
                    stage.Shutdown();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Bootstrap] 阶段 {stage.GetType().Name} 的 Shutdown 抛异常，已跳过：{exception}");
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 用当前登记表装配管线：每相位一个 ParallelStage（相位升序）。
        /// </summary>
        private static IPipeline BuildPipeline()
        {
            // 装配期快照：Pipeline 的相位分组需要在构造时拿到完整列表
            var phaseStages = new List<IPhaseStage>(StageList.Count);
            for (int i = 0; i < StageList.Count; i++)
            {
                phaseStages.Add(StageList[i]);
            }

            IPipeline pipeline = Pipeline.Create();

            IReadOnlyList<IPipelineStage> groups = Pipeline.BuildPhaseGroups(phaseStages);
            for (int i = 0; i < groups.Count; i++)
            {
                pipeline.AddStage(groups[i]);
            }

            return pipeline;
        }

        #endregion
    }
}
