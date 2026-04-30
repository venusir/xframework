using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace XFramework
{
    /// <summary>
    /// 基于 YooAsset 的资源服务实现。
    /// <para>内部类，不对外暴露。外部通过 <see cref="IAssetService"/> 接口访问。</para>
    /// </summary>
    class YooAssetServiceImpl : IAssetService, ILoadableProvider
    {
        private readonly string _packageName;
        private ResourcePackage _package;

        /// <summary>已加载资源的引用计数。</summary>
        private readonly Dictionary<string, int> _refCounts = new Dictionary<string, int>();

        /// <summary>已加载资源的缓存。</summary>
        private readonly Dictionary<string, object> _cache = new Dictionary<string, object>();

        /// <summary>YooAsset 资源句柄缓存，用于释放时通知 YooAsset 卸载底层资源。</summary>
        private readonly Dictionary<string, YooAsset.AssetHandle> _yooHandles = new Dictionary<string, YooAsset.AssetHandle>();

        public YooAssetServiceImpl(string packageName = "DefaultPackage")
        {
            _packageName = packageName;
        }

        #region ILoadableProvider

        void ILoadableProvider.MountLoadables(ILoadCollector collector)
        {
            var initTask = new YooAssetInitTask(_packageName);
            collector.AddLoadable(initTask);
        }

        #endregion

        #region IAssetService

        public async UniTask<AssetHandle> LoadAssetAsync(string location, CancellationToken cancellationToken = default)
        {
            if (_package == null)
            {
                _package = YooAssets.TryGetPackage(_packageName);
                if (_package == null)
                {
                    return default;
                }
            }

            var operation = _package.LoadAssetAsync(location);
            await operation.WithCancellation(cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
            {
                return default;
            }

            // 增加引用计数
            if (_refCounts.ContainsKey(location))
                _refCounts[location]++;
            else
                _refCounts[location] = 1;

            _cache[location] = operation.AssetObject;
            _yooHandles[location] = operation;

            return new AssetHandle(operation.AssetObject, this, location);
        }

        public void Release(AssetHandle handle)
        {
            string location = handle.Location;
            if (string.IsNullOrEmpty(location))
                return;

            ReleaseAsset(location);
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// 内部释放指定位置的资源。
        /// <para>引用计数归零时，通知 YooAsset 释放底层 AssetBundle。</para>
        /// </summary>
        internal void ReleaseAsset(string location)
        {
            if (!_refCounts.ContainsKey(location))
                return;

            _refCounts[location]--;
            if (_refCounts[location] <= 0)
            {
                _refCounts.Remove(location);
                _cache.Remove(location);

                // 通知 YooAsset 释放底层资源（减少 Provider 引用计数，卸载 AssetBundle）
                if (_yooHandles.TryGetValue(location, out var yooHandle))
                {
                    yooHandle.Release();
                    _yooHandles.Remove(location);
                }
            }
        }

        #endregion
    }
}
