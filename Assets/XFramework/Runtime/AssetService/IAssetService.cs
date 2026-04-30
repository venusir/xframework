using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 资源服务接口。提供统一的资源加载与释放能力。
    /// <para>实现此接口的服务节点可通过 <see cref="BaseNode.Get{T}"/> 被其他节点访问。</para>
    /// </summary>
    public interface IAssetService
    {
        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>资源句柄，可通过 <see cref="AssetHandle.GetAsset{T}"/> 获取具体资源。</returns>
        UniTask<AssetHandle> LoadAssetAsync(string location, CancellationToken cancellationToken = default);

        /// <summary>
        /// 释放指定句柄引用的资源（引用计数减一）。
        /// <para>当引用计数归零时，资源将被卸载。</para>
        /// </summary>
        void Release(AssetHandle handle);
    }
}
