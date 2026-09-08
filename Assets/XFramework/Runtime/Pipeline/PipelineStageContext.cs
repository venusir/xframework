using UnityEngine;

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
    /// <para>双层结构:阶段写面(<see cref="Progress"/>/<see cref="Description"/>/<see cref="State"/> + SetXxx)供阶段在
    /// <see cref="IPipelineStage.ExecuteAsync"/> 内写入,写入即同步触发接收方(管线或并行阶段)的聚合;
    /// 全局读面由管线填充,供 UI 读取当前运行状态。</para>
    /// <para>线程契约:写入须与 <see cref="IPipeline.RunAsync"/> 调度同一上下文(Unity 主线程)——写入同步触发
    /// 聚合与订阅者回调,整条链非线程安全;Editor 下越线程写入打 <see cref="Debug.LogError"/> 提示
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

        #region 阶段级(阶段写入)

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

        /// <summary>阶段内当前任务名称(容器子阶段转发用,可空)。</summary>
        public string CurrentTaskName { get; internal set; }

        /// <summary>设置阶段进度,自动 clamp 0~1,并触发管线聚合广播(经统一通知咽喉,含主线程断言)。</summary>
        public void SetProgress(float value)
        {
            Progress = Mathf.Clamp01(value);
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
                Debug.LogError("[Pipeline] PipelineStageContext must be written from the Unity main thread; " +
                    "aggregation is not thread-safe. 阶段写入需与 RunAsync 调度同一上下文(见 IPipelineStage 契约)。");
            }
#endif
            Owner?.OnStageContextChanged(this);
        }

        #endregion

        #region 全局级(管线填充,只读)

        /// <summary>全局进度,0~1。</summary>
        public float OverallProgress { get; internal set; }

        /// <summary>当前阶段名称。</summary>
        public string CurrentStageName { get; internal set; }

        /// <summary>总阶段数。</summary>
        public int TotalStageCount { get; internal set; }

        /// <summary>已完成阶段数。</summary>
        public int CompletedStageCount { get; internal set; }

        /// <summary>失败阶段数。</summary>
        public int FailedStageCount { get; internal set; }

        #endregion
    }
}
