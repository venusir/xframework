using System;

namespace XFramework.XReactive
{

    /// <summary>
    /// 响应式属性接口。可订阅值变化并读取当前值。
    /// <para><see cref="Value"/> 无 setter:接口是只读视图,写值经具体实现类型
    /// (如 <c>ReactiveProperty&lt;T&gt;</c> 的 Value setter),避免外部误写状态。</para>
    /// <para>契约:订阅时立即同步回调当前值;相同值不通知(去重语义)。</para>
    /// </summary>
    public interface IReactiveProperty<T>
    {
        T Value { get; }

        IDisposable Subscribe(Action<T> onNext);
    }
}
