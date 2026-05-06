using System;

namespace XFramework
{
    /// <summary>
    /// 响应式生命周期接口。将节点生命周期事件转为可订阅信号。
    /// <para>通过 <see cref="ReactiveLifecycle.GetLifecycle(BaseNode)"/> 获取生命周期信号。</para>
    /// </summary>
    public interface IReactiveLifecycle
    {
        /// <summary>Awake 完成时触发。</summary>
        IReadonlySignal OnInitializedSignal { get; }

        /// <summary>Start 完成时触发。</summary>
        IReadonlySignal OnStartedSignal { get; }

        /// <summary>Destroy 时触发。</summary>
        IReadonlySignal OnDestroyedSignal { get; }
    }
}
