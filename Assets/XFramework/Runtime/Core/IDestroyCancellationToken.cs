using System.Threading;

namespace XFramework.XCore
{

    /// <summary>
    /// 提供销毁时的 CancellationToken，用于自动取消订阅和释放资源。
    /// <para>类似于 MonoBehaviour.destroyCancellationToken。</para>
    /// <para>实现此接口后，通过 <see cref="MessageBus"/> 的扩展方法订阅消息时，
    /// 订阅会自动绑定到对象的生命周期，对象销毁时自动取消订阅。</para>
    /// </summary>
    public interface IDestroyCancellationToken
    {
        /// <summary>
        /// 对象销毁时的 CancellationToken。绑定到此 Token 的订阅会在对象销毁时自动取消。
        /// </summary>
        CancellationToken DestroyCancellationToken { get; }
    }
}
