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
    /// <para><b>两条互斥的构造路径</b>：<see cref="InputBootstrapStage(InputSystemOptions)"/> 走**默认后端**
    /// （Unity Input System），<see cref="InputBootstrapStage(IInputProvider)"/> 走**任意自定义后端**
    /// （Rewired 等）——后者是插件中立的（<see cref="IInputProvider"/> 不含任何 Unity 输入类型），
    /// 「换后端」因此也能只写一行且清理对称。</para>
    /// <para><b>已初始化则早退、且不接管</b>：使用方可能已自行 <c>Initialize</c>，那就直接完成、
    /// 不重复调用（那只会打一条重复初始化告警）、也不在 <see cref="Shutdown"/> 里销毁它。</para>
    /// </summary>
    public sealed class InputBootstrapStage : IBootstrapStage
    {
        #region Private Fields

        /// <summary>默认后端的配置；与 <see cref="_provider"/> 互斥（一个非空、另一个必为 null）。</summary>
        private readonly InputSystemOptions _options;

        /// <summary>自定义后端实例；与 <see cref="_options"/> 互斥。</summary>
        private readonly IInputProvider _provider;

        /// <summary>本阶段是否<b>确实</b>完成了初始化。清理据此决定要不要销毁（见类型注释）。</summary>
        private bool _initializedByThisStage;

        #endregion

        #region Construction

        /// <summary>
        /// 用<b>默认后端</b>（Unity Input System）构造引导阶段。
        /// <para><paramref name="options"/>.<c>Asset</c> 为 null 由
        /// <c>InputManager.Initialize</c> 自身的参数校验拦截（那条消息只有一处）——失败会沿引导管线
        /// 的标准失败路径报出来，而不是在构造期。</para>
        /// </summary>
        /// <param name="options">默认后端的初始化选项（自带资产、初始 ActionMap）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null 时抛出。</exception>
        public InputBootstrapStage(InputSystemOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _options = options;
        }

        /// <summary>
        /// 用<b>任意自定义后端</b>构造引导阶段（Rewired 等）——一句话就是「换后端」那条路的阶段侧入口：
        /// <c>Bootstrap.Register(new InputBootstrapStage(myProvider))</c>，清理随 <see cref="Shutdown"/>
        /// 对称地走（<c>InputManager.Destroy()</c> 对自定义 provider 同样会 <c>Dispose</c>）。
        /// <para><b>后端中立</b>：<see cref="IInputProvider"/> 在 <c>XFramework.XInput</c> 里，
        /// 不含任何 Unity 输入类型，因此本重载不触「Unity 输入类型只出现在 <c>XInput.Default</c>」那条模块边界。</para>
        /// <para><b>与默认后端互斥</b>：两条路径分别走 <c>InputManager.Initialize(IInputProvider)</c> 与
        /// <c>InputManager.Initialize(InputSystemOptions)</c>。注意**裸 <c>null</c> 实参会与另一个重载二义**，
        /// 需要显式转换（正常调用不会遇到——两个参数类型都是具体类型）。</para>
        /// </summary>
        /// <param name="provider">自定义输入提供者实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> 为 null 时抛出。</exception>
        public InputBootstrapStage(IInputProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            _provider = provider;
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

            // 两条路径各自对应 InputManager 的一个 Initialize 重载（都注册帧驱动）
            if (_provider != null)
                InputManager.Initialize(_provider);
            else
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
