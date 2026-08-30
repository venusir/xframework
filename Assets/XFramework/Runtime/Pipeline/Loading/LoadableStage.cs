using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XPipeline
{

    /// <summary>
    /// 单加载任务阶段(内部):把 <see cref="ILoadable"/> 适配为管线阶段,由 <see cref="ParallelStage"/> 组内并行执行。
    /// <para>任务经 <see cref="LoadProgress"/> 写入,写后通知入口全字段镜像到子上下文并单次显式通知
    /// (一次任务写入恰好 1 次组级聚合);沉降后检查取消并显式上抛——防共享包装的契约兜底
    /// 把已取消任务误补为 Completed。</para>
    /// </summary>
    internal sealed class LoadableStage : IPipelineStage
    {
        #region Public API

        /// <summary>构造单任务阶段。</summary>
        /// <param name="loadable">本阶段承载的加载任务。</param>
        public LoadableStage(ILoadable loadable)
        {
            _loadable = loadable;
            _name = loadable.GetType().Name;
        }

        #endregion

        #region IPipelineStage

        /// <summary>阶段名 = 任务类型名(组级 CurrentTaskName 与失败诊断的来源)。</summary>
        public string Name => _name;

        /// <summary>单任务权重 1(组内加权聚合按任务数均分)。</summary>
        public float Weight => 1f;

        /// <summary>
        /// 以管线阶段形态执行单任务:创建 <see cref="LoadProgress"/> 注入任务,经门铃事件驱动
        /// 镜像到子上下文;沉降后检查取消并上抛 <see cref="OperationCanceledException"/>。
        /// </summary>
        public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
        {
            _childCtx = context;

            var progress = new LoadProgress
            {
                Name = _name,
                OnChanged = OnTaskChanged,
            };

            await RunTask(_loadable, progress, cancellationToken);

            _childCtx = null;

            // 取消检查:RunTask 已吞掉 OCE,此处显式上抛使共享包装走取消路径——
            // 否则正常返回会被契约兜底补置 Completed(取消任务误报完成)
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// LoadProgress 写后通知入口(门铃):全字段镜像到子上下文并单次显式通知,
        /// 一次任务写入恰好触发 1 次组级聚合。
        /// </summary>
        private void OnTaskChanged(LoadProgress changed)
        {
            var ctx = _childCtx;
            if (ctx == null) return; // 迟写防护:沉降后任务 fire-and-forget 写入直接忽略

            ctx.Weight = changed.Weight;
            ctx.Progress = changed.Progress;
            ctx.Description = changed.Description;
            ctx.CurrentTaskName = changed.Name;
            ctx.State = MapState(changed.State);

            ctx.Owner?.OnStageContextChanged(ctx);
        }

        /// <summary>加载状态 → 管线阶段状态映射(Loading → Executing,其余同名)。</summary>
        private static PipelineStageState MapState(LoadState state)
        {
            switch (state)
            {
                case LoadState.Loading: return PipelineStageState.Executing;
                case LoadState.Completed: return PipelineStageState.Completed;
                case LoadState.Failed: return PipelineStageState.Failed;
                default: return PipelineStageState.Pending;
            }
        }

        /// <summary>
        /// 包装执行单个加载任务:统一状态机写入、异常与取消捕获,保证任务必然收敛到终态。
        /// <para>正常返回但未写终态(如未调用 SetState 的实现)时自动补置为 <see cref="LoadState.Completed"/>,进度视为 1f;</para>
        /// <para>抛出的异常置为 <see cref="LoadState.Failed"/> 并写入描述,经镜像聚合报告;</para>
        /// <para>抛出 <see cref="OperationCanceledException"/> 视为取消,保持当前状态,由阶段统一走取消路径。</para>
        /// </summary>
        private static async UniTask RunTask(ILoadable loadable, LoadProgress ctx, CancellationToken cancellationToken)
        {
            ctx.SetState(LoadState.Loading);

            try
            {
                await loadable.LoadAsync(ctx, cancellationToken);

                // 契约兜底:正常返回但状态仍停留在 Pending/Loading → 视为完成
                if (ctx.State == LoadState.Pending || ctx.State == LoadState.Loading)
                {
                    ctx.SetProgress(1f);
                    ctx.SetState(LoadState.Completed);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消:保持当前状态,由阶段统一走取消路径,不视为失败
            }
            catch (Exception ex)
            {
                // 先写描述再置状态:状态写入触发聚合时描述已是异常消息(事件驱动取最新值)
                ctx.SetDescription(ex.Message);
                ctx.SetState(LoadState.Failed);
            }
        }

        #endregion

        #region Private Fields

        readonly ILoadable _loadable;
        readonly string _name;

        /// <summary>子阶段上下文(由 ParallelStage 注入),镜像广播的转发目标。沉降后置 null 迟写防护。</summary>
        PipelineStageContext _childCtx;

        #endregion
    }
}
