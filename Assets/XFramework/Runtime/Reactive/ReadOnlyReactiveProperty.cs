using System;
using System.Collections.Generic;
using XFramework.XMessage.Internal;

namespace XFramework.XReactive
{
    /// <summary>
    /// 只读响应式属性。由 <see cref="ReactiveProperty{T}"/> 通过 <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 派生。
    /// <para>仅暴露 <see cref="Value"/>（只读）和 <see cref="Subscribe"/>，不可赋值。</para>
    /// <para>基于 XMessage 事件流引擎实现,内部订阅源属性做值映射。</para>
    /// <para>使用完毕后需调用 <see cref="Dispose"/> 释放内部订阅。</para>
    /// </summary>
    /// <typeparam name="T">值的类型。</typeparam>
    /// <remarks>
    /// 行为契约:
    /// - <see cref="Subscribe"/> 订阅时立即同步回调当前映射值(UI 初始绑定依赖)
    /// - 源值变化沿映射链传播,映射结果与当前值相同不通知(去重语义)
    /// - <see cref="Value"/> getter 不做 disposed 检查(宽容读取);
    ///   <see cref="Subscribe"/> 在已释放时抛 <see cref="ObjectDisposedException"/>
    /// <para><b>生命周期：</b>本类型是 <see cref="IDisposable"/>,且由 <c>Select</c> 创建时就订阅了源——
    /// 谁创建谁释放,丢弃返回值会让它被源永久引用。详见
    /// <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/>。</para>
    /// <para>
    /// 实现 <see cref="IReactiveProperty{T}"/>:既可直接交给收该接口的绑定 API,也可作为
    /// <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 的源继续映射
    /// (派生值仍是可被订阅的只读值,链式成立)。这正是它「只读视图」定位的体现——
    /// 接口本身无 setter,不会因此获得写入能力。
    /// </para>
    /// </remarks>
    public class ReadOnlyReactiveProperty<T> : IReactiveProperty<T>, IDisposable
    {
        #region Private Fields

        private readonly EventStream<T> _stream = new();
        private IDisposable _sourceSub;
        private T _value;
        private bool _disposed;

        #endregion

        #region Constructor (internal — only created via ReactivePropertyExtensions.Select)

        private ReadOnlyReactiveProperty()
        {
        }

        /// <summary>
        /// 从源属性派生只读属性(静态泛型工厂:TSource 无法在类级泛型表达,故用方法级泛型)。
        /// <para>初始化时取源当前值作为初始值(不通知),之后订阅源做映射推送。</para>
        /// <para><b><paramref name="selector"/> 在此处被调用两次</b>:上一段取初值一次,源订阅的立即回调
        /// 再一次(同一输入,结果被 <see cref="Set"/> 去重丢弃)。这是「先算初值」与「订阅即回调当前值」
        /// 两条语义叠加的必然结果,故 selector 须为纯函数——详见
        /// <see cref="ReactivePropertyExtensions.Select{TSource, TResult}"/> 的说明。</para>
        /// </summary>
        internal static ReadOnlyReactiveProperty<TResult> Create<TSource, TResult>(
            IReactiveProperty<TSource> source, Func<TSource, TResult> selector)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var result = new ReadOnlyReactiveProperty<TResult>
            {
                _value = selector(source.Value)
            };
            // 源订阅:值变化时映射并推送(源自身已去重,这里对映射结果再去重一次)
            result._sourceSub = source.Subscribe(srcValue => result.Set(selector(srcValue)));
            return result;
        }

        #endregion

        #region Public Properties

        /// <summary>获取当前值。</summary>
        public T Value => _value;

        /// <summary>
        /// 当前存活订阅数。
        /// <para><b>诊断用</b>——订阅泄漏在运行时是无声的，本属性是唯一的观察手段。想看
        /// <b>源</b>上有没有多余的订阅（例如某个派生值被就地丢弃）请查源的
        /// <c>ReactiveProperty&lt;T&gt;.SubscriptionCount</c>；本属性数的是挂在自己身上的订阅者。</para>
        /// <para>仅供排查，<b>不要</b>拿它做逻辑分支：它是实现的当前状态，不是契约。</para>
        /// </summary>
        public int SubscriptionCount => _stream.SubscriptionCount;

        #endregion

        #region Subscribe

