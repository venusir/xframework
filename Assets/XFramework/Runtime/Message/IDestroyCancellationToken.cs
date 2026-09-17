using System.Threading;

namespace XFramework.XMessage
{

    /// <summary>
    /// 提供销毁时的 <see cref="CancellationToken"/>，把订阅与可释放资源自动绑定到对象的生命周期。
    /// <para>语义与 <c>MonoBehaviour.destroyCancellationToken</c> 一致，供<b>非 MonoBehaviour 对象</b>表达同一件事。</para>
    /// <para><see cref="MessageManager"/> 的订阅扩展方法识别此接口：实现了它的订阅者，在令牌取消时自动退订；
    /// 对象已销毁（令牌已取消）时订阅会立即释放。</para>
    /// <para><b>为什么定义在 Message 模块：</b>框架内唯一的消费者是 <see cref="MessageManager"/>
    /// （见 <c>TryBindToDestroy</c>），而同族的 <see cref="IMessagePublisher"/> / <see cref="IMessageSubscriber"/>
    /// 标记接口本就位于此处。它本身是通用生命周期契约，若将来出现第二个消费者，可整体迁往更中立的模块。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// public sealed class PlayerModel : IDestroyCancellationToken
    /// {
    ///     readonly CancellationTokenSource _cts = new CancellationTokenSource();
    ///
    ///     public CancellationToken DestroyCancellationToken => _cts.Token;
    ///
    ///     public void Dispose()
    ///     {
    ///         _cts.Cancel();
    ///         _cts.Dispose();
    ///     }
    /// }
    ///
    /// // 订阅随 PlayerModel 销毁自动取消
    /// model.Subscribe&lt;PlayerDiedMessage&gt;(OnPlayerDied);
    /// </code>
    /// </example>
    public interface IDestroyCancellationToken
    {
        /// <summary>
        /// 对象销毁时的 CancellationToken。绑定到此令牌的订阅会在对象销毁时自动取消。
        /// </summary>
        CancellationToken DestroyCancellationToken { get; }
    }
}
