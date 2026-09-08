using System;
using System.Threading;
using UnityEngine;

namespace XFramework.XPipeline
{
    /// <summary>
    /// 容器子阶段加权聚合器(内部):并行容器(<see cref="ParallelStage"/>)与串行容器共用的
    /// 「门铃 + 加权聚合 + 转发」实现。
    /// <para>持有子上下文数组与广播快照,作为子上下文 Owner(<see cref="IStageContextSink"/>):
    /// 子阶段写入即触发组内加权聚合(经共享扫描助手 <see cref="ContextAggregation"/> 完成
    /// Σ(w·p)/Σ(w) 与阈值节流),收敛后转发容器主上下文;首失败诊断捕获(诊断优先);
    /// 失败时对注入的链接取消源调 <see cref="CancellationTokenSource.Cancel"/> 中断在途兄弟
    /// (仅并行容器;串行容器注入 null)。</para>
    /// <para>每次执行前经 <see cref="Reset"/> 注入转发目标并复位状态;容器沉降后经
    /// <see cref="ReleaseLinkedCts"/> 释放取消源引用;执行结束经 <see cref="Settle"/> 置沉降标志
    /// (迟写防护)。</para>
    /// </summary>
    internal sealed class StageAggregator : IStageContextSink
    {
        #region Public API

        /// <summary>构造聚合器,分配子上下文数组与状态快照数组。</summary>
        /// <param name="childCount">子阶段数量。</param>
        internal StageAggregator(int childCount)
        {
            ChildContexts = new PipelineStageContext[childCount];
            _lastStates = new PipelineStageState[childCount];
        }

        /// <summary>子阶段上下文数组(容器执行时装配填充)。</summary>
        internal readonly PipelineStageContext[] ChildContexts;

        /// <summary>本次执行是否有子阶段失败(首失败后保持,诊断一致性)。</summary>
        internal bool AnyFailed { get; private set; }

        /// <summary>首失败子阶段的描述。</summary>
        internal string FailDescription { get; private set; }

        /// <summary>首失败子阶段的任务名。</summary>
        internal string FailTaskName { get; private set; }

        /// <summary>
        /// 每次执行开始调用:注入转发目标(容器主上下文)与链接取消源(失败时中断在途兄弟;
        /// 串行容器传 null),复位全部聚合状态。
        /// </summary>
        internal void Reset(PipelineStageContext target, CancellationTokenSource linkedCts)
        {
            _target = target;
            _linkedCts = linkedCts;
            _settled = false;
            _lastOverall = -1f;
            _lastDesc = null;
            _aggregating = false;
            _dirty = false;
            AnyFailed = false;
            FailDescription = null;
            FailTaskName = null;
        }

        /// <summary>
        /// 释放链接取消源引用。容器沉降后调用,防止终局路径(取消源经 using 释放后)
        /// 在途写入误 Cancel 已释放的取消源。
        /// </summary>
        internal void ReleaseLinkedCts()
        {
            _linkedCts = null;
        }

        /// <summary>执行结束调用:置沉降标志(迟写防护),释放链接取消源引用。</summary>
        internal void Settle()
        {
            _settled = true;
            _linkedCts = null;
        }

        #endregion

        #region IStageContextSink (显式实现)

        /// <summary>
        /// 子上下文写入通知入口(门铃)。聚合广播发生在子阶段写入栈内,广播中子阶段再次写入会递归,
        /// 用 <see cref="_aggregating"/> + <see cref="_dirty"/> 延迟重聚合;聚合整体 try/catch,
        /// 订阅者(UI)异常不得冒泡进子阶段栈把无辜子阶段打成 Failed。
        /// </summary>
        void IStageContextSink.OnStageContextChanged(PipelineStageContext changed)
        {
            if (_settled) return; // 迟写防护:沉降后子阶段再写 → 忽略

            if (_aggregating)
            {
                _dirty = true;
                return;
            }

            _aggregating = true;
            try
            {
                Aggregate();
            }
            finally
            {
                _aggregating = false;
            }

            if (_dirty)
            {
                _dirty = false;
                Aggregate();
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 组内聚合:加权 Σ(w·p)/Σ(w) + 阈值节流(≥1% || 描述变化 || 状态变化)后转发主上下文;
        /// 失败检测立即取消在途兄弟子阶段(注入的链接取消源,仅并行容器注入)。
        /// </summary>
        private void Aggregate()
        {
            try
            {
                // 加权扫描共享助手(组模式):Σ(w·p)/Σ(w) + 当前描述/任务名 + 首失败诊断捕获,
                // 完成记全权、执行中记 w·p、失败权重移出(进度略回退,语义正确)
                var scan = ContextAggregation.Scan(ChildContexts, ContextAggregation.ScanMode.Group);

                // 差异后缀:失败判定与诊断优先(描述/任务名强制取首失败子阶段,避免被兄弟描述覆盖)
                string currentDesc = scan.Description;
                string currentTaskName = scan.TaskName;
                if (scan.FailedCount > 0)
                {
                    AnyFailed = true;
                    if (scan.FailDescription != null)
                        currentDesc = scan.FailDescription;
                    if (scan.FailTaskName != null)
                        currentTaskName = scan.FailTaskName;
                }

                float overallProgress = scan.Overall;

                // 阈值节流:总体进度变化 ≥1% || 描述变化 || 任一子阶段状态变化(共享扫描,每帧路径零 LINQ)
                bool dirty = ContextAggregation.IsDirty(overallProgress, currentDesc,
                    _lastOverall, _lastDesc, ChildContexts, _lastStates);

                if (dirty)
                {
                    _target.SetProgress(overallProgress);
                    _target.SetDescription(currentDesc ?? ContextAggregation.RunningDescriptionPlaceholder);
                    _target.CurrentTaskName = currentTaskName;

                    _lastOverall = overallProgress;
                    _lastDesc = currentDesc ?? ContextAggregation.RunningDescriptionPlaceholder;
                    ContextAggregation.CopyStates(ChildContexts, _lastStates);
                }

                if (scan.FailedCount > 0)
                    _linkedCts?.Cancel(); // 中断在途兄弟子阶段,经 WhenAll 沉降
            }
            catch (Exception ex)
            {
                // 防御:聚合/广播链上的异常(如订阅者)隔离在此,不冒泡进子阶段栈
                Debug.LogError($"[Pipeline] progress aggregation failed: {ex.Message}");
            }
        }

        #endregion

        #region Private Fields

        /// <summary>转发目标(容器主上下文),由 Reset 注入。</summary>
        PipelineStageContext _target;

        /// <summary>链接取消源(失败时中断在途兄弟;串行容器为 null),由 Reset 注入、Release/Settle 释放。</summary>
        CancellationTokenSource _linkedCts;

        /// <summary>已沉降标志(迟写防护:沉降后子阶段再写经门铃直接忽略)。</summary>
        bool _settled;

        /// <summary>上一帧广播快照:组进度 + 描述 + 各子阶段状态(阈值节流,首帧 -1 保证必广播)。</summary>
        float _lastOverall = -1f;
        string _lastDesc;
        readonly PipelineStageState[] _lastStates;

        /// <summary>聚合重入保护:聚合广播中子阶段再次写入时置 dirty,外层收尾再聚合。</summary>
        bool _aggregating;
        bool _dirty;

        #endregion
    }
}
