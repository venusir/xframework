using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XPipeline;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 委托形式的引导阶段工厂：把「只调一次初始化」的样板类收成一次 <see cref="Create"/>。
    /// <para><b>所有权归调用方</b>：工厂不替你判断初始化与清理的边界——<paramref name="shutdown"/>
    /// 由你提供（可以是 null，表示本阶段没有需要反向清理的东西）。委托里初始化了什么，
    /// 就由传入的 <paramref name="shutdown"/> 负责拆掉；阶段只负责调用它，
    /// 并且可能在 <c>ExecuteAsync</c> 从未执行过时被调用（见 <see cref="IBootstrapStage.Shutdown"/>）。</para>
    /// <para>需要在执行中写描述/进度、或需要可重入的所有权判定的阶段，仍应实现 <see cref="IBootstrapStage"/>。</para>
    /// </summary>
    public static class BootstrapStage
    {
        /// <summary>
        /// 创建一个委托阶段。
        /// </summary>
        /// <param name="phase">相位号；语义见 <see cref="BootstrapPhases"/>（同相位并行、相位升序串行）。</param>
        /// <param name="name">阶段名（进入日志与失败原因）；不能为 null。</param>
        /// <param name="execute">初始化委托；不能为 null。抛出的异常由 Pipeline 的 StageExecution 置为失败。</param>
        /// <param name="shutdown">反向清理委托，可为 null；应幂等，且能在从未初始化时安全空转。</param>
        /// <param name="weight">进度权重，默认 1；必须为有限非负数（0 表示不占进度）。</param>
        /// <returns>可直接交给 <see cref="Bootstrap.Register(IBootstrapStage)"/> 的阶段实例。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> 或 <paramref name="execute"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> 为 NaN、无穷或负数时抛出。</exception>
        public static IBootstrapStage Create(int phase,
                                             string name,
                                             Func<CancellationToken, UniTask> execute,
                                             Action shutdown = null,
                                             float weight = 1f)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "阶段权重必须为有限非负数。");

            return new DelegateBootstrapStage(phase, name, execute, shutdown, weight);
        }

        /// <summary>委托阶段的实现；不公开——使用方只经 <see cref="Create"/> 拿到 <see cref="IBootstrapStage"/>。</summary>
        private sealed class DelegateBootstrapStage : IBootstrapStage
        {
            private readonly int _phase;
            private readonly string _name;
            private readonly Func<CancellationToken, UniTask> _execute;
            private readonly Action _shutdown;
            private readonly float _weight;

            internal DelegateBootstrapStage(int phase,
                                            string name,
                                            Func<CancellationToken, UniTask> execute,
                                            Action shutdown,
                                            float weight)
            {
                _phase = phase;
                _name = name;
                _execute = execute;
                _shutdown = shutdown;
                _weight = weight;
            }

            public int Phase => _phase;

            public string Name => _name;

            public float Weight => _weight;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                context.SetDescription(_name);
                await _execute(cancellationToken);
                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
            }

            public void Shutdown()
            {
                _shutdown?.Invoke();
            }
        }
    }
}
