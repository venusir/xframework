using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
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

        /// <summary>待释放队列：引用计数归零后记录的时间戳（Time.realtimeSinceStartup）。</summary>
        private readonly Dictionary<string, float> _pendingReleaseTimes = new Dictionary<string, float>();

        /// <summary>资源卸载延迟时间（秒）。引用计数归零后等待此时间才真正卸载，避免频繁加载/卸载。</summary>
        private readonly float _unloadDelaySeconds = 5f;

        /// <summary>统一卸载 Tick 协程是否正在运行。</summary>
        private bool _tickRunning;

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

            // 检查是否有待释放记录，有则取消延迟，恢复引用计数
            if (_pendingReleaseTimes.ContainsKey(location))
            {
                _pendingReleaseTimes.Remove(location);

                // 恢复引用计数（之前归零了，现在重新加回 1）
                _refCounts[location] = 1;

                return new AssetHandle(_cache[location], this, location);
            }

            // 检查缓存中是否已有此资源
            if (_cache.TryGetValue(location, out var cachedAsset))
            {
                _refCounts[location]++;
                return new AssetHandle(cachedAsset, this, location);
            }

            var operation = _package.LoadAssetAsync(location);
            await operation.WithCancellation(cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
            {
                return default;
            }

            // 增加引用计数
            _refCounts[location] = 1;

            _cache[location] = operation.AssetObject;
            _yooHandles[location] = operation;

            return new AssetHandle(operation.AssetObject, this, location);
        }

        public async void LoadAssetAsync(string location, Action<AssetHandle> onCompleted, Action<string> onError = null)
        {
            var result = await LoadAssetAsync(location);
            if (result.IsValid)
                onCompleted?.Invoke(result);
            else
                onError?.Invoke($"Failed to load asset: {location}");
        }

        public void Release(AssetHandle handle)
        {
            string location = handle.Location;
            if (string.IsNullOrEmpty(location))
                return;

            ReleaseAsset(location);
        }

        public void Destroy()
        {
            _pendingReleaseTimes.Clear();

            // 强制释放所有缓存的 YooAsset 句柄，卸载底层资源
            foreach (var kvp in _yooHandles)
            {
                kvp.Value.Release();
            }

            _yooHandles.Clear();
            _cache.Clear();
            _refCounts.Clear();
            _package = null;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// 内部释放指定位置的资源。
        /// <para>引用计数归零时记录时间戳，由统一 Tick 协程延迟卸载。</para>
        /// </summary>
        internal void ReleaseAsset(string location)
        {
            if (!_refCounts.ContainsKey(location))
                return;

            _refCounts[location]--;
            if (_refCounts[location] <= 0)
            {
                _refCounts.Remove(location);
                _pendingReleaseTimes[location] = Time.realtimeSinceStartup;

                // 确保 Tick 协程正在运行
                if (!_tickRunning)
                {
                    _tickRunning = true;
                    UnloadTickAsync().Forget();
                }
            }
        }

        /// <summary>
        /// 统一卸载 Tick 协程。每帧检查所有待释放记录，到期则真正卸载。
        /// <para>待释放列表为空时自动结束。</para>
        /// </summary>
        private async UniTaskVoid UnloadTickAsync()
        {
            try
            {
                while (_pendingReleaseTimes.Count > 0)
                {
                    float now = Time.realtimeSinceStartup;
                    List<string> expired = null;

                    foreach (var kvp in _pendingReleaseTimes)
                    {
                        if (now - kvp.Value >= _unloadDelaySeconds)
                        {
                            if (expired == null)
                                expired = new List<string>(4);
                            expired.Add(kvp.Key);
                        }
                    }

                    if (expired != null)
                    {
                        foreach (var location in expired)
                        {
                            _pendingReleaseTimes.Remove(location);
                            _cache.Remove(location);

                            if (_yooHandles.TryGetValue(location, out var yooHandle))
                            {
                                yooHandle.Release();
                                _yooHandles.Remove(location);
                            }
                        }
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            }
            finally
            {
                _tickRunning = false;
            }
        }

        #endregion
    }
}
