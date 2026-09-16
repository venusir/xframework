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
        /// 尝试写入值。
        /// <para>约定：不支持写入或已释放时返回 false，<b>不抛异常</b>——绑定层不该因为
        /// 目标失效而把异常抛进 UI 事件回调。</para>
        /// </summary>
        /// <param name="value">要写入的值。</param>
        /// <returns>确实写入返回 true。</returns>
        bool TryWriteValue(T value);
    }
}
