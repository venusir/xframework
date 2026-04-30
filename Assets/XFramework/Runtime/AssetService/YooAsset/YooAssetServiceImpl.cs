using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace XFramework
{
    /// <summary>
    /// 基于 YooAsset 的资源服务底层实现。
    /// <para>内部类，不对外暴露。外部通过 <see cref="IAssetService"/> 接口访问。</para>
    /// <para>职责：资源加载/卸载、引用计数、延迟卸载。</para>
    /// </summary>
    class YooAssetServiceImpl
    {
        private readonly string _packageName;
        private ResourcePackage _package;

        /// <summary>已加载资源的引用计数。</summary>
        private readonly Dictionary<string, int> _refCounts = new Dictionary<string, int>();

        /// <summary>已加载资源的缓存。</summary>
        private readonly Dictionary<string, object> _cache = new Dictionary<string, object>();

        /// <summary>YooAsset 资源句柄缓存，用于释放时通知 YooAsset 卸载底层资源。</summary>
        private readonly Dictionary<string, YooAsset.AssetHandle> _yooHandles = new Dictionary<string, YooAsset.AssetHandle>();

        /// <summary>待释放队列：引用计数归零后记录的时间戳。</summary>
        private readonly Dictionary<string, float> _pendingReleaseTimes = new Dictionary<string, float>();

        /// <summary>资源卸载延迟时间（秒）。</summary>
        private readonly float _unloadDelaySeconds = 5f;

        /// <summary>统一卸载 Tick 协程是否正在运行。</summary>
        private bool _tickRunning;

        public YooAssetServiceImpl(string packageName = "DefaultPackage")
        {
            _packageName = packageName;
        }

        /// <summary>
        /// 获取资源包实例。如果尚未初始化则尝试获取。
        /// </summary>
        private ResourcePackage GetOrCreatePackage()
        {
            if (_package == null)
            {
                _package = YooAssets.TryGetPackage(_packageName);
            }
            return _package;
        }

        /// <summary>
        /// 异步加载资源（引用计数 +1）。
        /// </summary>
        public async UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            var package = GetOrCreatePackage();
            if (package == null)
                return null;

            // 检查是否有待释放记录，有则取消延迟，恢复引用计数
            if (_pendingReleaseTimes.ContainsKey(location))
            {
                _pendingReleaseTimes.Remove(location);
                _refCounts[location] = 1;
                return _cache[location] as T;
            }

            // 检查缓存中是否已有此资源
            if (_cache.TryGetValue(location, out var cachedAsset))
            {
                _refCounts[location]++;
                return cachedAsset as T;
            }

            var operation = package.LoadAssetAsync(location);
            await operation.WithCancellation(cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
                return null;

            _refCounts[location] = 1;
            _cache[location] = operation.AssetObject;
            _yooHandles[location] = operation;

            return operation.AssetObject as T;
        }

        /// <summary>
        /// 释放资源（引用计数 -1）。归零时启动延迟卸载。
        /// </summary>
        public void Release(string location)
        {
            if (!_refCounts.ContainsKey(location))
                return;

            _refCounts[location]--;
            if (_refCounts[location] <= 0)
            {
                _refCounts.Remove(location);
                _pendingReleaseTimes[location] = Time.realtimeSinceStartup;

                if (!_tickRunning)
                {
                    _tickRunning = true;
                    UnloadTickAsync().Forget();
                }
            }
        }

        /// <summary>
        /// 统一卸载 Tick 协程。每帧检查所有待释放记录，到期则真正卸载。
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

        /// <summary>
        /// 销毁服务，强制释放所有资源。
        /// </summary>
        public void Destroy()
        {
            _pendingReleaseTimes.Clear();

            foreach (var kvp in _yooHandles)
            {
                kvp.Value.Release();
            }

            _yooHandles.Clear();
            _cache.Clear();
            _refCounts.Clear();
            _package = null;
        }
    }
}
