namespace XFramework.XReactive
{
    /// <summary>
    /// 可写的响应式属性。
    /// <para><see cref="IReactiveProperty{T}"/> 刻意只暴露只读视图——写值经具体实现类型，
    /// 避免外部误写状态。本接口是「确实需要写入」时按需索取的能力契约：绑定 API 收它而不是
    /// 具体类 <c>ReactiveProperty&lt;T&gt;</c>，于是任何第三方实现都能接入双向绑定
    /// （<c>SettingRef</c> 即是其中之一）。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    public interface IReactivePropertyWriter<T> : IReactiveProperty<T>
    {
        /// <summary>
        /// 尝试写入值，并回传写入后目标实际持有的值。
        /// <para>约定：目标已失效时返回 <c>false</c>，此时 <paramref name="actual"/> 为 <c>default</c>，
        /// 且<b>不抛异常</b>——绑定层不该因为目标失效而把异常抛进 UI 事件回调。「已失效」的判断归实现
        /// 自己：<c>ReactiveProperty&lt;T&gt;</c> 看是否已释放，<c>SettingRef</c> 看设置类型是否仍注册。</para>
        /// <para>成功时 <paramref name="actual"/> 由实现直接给出（不重读 <c>Value</c> 的 getter，
        /// 避免依赖各实现对「已失效」的策略）。写入会同步派发通知，订阅者可能在派发中再次写入：
        /// <c>ReactiveProperty&lt;T&gt;</c> 回传派发后的字段值（可能已是后来者），<c>SettingRef</c>
        /// 回传本次写入值——**重入场景下两者可能分叉**，调用方不要依赖 <paramref name="actual"/> 的具体来源。
        /// 要「现在的值」请另读 <see cref="IReactiveProperty{T}.Value"/>（并自行处理目标失效）。</para>
        /// <para><b>一处别误读：</b>返回 <c>true</c> <b>不</b>表示值变了——写入与当前值相同时会被去重、
        /// 订阅者收不到通知，但仍返回 <c>true</c>。</para>
        /// </summary>
        /// <param name="value">要写入的值。</param>
        /// <param name="actual">写入后目标实际持有的值；返回 false 时为 default。</param>
        /// <returns>确实写入了返回 true；目标已失效返回 false。</returns>
        bool TryWriteValue(T value, out T actual);
    }
}
