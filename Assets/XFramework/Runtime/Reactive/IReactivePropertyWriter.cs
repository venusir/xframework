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
        /// <para>成功时 <paramref name="actual"/> 是写入后目标实际持有的值，调用方应当读它，
        /// <b>不要</b>再去访问 <see cref="IReactiveProperty{T}.Value"/>：写入会同步派发通知，某个订阅者
        /// 在派发中释放目标也照常发生，而 <paramref name="actual"/> 由实现直接给出——读它就不必依赖
        /// <c>Value</c> 对「已失效」采取何种策略（那是各实现自己的事，接口并未统一）。</para>
        /// <para><b>两处别误读：</b>① 返回 <c>true</c> <b>不</b>表示值变了——写入与当前值相同时会被去重、
        /// 订阅者收不到通知，但仍返回 <c>true</c>；② <paramref name="actual"/> <b>不</b>保证经过规范化，
        /// 随框架发布的两个实现都不取整也不钳制，它恒等于 <paramref name="value"/>。</para>
        /// </summary>
        /// <param name="value">要写入的值。</param>
        /// <param name="actual">写入后目标实际持有的值；返回 false 时为 default。</param>
        /// <returns>确实写入了返回 true；目标已失效返回 false。</returns>
        bool TryWriteValue(T value, out T actual);
    }
}
