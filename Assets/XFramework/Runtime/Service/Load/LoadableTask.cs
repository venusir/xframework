using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 加载任务基类。实现 <see cref="ILoadable"/> 并提供属性默认实现。
    /// <para>纯 C# 类，不继承节点。子类只需重写 <see cref="LoadAsync"/> 实现具体加载逻辑。</para>
    /// <para>自动管理 State 流转：Pending → Loading → Completed / Failed。</para>
    /// </summary>
    public abstract class LoadableTask : ILoadable
    {
        #region Public Properties

        /// <summary>任务名称。默认取类名，子类可在构造函数中修改。</summary>
        public string Name { get; protected set; }

        /// <summary>当前加载进度，取值范围 0.0 ~ 1.0。</summary>
        public float Progress { get; private set; }

        /// <summary>当前加载阶段的描述文字。</summary>
        public string Description { get; private set; }

        /// <summary>当前加载状态。</summary>
        public LoadState State { get; private set; } = LoadState.Pending;

        /// <summary>
        /// 加载权重。子类可在构造函数中修改此值。
        /// </summary>
        public float Weight { get; protected set; } = 1f;

        #endregion

        #region Constructor

        protected LoadableTask()
        {
            Name = GetType().Name;
        }

        #endregion

        #region ILoadable Implementation

        async UniTask ILoadable.LoadAsync(CancellationToken cancellationToken)
        {
            SetState(LoadState.Loading);

            try
            {
                await LoadAsync(cancellationToken);

                if (State != LoadState.Failed)
                {
                    SetProgress(1f);
                    SetState(LoadState.Completed);
                }
            }
            catch (OperationCanceledException)
            {
                SetDescription("已取消");
                SetState(LoadState.Failed);
            }
            catch (Exception ex)
            {
                SetDescription($"加载异常: {ex.Message}");
                SetState(LoadState.Failed);
            }
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// 子类在此实现具体的加载逻辑。
        /// <para>加载过程中应调用 <see cref="SetProgress"/>、<see cref="SetDescription"/> 更新进度和描述。</para>
        /// <para>如果加载失败，可调用 <see cref="SetState"/>(<see cref="LoadState.Failed"/>) 标记失败，或直接抛出异常由基类统一处理。</para>
        /// <para>通过 <paramref name="cancellationToken"/> 可监听取消信号，在取消时停止加载。</para>
        /// </summary>
        protected abstract UniTask LoadAsync(CancellationToken cancellationToken);

        /// <summary>设置当前加载进度，会自动 clamp 到 0~1 范围。</summary>
        protected void SetProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
        }

        /// <summary>设置当前加载阶段的描述文字。</summary>
        protected void SetDescription(string description)
        {
            Description = description;
        }

        /// <summary>设置当前加载状态。</summary>
        protected void SetState(LoadState state)
        {
            State = state;
        }

        #endregion
    }
}
