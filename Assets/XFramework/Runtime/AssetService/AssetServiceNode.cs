using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 资源服务节点。作为 <see cref="EntityNode"/> 挂载到节点树中，提供全局资源加载能力。
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
    /// <para>内部使用 YooAsset 实现资源加载，对外仅暴露 <see cref="IAssetService"/> 接口。</para>
    /// </summary>
    public class AssetServiceNode : EntityNode, IAssetService, ILoadableProvider
    {
        #region Private Fields

        private IAssetService _serviceImpl;

        #endregion

        #region Lifecycle

        protected override void OnAwake()
        {
            base.OnAwake();
            _serviceImpl = new YooAssetServiceImpl();
        }

        protected override void OnDestroy()
        {
            _serviceImpl = null;
            base.OnDestroy();
        }

        #endregion

        #region ILoadableProvider

        void ILoadableProvider.MountLoadables(ILoadCollector collector)
        {
            if (_serviceImpl is ILoadableProvider provider)
            {
                provider.MountLoadables(collector);
            }
        }

        #endregion

        #region IAssetService

        public UniTask<AssetHandle> LoadAssetAsync(string location, CancellationToken cancellationToken = default)
        {
            return _serviceImpl.LoadAssetAsync(location, cancellationToken);
        }

        public void Release(AssetHandle handle)
        {
            _serviceImpl.Release(handle);
        }

        #endregion
    }
}
