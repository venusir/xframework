using System;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 加载器接口。对外暴露的加载调度入口，隐藏 <see cref="Loader"/> 实现。
    /// <para>通过 <see cref="AddLoadable"/> 注册实现了 <see cref="ILoadable"/> 的节点，调用 <see cref="LoadAsync"/> 统一调度。</para>
    /// </summary>
    public interface ILoader
    {
        /// <summary>是否正在加载中。</summary>
        bool IsLoading { get; }

        /// <summary>当前总体进度，取值范围 0.0 ~ 1.0。</summary>
        float Progress { get; }

        /// <summary>当前加载阶段的描述文字。</summary>
        string Description { get; }

        /// <summary>加载进度变更事件。每帧轮询时触发，传递当前进度快照。</summary>
        event Action<LoadProgressSnapshot> OnProgressUpdate;

        /// <summary>全部加载完成事件。</summary>
        event Action OnLoadCompleted;

        /// <summary>加载失败事件。参数为失败原因描述。</summary>
        event Action<string> OnLoadFailed;

        /// <summary>
        /// 注册一个实现了 <see cref="ILoadable"/> 的加载任务。
        /// </summary>
        void AddLoadable(ILoadable loadable, string name = null, float weight = 1f);

        /// <summary>
        /// 执行加载。调度所有已注册的加载任务并统一调度。
        /// </summary>
        UniTask LoadAsync();

        /// <summary>
        /// 销毁加载器，清理内部状态和事件订阅。
        /// <para>调用后不应再使用此实例。</para>
        /// </summary>
        void Destroy();
    }
}
