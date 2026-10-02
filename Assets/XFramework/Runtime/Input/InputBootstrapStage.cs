using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XBootstrap;
using XFramework.XInput.Default;
using XFramework.XPipeline;

namespace XFramework.XInput
{

    /// <summary>
    /// <see cref="InputManager"/> 的引导阶段：把输入模块的初始化交给框架启动流程。
    /// <para>Phase = <see cref="BootstrapPhases.Input"/>（1），晚于 Asset（输入资产可能经资源系统加载）。</para>
    /// <para><b>已初始化则早退、且不接管</b>：使用方可能已自行 <c>Initialize</c>，那就直接完成、
    /// 不重复调用（那只会打一条重复初始化告警）、也不在 <see cref="Shutdown"/> 里销毁它。</para>
    /// </summary>
    public sealed class InputBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        private readonly InputSystemOptions _options;

        /// <summary>本阶段是否<b>确实</b>完成了初始化。清理据此决定要不要销毁（见类型注释）。</summary>
        private bool _initializedByThisStage;

        #endregion

        #region Construction

        /// <summary>
        /// 构造引导阶段。
        /// <para><paramref name="options"/>.<c>Asset</c> 为 null 由
        /// <c>InputManager.Initialize</c> 自身的参数校验拦截（那条消息只有一处）——失败会沿引导管线
        /// 的标准失败路径报出来，而不是在构造期。</para>
        /// </summary>
        /// <param name="options">默认提供者的初始化选项（自带资产、初始 ActionMap）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null 时抛出。</exception>
        public InputBootstrapStage(InputSystemOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _options = options;
        }

        #endregion

        #region IBootstrapStage

        /// <summary>Phase = <see cref="BootstrapPhases.Input"/>（1），晚于 Asset。</summary>
        public int Phase => BootstrapPhases.Input;

        /// <inheritdoc/>
        public string Name => GetType().Name;

        /// <inheritdoc/>
        public float Weight => 1f;

        /// <summary>
        /// 初始化输入管理器。同步瞬时阶段（没有可取消的 await），忽略 <c>cancellationToken</c>。
        /// </summary>
        public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            context.SetDescription("Initializing Input Manager...");

            if (InputManager.IsInitialized)
            {
                // 使用方已自行初始化：不重复调用，也不接管——Shutdown 时不销毁别人的管理器
                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
                return UniTask.CompletedTask;
            }

            InputManager.Initialize(_options);
            _initializedByThisStage = true;

            context.SetProgress(1f);
            context.SetState(PipelineStageState.Completed);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 反向清理：<b>只销毁本阶段自己初始化的那份</b>（空转与早退路径都不碰别人的管理器）。幂等。
        /// </summary>
        public void Shutdown()
        {
            if (!_initializedByThisStage)
                return;

            _initializedByThisStage = false;
            InputManager.Destroy();
        }

        #endregion
    }
}
