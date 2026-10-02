using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XPipeline;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 框架启动引导的登记与运行入口。
    /// <para>需要异步初始化的模块实现 <see cref="IBootstrapStage"/> 并在此<b>显式登记</b>；
    /// <see cref="RunAsync"/> 按相位装配管线（同相位并行、相位升序串行，复用
    /// <see cref="Pipeline.BuildPhaseGroups"/>），<see cref="Shutdown"/> 按执行序的逆序（相位降序）反向清理。</para>
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

        /// <summary>按登记顺序保存的引导阶段。清理时按相位逆序遍历。</summary>
        private static readonly List<IBootstrapStage> StageList = new List<IBootstrapStage>();

        /// <summary>是否有一次 <see cref="RunAsync"/> 在途。运行期登记表只读（改动只告警、不阻止）。</summary>
        private static bool _isRunning;

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

            if (_isRunning)
            {
                LogManager.Warning(LogCategories.Bootstrap,
                    "RunAsync 运行中登记阶段 {0}：本轮不会执行它（装配期已快照），但它会参与后续的 Shutdown。",
                    stage.GetType().Name);
            }

            for (int i = 0; i < StageList.Count; i++)
            {
                if (ReferenceEquals(StageList[i], stage))
                {
                    LogManager.Warning(LogCategories.Bootstrap, "阶段 {0} 的同一实例已登记，忽略重复登记。", stage.GetType().Name);
                    return;
                }
            }

            StageList.Add(stage);
        }

        /// <summary>
        /// 注销一个已登记的引导阶段（按实例）。
        /// <para><b>替换内置阶段的推荐姿势</b>：先 <see cref="RegisterDefaults"/>，再用
        /// <see cref="Unregister{T}()"/> 摘掉不要的那个，最后 <see cref="Register(IBootstrapStage)"/> 自己的实例——
        /// 与调用顺序无关，因此在 <see cref="DefaultGameLauncher"/> 在场的场景同样可用
        /// （它在 Awake 里登记默认组合，与使用方自己 Awake 的先后是不确定的）。</para>
        /// </summary>
        /// <param name="stage">要注销的阶段实例。</param>
        /// <returns>移除了返回 true；该实例不在登记表中返回 false（不打日志）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stage"/> 为 null 时抛出。</exception>
        public static bool Unregister(IBootstrapStage stage)
        {
            if (stage == null)
                throw new ArgumentNullException(nameof(stage));

            for (int i = 0; i < StageList.Count; i++)
            {
                if (ReferenceEquals(StageList[i], stage))
                {
                    StageList.RemoveAt(i);

                    if (_isRunning)
                    {
                        LogManager.Warning(LogCategories.Bootstrap,
                            "RunAsync 运行中注销阶段 {0}：它本轮仍会执行（装配期已快照），但不会再收到 Shutdown。",
                            stage.GetType().Name);
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 按<b>精确类型</b>注销全部匹配的阶段，返回移除数量。
        /// <para>「精确类型」与 <see cref="RegisterDefaults"/> 的跳过口径一致：<b>派生类不会被连带移除</b>
        /// （`GetType() == typeof(T)`，因此传接口类型也不会命中任何阶段）。</para>
        /// </summary>
        /// <typeparam name="T">要移除的引导阶段类型。</typeparam>
        /// <returns>移除的数量；无匹配返回 0（不打日志）。</returns>
        public static int Unregister<T>() where T : IBootstrapStage
        {
            int removed = 0;
            for (int i = StageList.Count - 1; i >= 0; i--)
            {
                if (StageList[i].GetType() == typeof(T))
                {
                    StageList.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0 && _isRunning)
            {
                LogManager.Warning(LogCategories.Bootstrap,
                    "RunAsync 运行中注销了 {0} 个 {1} 阶段：它们本轮仍会执行（装配期已快照），但不会再收到 Shutdown。",
                    removed, typeof(T).Name);
            }

            return removed;
        }

        /// <summary>
        /// 登记框架内置的三个引导阶段：Asset(Phase 0) → Data(Phase 3) → Save(Phase 4)。
        /// <para>这是「开箱可用」的默认组合，不是强制——只想要其中一部分就自己逐个 <see cref="Register"/>。
        /// Localization 不在默认组合内：它需要语言数据，由使用方自行构造并登记。</para>
        /// <para><b>可重复调用</b>：它按类型跳过已存在的内置阶段，因此重复调用不会叠加
        /// （这是与 <see cref="Register"/> 唯一的语义差异——后者按实例去重，见其说明）。</para>
        /// <para><b>要替换其中某个内置阶段</b>：用 <see cref="Unregister{T}()"/> 摘掉它再
        /// <see cref="Register(IBootstrapStage)"/> 自己的实例——不要依赖与本方法的调用先后
        /// （与 <see cref="DefaultGameLauncher"/> 同场时先后由 Unity 决定，见 README）。</para>
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
        /// <para><b>重入只警告并忽略</b>（与 Pipeline 实例自身的重入守卫同形）：每次调用都会装配一条
        /// 新管线（管线守卫是实例级的），并发两次会让同一批阶段跑两遍。被忽略的调用<b>立即返回、
        /// 不代表启动完成</b>——要「等启动结束」，请共享第一次调用的任务，而不是再调一次。</para>
        /// </summary>
        /// <param name="progress">进度接收者，可为 null。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>管线跑完的异步任务。</returns>
        /// <exception cref="InvalidOperationException">任一阶段失败时抛出，消息含失败原因。</exception>
        /// <exception cref="OperationCanceledException">启动被取消时抛出。</exception>
        public static async UniTask RunAsync(IProgress<PipelineProgress> progress = null,
                                             CancellationToken cancellationToken = default)
        {
            if (_isRunning)
            {
                LogManager.Warning(LogCategories.Bootstrap, "RunAsync 已在运行中，忽略本次调用。");
                return;
            }

            // 在首个 await 之前同步置位(async 方法在首个未完成 await 之前是同步执行的)
            _isRunning = true;

            try
            {
                if (StageList.Count == 0)
                {
                    LogManager.Warning(LogCategories.Bootstrap, "未登记任何引导阶段，RunAsync 直接返回。");
                    return;
                }

                IPipeline pipeline = BuildPipeline();

                if (progress != null)
                {
                    pipeline.OnProgressUpdate += value => progress.Report(value);
                }

                // 终局从拉取面读：PipelineImpl.RunAsync 在失败/取消时都"正常返回"，
                // 单看返回值分不出成功与失败（这正是旧 StartupAsync 把失败吞成日志的原因）。
                // 旧实现为此订阅 OnFailed/OnCancelled 再用两个局部变量记账，现由管线自己落位。
                PipelineStatus status = PipelineStatus.Idle;
                string failureReason = null;

                try
                {
                    await pipeline.RunAsync(cancellationToken);

                    // 必须在 finally 的 Destroy 之前读：销毁会把状态回落 Idle
                    status = pipeline.Status;
                    failureReason = pipeline.FailureReason;
                }
                finally
                {
                    pipeline.Destroy();
                }

                if (failureReason != null)
                {
                    throw new InvalidOperationException($"[Bootstrap] 启动失败：{failureReason}");
                }

                if (status == PipelineStatus.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// 按<b>执行序的逆序</b>清理各阶段：相位降序，同相位内按登记逆序。
        /// <para>逆序的理由：后初始化的可能依赖先初始化的（例如 Save 依赖 Data），
        /// 先拆依赖再拆被依赖者。执行序由 <see cref="IPhaseStage.Phase"/> 决定（见 <see cref="RunAsync"/>），
        /// 因此清理序也按相位排——直接倒着遍历登记表只在「登记序恰好等于相位序」时才与执行序的逆序一致，
        /// 而登记顺序是使用方自由给的。</para>
        /// <para>同相位内各阶段并行执行、本无严格先后，取登记逆序作为确定性次序。</para>
        /// <para>单个阶段抛异常不会阻断其余阶段的清理——清理路径上一个模块的问题
        /// 不应导致别的模块泄漏。</para>
        /// </summary>
        public static void Shutdown()
        {
            // 收集 distinct 相位并升序排列(相位数量 = 模块数量级,非每帧路径;手写循环,零 LINQ)
            var phases = new List<int>(StageList.Count);
            for (int i = 0; i < StageList.Count; i++)
            {
                int phase = StageList[i].Phase;
                if (!phases.Contains(phase))
                {
                    phases.Add(phase);
                }
            }

            phases.Sort();

            // 相位降序;同相位内按登记逆序
            for (int p = phases.Count - 1; p >= 0; p--)
            {
                int phase = phases[p];
                for (int i = StageList.Count - 1; i >= 0; i--)
                {
                    if (StageList[i].Phase == phase)
                    {
                        ShutdownStage(StageList[i]);
                    }
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

        /// <summary>
        /// 清理单个阶段：异常隔离在阶段边界上（一个模块的清理问题不得阻断其余模块）。
        /// </summary>
        private static void ShutdownStage(IBootstrapStage stage)
        {
            try
            {
                stage.Shutdown();
            }
            catch (Exception exception)
            {
                LogManager.Error(LogCategories.Bootstrap, "阶段 {0} 的 Shutdown 抛异常，已跳过：{1}",
                    stage.GetType().Name, exception);
            }
        }

        #endregion
    }
}
