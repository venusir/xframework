using UnityEngine;
using XFramework.XLog;

namespace XFramework.XPipeline
{
    /// <summary>
    /// 阶段上下文写入的接收方(内部)。阶段经 <see cref="PipelineStageContext"/> 写入(SetProgress/SetDescription/SetState)
    /// 即同步回调,接收方负责聚合与广播。管线实现与并行阶段各自实现本接口,形成事件驱动的进度链。
    /// </summary>
    internal interface IStageContextSink
    {
        /// <summary>阶段上下文发生写入。context 为被写入的上下文实例。</summary>
        void OnStageContextChanged(PipelineStageContext context);
    }

    /// <summary>
    /// 管线阶段执行上下文。由 <see cref="IPipeline"/> 装配时创建并注入阶段。
    /// <para>只承载<b>阶段写面</b>(<see cref="Progress"/>/<see cref="Description"/>/<see cref="State"/> + SetXxx):
    /// 供阶段在 <see cref="IPipelineStage.ExecuteAsync"/> 内写入,写入即同步触发接收方(管线或容器)的聚合。
    /// 读取面不在本类——运行中经 <see cref="IPipeline.OnProgressUpdate"/> 推送 <see cref="PipelineProgress"/>,
    /// 终局经 <see cref="IPipeline.Status"/>/<see cref="IPipeline.FailureReason"/> 拉取。</para>
    /// <para>线程契约:写入须与 <see cref="IPipeline.RunAsync"/> 调度同一上下文(Unity 主线程)——写入同步触发
    /// 聚合与订阅者回调,整条链非线程安全;Editor 下越线程写入打 <c>LogManager.Error</c>提示
    /// (开发期断言,Release 构建零开销)。基础设施内部直写字段(容器子上下文预置、聚合器转发
    /// <see cref="CurrentTaskName"/>)不经本写面,不在断言范围——本就只发生在主线程。</para>
    /// </summary>
    public sealed class PipelineStageContext
    {
        #region Private Fields

        /// <summary>归属接收方(管线实现或并行阶段)。阶段写入时同步触发聚合(零闭包引用)。</summary>
        internal IStageContextSink Owner;

#if UNITY_EDITOR
        /// <summary>越线程写入是否已报首错(防洪泛;上下文每运行重建,违规按次运行重报一次)。</summary>
        bool _threadViolationLogged;
#endif

        #endregion

        #region 阶段写入

        /// <summary>阶段名称。由管线装配时设置。</summary>
        public string Name { get; internal set; }

        /// <summary>阶段权重。由管线装配时设置。</summary>
        public float Weight { get; internal set; } = 1f;

        /// <summary>阶段进度,0~1。</summary>
        public float Progress { get; internal set; }

        /// <summary>阶段描述文字。</summary>
        public string Description { get; internal set; }

        /// <summary>阶段状态。</summary>
        public PipelineStageState State { get; internal set; } = PipelineStageState.Pending;

        /// <summary>阶段内当前任务名称(容器子阶段转发用,可空)。经 <see cref="SetCurrentTaskName"/> 写入。</summary>
        public string CurrentTaskName { get; internal set; }

        /// <summary>
        /// 设置阶段内当前任务名,并触发管线聚合广播(经统一通知咽喉,含主线程断言)。
        /// <para>任务名变化本身即脏(阈值节流的三项之一),故只改任务名也会广播——UI 据此显示
        /// 「当前在跑哪个子任务」;null 表示未知,容器转发时回落子阶段名。</para>
        /// </summary>
        public void SetCurrentTaskName(string value)
        {
            CurrentTaskName = value;
            NotifyChanged();
        }

        /// <summary>设置阶段进度,自动 clamp 0~1(NaN 归一为 0),并触发管线聚合广播(经统一通知咽喉,含主线程断言)。</summary>
        public void SetProgress(float value)
        {
            // NaN 必须显式拦下:Mathf.Clamp01 的两支比较对 NaN 均为 false,会原样放行(实测);
            // NaN 进了加权和会让全局进度变 NaN,而脏判定的 Abs(NaN - x) >= 0.01f 恒为 false——
            // ≥1% 节流从那一刻起对进度写入永久失效,直到运行结束。归一为 0 与「权值和为 0 时回落 0」同义
            Progress = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
            NotifyChanged();
        }

        /// <summary>设置阶段描述,并触发管线聚合广播(经统一通知咽喉,含主线程断言)。</summary>
        public void SetDescription(string description)
        {
            Description = description;
            NotifyChanged();
        }

        /// <summary>设置阶段状态,并触发管线聚合广播(经统一通知咽喉,含主线程断言)。</summary>
        public void SetState(PipelineStageState state)
        {
            State = state;
            NotifyChanged();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 写入统一通知咽喉:同步触发接收方聚合;Editor 下先做主线程断言。
        /// <para>主线程检测复用 UniTask 的 <see cref="Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread"/>
        /// (mainThreadId 由 UniTask 域加载/运行时初始化在主线程固化),全限定调用与
        /// Pipeline.cs 中 <see cref="System.Diagnostics.Stopwatch"/> 风格一致;</para>
        /// <para>仅 <c>#if UNITY_EDITOR</c> 编译(Release 构建零开销);越线程写入只报首错防洪泛
        /// (<see cref="_threadViolationLogged"/>),上下文每运行重建 → 违规按次运行重报一次。</para>
        /// </summary>
        private void NotifyChanged()
        {
#if UNITY_EDITOR
            // 线程契约防护(仅编辑器断言):聚合遍历/节流快照非线程安全,写入须与调度同一上下文(Unity 主线程)
            if (!_threadViolationLogged && !Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
            {
                _threadViolationLogged = true;
                LogManager.Error(LogCategories.Pipeline,
                    "PipelineStageContext must be written from the Unity main thread; " +
                    "aggregation is not thread-safe. 阶段写入需与 RunAsync 调度同一上下文(见 IPipelineStage 契约)。");
            }
#endif
            Owner?.OnStageContextChanged(this);
        }

        #endregion
    }
}
