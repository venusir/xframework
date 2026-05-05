using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 请求-响应节点。支持 Request/Response 模式。
    /// <para>注册请求处理器后，可通过 <see cref="RequestAsync{TRequest,TResponse}"/> 发送请求并等待响应。</para>
    /// </summary>
    public class RequestNode : BaseNode, IRequestNode
    {
        #region Private Fields

        private readonly Dictionary<Type, object> _handlers = new();

        #endregion

        #region Public Methods

        /// <summary>
        /// 注册请求处理器。
        /// </summary>
        /// <typeparam name="TRequest">请求类型。</typeparam>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        /// <param name="handler">异步处理器。</param>
        public void Register<TRequest, TResponse>(Func<TRequest, UniTask<TResponse>> handler)
        {
            _handlers[typeof(TRequest)] = handler;
        }

        /// <summary>
        /// 发送请求并等待响应。
        /// </summary>
        /// <typeparam name="TRequest">请求类型。</typeparam>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        /// <param name="request">请求对象。</param>
        /// <returns>响应对象。</returns>
        /// <exception cref="InvalidOperationException">未注册对应的处理器时抛出。</exception>
        public async UniTask<TResponse> RequestAsync<TRequest, TResponse>(TRequest request)
        {
            if (_handlers.TryGetValue(typeof(TRequest), out var handler))
            {
                return await ((Func<TRequest, UniTask<TResponse>>)handler)(request);
            }
            throw new InvalidOperationException(
                $"No handler registered for request type '{typeof(TRequest).Name}'.");
        }

        #endregion

        #region Lifecycle Overrides

        protected override void OnDestroy()
        {
            _handlers.Clear();
        }

        #endregion
    }
}
