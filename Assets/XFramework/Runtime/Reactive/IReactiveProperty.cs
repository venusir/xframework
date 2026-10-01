using System;

namespace XFramework.XReactive
{
    /// <summary>
    /// 响应式属性接口。可订阅值变化并读取当前值。
    /// <para><see cref="Value"/> 无 setter:接口是只读视图,写值经具体实现类型
    /// (如 <c>ReactiveProperty&lt;T&gt;</c> 的 Value setter),避免外部误写状态。</para>
    /// <para><b>契约只有一条:订阅时立即同步回调当前值。</b>「相同值不通知(去重)」与「读取不抛异常」
    /// 都是 <c>ReactiveProperty&lt;T&gt;</c> / <c>ReadOnlyReactiveProperty&lt;T&gt;</c> 自己的行为,
    /// <b>不是本接口的保证</b>——Settings 的 <c>SettingRef&lt;T,TField&gt;</c> 同样实现本接口，
    /// 但实例替换时会<b>无条件重放</b>（值没变也回调）、读取在设置类型注销后照抛。</para>
    /// <para>按接口编程时不要把去重或「读不抛」当前提；确实需要写能力时另取
    /// <see cref="IReactivePropertyWriter{T}"/>。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    public interface IReactiveProperty<T>
    {
        /// <summary>当前值。无 setter——接口是只读视图,写值经具体实现类型或 <see cref="IReactivePropertyWriter{T}"/>。</summary>
        T Value { get; }

        /// <summary>订阅值变化。<b>订阅时立即同步回调一次当前值</b>,这是本接口唯一的契约。</summary>
        /// <param name="handler">值变化时的回调，不可为 null。</param>
        /// <returns>取消订阅的句柄；释放它即不再收到投递。</returns>
        IDisposable Subscribe(Action<T> handler);
    }
}
