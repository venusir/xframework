using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XData
{

    /// <summary>
    /// <see cref="DataManager"/> 的引导阶段。Phase = <see cref="BootstrapPhases.Data"/>（3），晚于 Asset、早于 Save。
    /// <para>同步瞬时阶段：无取消窗口（没有可取消的 await），故忽略 <c>cancellationToken</c>。</para>
    /// </summary>
    public sealed class DataBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        /// <summary>
        /// 本阶段是否<b>确实</b>完成了初始化。<see cref="Shutdown"/> 据此决定要不要注销——
        /// 「本阶段初始化的服务才由本阶段清理」，已初始化早退路径不该把使用方注入的实现一起注销。
        /// </summary>
        private bool _initializedByThisStage;

        #endregion

        #region IBootstrapStage

        /// <summary>Phase = <see cref="BootstrapPhases.Data"/>（3）。晚于 Asset、早于 Save，确保依赖的模块已就绪。</summary>
        public int Phase => BootstrapPhases.Data;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>注入默认的 <see cref="DataManagerImpl"/> 完成初始化。</summary>
        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing Data Manager...");

            bool wasInitialized = DataManager.IsInitialized;
            DataManager.Initialize(new DataManagerImpl());

            // 只在**本次**完成初始化时置位；重跑时 wasInitialized 为真，绝不能把上一轮置的 true
            // 覆写成 false——否则 Shutdown 不再注销第一次由本阶段初始化的门面（DATA-002）。
            if (!wasInitialized)
                _initializedByThisStage = true;

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 反向清理：<b>只注销本阶段自己初始化的那份</b>——已初始化早退路径不碰使用方注入的实现。幂等。
        /// </summary>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
                return;

            _initializedByThisStage = false;
            DataManager.Shutdown();
        }

        #endregion
    }
}
