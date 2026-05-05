using System;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 请求-响应节点。发送请求并等待响应。
    /// <para>适用于需要双向通信的场景，如查询数据、执行命令并获取结果。</para>
    /// </summary>
    public interface IRequestNode : IBaseNode
    {
        /// <summary>发送请求并等待响应。</summary>
        UniTask<TResponse> RequestAsync<TRequest, TResponse>(TRequest request);
    }
}
