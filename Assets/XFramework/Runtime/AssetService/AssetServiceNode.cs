using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 资源服务节点。作为 <see cref="LeafNode"/> 挂载到节点树中，提供全局资源加载能力。
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
    /// <para>内部使用 YooAsset 实现资源加载，对外暴露 <see cref="IAssetService"/> 接口。</para>
    /// <para>自动管理引用计数、对象池、延迟卸载。</para>
    /// </summary>
    public class AssetServiceNode : LeafNode, IAssetService, ILoadableProvider
    {
        #region Private Fields

        private YooAssetServiceImpl _serviceImpl;

        /// <summary>资源实例 → location 映射（用于 Release 通过资源实例查找 location）。</summary>
        private readonly Dictionary<UnityEngine.Object, string> _assetToLocation = new Dictionary<UnityEngine.Object, string>();

        /// <summary>实例 GameObject → location 映射（用于 DestroyInstance 查找 location）。</summary>
        private readonly Dictionary<GameObject, string> _instanceToLocation = new Dictionary<GameObject, string>();

        /// <summary>location → 当前活跃实例数。</summary>
        private readonly Dictionary<string, int> _locationCounts = new Dictionary<string, int>();

        /// <summary>location → 对象池（已 deactive 的闲置实例）。</summary>
        private readonly Dictionary<string, Stack<GameObject>> _pools = new Dictionary<string, Stack<GameObject>>();

        /// <summary>每种预制体最多保留的闲置实例数。</summary>
        private const int MaxPoolSize = 5;

        #endregion

        #region Lifecycle

        protected override void OnAwake()
        {
            base.OnAwake();
            _serviceImpl = new YooAssetServiceImpl();
        }

        protected override void OnDestroy()
        {
            // 清理所有池中的实例
            foreach (var kvp in _pools)
            {
                foreach (var go in kvp.Value)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            _pools.Clear();

            _serviceImpl?.Destroy();
            _serviceImpl = null;

            _assetToLocation.Clear();
            _instanceToLocation.Clear();
            _locationCounts.Clear();

            base.OnDestroy();
        }

        #endregion

        #region ILoadableProvider

        void ILoadableProvider.MountLoadables(ILoadCollector collector)
        {
            collector.AddLoadable(new YooAssetInitTask());
        }

        #endregion

        #region IAssetService — UniTask

        public async UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            var asset = await _serviceImpl.LoadAsync<T>(location, cancellationToken);
            if (asset != null)
            {
                _assetToLocation[asset] = location;
            }
            return asset;
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Transform parent = null)
        {
            return await InstantiateAsyncInternal(location, null, null, parent);
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            return await InstantiateAsyncInternal(location, position, rotation, parent);
        }

        public async UniTask<T> InstantiateAsync<T>(string location, Transform parent = null) where T : Component
        {
            var go = await InstantiateAsyncInternal(location, null, null, parent);
            return go != null ? go.GetComponent<T>() : null;
        }

        public async UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
        {
            var go = await InstantiateAsyncInternal(location, position, rotation, parent);
            return go != null ? go.GetComponent<T>() : null;
        }

        #endregion

        #region IAssetService — Callback

        public async void LoadAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null) where T : UnityEngine.Object
        {
            var result = await LoadAsync<T>(location);
            if (result != null)
                onCompleted?.Invoke(result);
            else
                onError?.Invoke($"Failed to load asset: {location}");
        }

        public async void InstantiateAsync(string location, Action<GameObject> onCompleted, Action<string> onError = null, Transform parent = null)
        {
            var result = await InstantiateAsync(location, parent);
            if (result != null)
                onCompleted?.Invoke(result);
            else
                onError?.Invoke($"Failed to instantiate: {location}");
        }

        public async void InstantiateAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null, Transform parent = null) where T : Component
        {
            var result = await InstantiateAsync<T>(location, parent);
            if (result != null)
                onCompleted?.Invoke(result);
            else
                onError?.Invoke($"Failed to instantiate: {location}");
        }

        #endregion

        #region IAssetService — Lifecycle

        public void Release(UnityEngine.Object asset)
        {
            if (asset == null) return;

            if (_assetToLocation.TryGetValue(asset, out var location))
            {
                _serviceImpl.Release(location);
                _assetToLocation.Remove(asset);
            }
        }

        public void DestroyInstance(GameObject instance)
        {
            if (instance == null) return;

            if (_instanceToLocation.TryGetValue(instance, out var location))
            {
                _instanceToLocation.Remove(instance);

                // 引用计数 -1
                if (_locationCounts.TryGetValue(location, out var count))
                {
                    count--;
                    if (count <= 0)
                    {
                        _locationCounts.Remove(location);
                        _serviceImpl.Release(location);
                    }
                    else
                    {
                        _locationCounts[location] = count;
                    }
                }

                // 回池或销毁
                ReturnToPoolOrDestroy(location, instance);
            }
            else
            {
                // 非托管实例，直接销毁
                UnityEngine.Object.Destroy(instance);
            }
        }

        public void DestroyInstance<T>(T component) where T : Component
        {
            if (component == null) return;
            DestroyInstance(component.gameObject);
        }

        public void Destroy()
        {
            OnDestroy();
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// 内部实例化逻辑。优先从对象池获取，否则加载资源并实例化。
        /// </summary>
        private async UniTask<GameObject> InstantiateAsyncInternal(string location, Vector3? position, Quaternion? rotation, Transform parent)
        {
            // 1. 优先从对象池获取
            if (_pools.TryGetValue(location, out var pool) && pool.Count > 0)
            {
                var pooled = pool.Pop();
                if (pooled != null)
                {
                    // 重置变换
                    pooled.transform.SetParent(parent);
                    if (position.HasValue) pooled.transform.position = position.Value;
                    if (rotation.HasValue) pooled.transform.rotation = rotation.Value;
                    pooled.SetActive(true);

                    // 记录映射
                    _instanceToLocation[pooled] = location;
                    _locationCounts[location] = _locationCounts.TryGetValue(location, out var c) ? c + 1 : 1;

                    return pooled;
                }
            }

            // 2. 加载资源
            var prefab = await _serviceImpl.LoadAsync<GameObject>(location);
            if (prefab == null) return null;

            // 记录资源映射（首次加载时）
            _assetToLocation[prefab] = location;

            // 3. 实例化
            GameObject go;
            if (position.HasValue && rotation.HasValue)
            {
                go = UnityEngine.Object.Instantiate(prefab, position.Value, rotation.Value, parent);
            }
            else
            {
                go = UnityEngine.Object.Instantiate(prefab, parent);
            }

            // 4. 记录实例映射
            _instanceToLocation[go] = location;
            _locationCounts[location] = _locationCounts.TryGetValue(location, out var cnt) ? cnt + 1 : 1;

            return go;
        }

        /// <summary>
        /// 将实例回池或销毁。池满时销毁最旧的实例。
        /// </summary>
        private void ReturnToPoolOrDestroy(string location, GameObject instance)
        {
            instance.SetActive(false);
            instance.transform.SetParent(null);

            if (!_pools.TryGetValue(location, out var pool))
            {
                pool = new Stack<GameObject>();
                _pools[location] = pool;
            }

            if (pool.Count < MaxPoolSize)
            {
                pool.Push(instance);
            }
            else
            {
                UnityEngine.Object.Destroy(instance);
            }
        }

        #endregion
    }
}
