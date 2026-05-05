using System;

namespace XFramework
{
    /// <summary>
    /// 无参数事件标记类型。用于不需要传递数据的信号。
    /// </summary>
    public readonly struct Unit { }

    /// <summary>
    /// 响应式生命周期接口。将节点生命周期事件转为可订阅信号。
    /// <para>通过 <see cref="ReactiveLifecycle.AddLifecycle(BaseNode)"/> 为节点激活生命周期信号。</para>
    /// </summary>
    public interface IReactiveLifecycle
    {
        /// <summary>Awake 完成时触发。</summary>
        IReadonlySignal<Unit> OnInitializedSignal { get; }

        /// <summary>Start 完成时触发。</summary>
        IReadonlySignal<Unit> OnStartedSignal { get; }

        /// <summary>Destroy 时触发。</summary>
        IReadonlySignal<Unit> OnDestroyedSignal { get; }
    }
}