        /// <summary>订阅值变化。订阅时立即回调当前值。</summary>
        /// <param name="onNext">值变化时的回调。</param>
        /// <returns>订阅句柄，可用于取消订阅。</returns>
        /// <exception cref="ArgumentNullException">onNext 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException">属性已释放时抛出。</exception>
        /// <remarks>立即回调抛出的异常原样上抛(订阅已自动清理,不会泄漏)；之后投递中的异常记 Error 日志后继续。</remarks>
        public IDisposable Subscribe(Action<T> onNext)
        {
            if (onNext == null) throw new ArgumentNullException(nameof(onNext));
            ThrowIfDisposed();

            var handle = _stream.Subscribe(onNext);
            try
            {
                onNext(_value);
            }
            catch
            {
                // 同 ReactiveProperty.Subscribe:注册期异常原样上抛,但必须先退订,
                // 否则句柄交不出去、该订阅永久泄漏。
                handle.Dispose();
                throw;
            }
            return handle;
        }

        #endregion

        #region IDisposable

        /// <summary>释放内部订阅并取消源订阅。</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _sourceSub?.Dispose();
            _stream.Dispose();
        }

        #endregion

        #region Private

        private void Set(T value)
        {
            if (_disposed)
                return;
            // 映射结果去重:与当前值相同不通知
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;
            _value = value;
            _stream.OnNext(value);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name,
                    $"[Reactive] ReadOnlyReactiveProperty<{typeof(T).Name}> 已释放,请勿再订阅。");
        }

        #endregion

        #region Object

        /// <summary>
        /// 诊断展示：类型与当前映射值（如 <c>ReadOnlyReactiveProperty&lt;String&gt;(Lv.50)</c>）。
        /// <para>格式与 <c>ReactiveProperty&lt;T&gt;.ToString()</c> 同形。宽容读取：已释放后仍展示
        /// 最后持有的值，不抛异常。</para>
        /// </summary>
        public override string ToString()
        {
            return $"{nameof(ReadOnlyReactiveProperty<T>)}<{typeof(T).Name}>({_value})";
        }

        #endregion
    }

    /// <summary>
    /// 为 <see cref="ReactiveProperty{T}"/> 提供 LINQ 风格的转换扩展。
    /// <para>响应式属性的链式操作（映射派生）统一经此类扩展方法进行。</para>
    /// </summary>
    public static class ReactivePropertyExtensions
    {
        /// <summary>
        /// 将响应式属性映射为只读派生属性，值随源自动变化。
        /// <para>例: <c>level.Select(lv => $"Lv.{lv}")</c></para>
        /// <para>返回值是 <see cref="ReadOnlyReactiveProperty{T}"/>，只能 Subscribe 不能赋值，符合派生值的语义。</para>
        /// <para><b>返回值必须有人持有并释放：</b>它在构造时就订阅了源,所以源的事件流引用着它——
        /// 就地丢弃返回值等于让它永久订阅下去,此后每次源变化都会再跑一遍 selector。注意
        /// <c>BindToXxx</c> 返回的是<b>绑定方</b>的句柄,释放它<b>不能</b>释放派生值。
        /// 归口的现成去处：<c>ViewModelBase.CreateReadOnlyProperty</c>（创建 + 登记,随 ViewModel 释放）
        /// 或 <c>UIViewBase.Track(handle)</c>。</para>
        /// <para><b>selector 必须是纯函数：</b>构造时它会被调用<b>两次</b>——一次取初值，一次来自源订阅的
        /// 立即回调（结果与初值相同、被去重丢弃）。纯映射只是白算一遍；带副作用或非确定性的 selector
        /// （计数器、随机数、缓存填充）会跑两遍，且只有第二次的结果留在 <see cref="Value"/> 上。</para>
        /// </summary>
        /// <typeparam name="TSource">源值类型。</typeparam>
        /// <typeparam name="TResult">结果值类型。</typeparam>
        /// <param name="source">源响应式属性。</param>
        /// <param name="selector">值映射函数，必须是纯函数（构造时会被调用两次）。</param>
        /// <returns>新的只读响应式属性。它是 <see cref="IDisposable"/>，需交给生命周期所有者释放。</returns>
        /// <exception cref="ArgumentNullException">source 或 selector 为 null 时抛出。</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="source"/> 已释放时抛出——
        /// 注意取值本身是宽容的（读已释放的源仍返回最后的值），但订阅不是。</exception>
        /// <exception cref="Exception">selector 在构造期抛出的异常原样上抛。</exception>
        public static ReadOnlyReactiveProperty<TResult> Select<TSource, TResult>(
            this IReactiveProperty<TSource> source,
            Func<TSource, TResult> selector)
        {
            return ReadOnlyReactiveProperty<TResult>.Create(source, selector);
        }
    }
}
