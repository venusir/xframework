using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 加载上下文。作为 <see cref="ILoadable.LoadAsync"/> 的参数传入，提供进度/状态/权重的读写能力。
    /// <para>由 <see cref="ILoader"/> 在装载时创建并注入，节点在加载过程中通过此对象报告进度。</para>
    /// </summary>
    public class LoadContext
    {
        #region Public Properties

        /// <summary>任务名称。由 Loader 在装载时设置。</summary>
        public string Name { get; internal set; }

        /// <summary>加载权重。由 Loader 在装载时设置。</summary>
        public float Weight { get; internal set; } = 1f;

        /// <summary>当前加载进度，取值范围 0.0 ~ 1.0。</summary>
        public float Progress { get; private set; }

        /// <summary>当前加载阶段的描述文字。</summary>
        public string Description { get; private set; }

        /// <summary>当前加载状态。</summary>
        public LoadState State { get; private set; } = LoadState.Pending;

        #endregion

        #region Public Methods

        /// <summary>设置当前加载进度，会自动 clamp 到 0~1 范围。</summary>
        public void SetProgress(float value)
        {
            Progress = Mathf.Clamp01(value);
        }

        /// <summary>设置当前加载阶段的描述文字。</summary>
        public void SetDescription(string description)
        {
            Description = description;
        }

        /// <summary>设置当前加载状态。</summary>
        public void SetState(LoadState state)
        {
            State = state;
        }

        #endregion
    }
}
