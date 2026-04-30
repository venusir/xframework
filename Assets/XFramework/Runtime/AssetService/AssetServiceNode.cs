using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 资源服务节点。作为 <see cref="LeafNode"/> 挂载到节点树中，提供全局资源加载能力。
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
    /// <para>内部使用 YooAsset 实现资源加载，对外仅暴露 <see cref="IAssetService"/> 接口。</para>
    /// </summary>
    public class AssetServiceNode : LeafNode, IAssetService, ILoadableProvider
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
            _serviceImpl?.Destroy();
            _serviceImpl = null;
            base.OnDestroy();
        }

        #endregion

        #region ILoadableProvider

        void ILoadableProvider.MountLoadables(ILoadCollector collector)
        {
            collector.AddLoadable(new YooAssetInitTask());
        }

        #endregion

        #region IAssetService

        public UniTask<AssetHandle> LoadAssetAsync(string location, CancellationToken cancellationToken = default)
        {
            return _serviceImpl.LoadAssetAsync(location, cancellationToken);
        }

        public void LoadAssetAsync(string location, Action<AssetHandle> onCompleted, Action<string> onError = null)
        {
            _serviceImpl.LoadAssetAsync(location, onCompleted, onError);
        }

        public void Release(AssetHandle handle)
        {
            _serviceImpl.Release(handle);
        }

        #endregion
    }
}
