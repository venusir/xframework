using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace XFramework
{
    /// <summary>
    /// 基于 YooAsset 的资源服务底层实现。
    /// <para>内部类，不对外暴露。外部通过 <see cref="IAssetManager"/> 接口或 <see cref="AssetManager"/> 访问。</para>
    /// <para>职责：资源加载、场景加载、预加载。</para>
    /// <para>生命周期由外部 <see cref="AssetHandle{T}"/> 管理，每次 LoadAsync 返回独立句柄，
    /// 用户 Dispose 句柄时直接调用 <see cref="YooAsset.AssetHandle.Release"/>。</para>
    /// </summary>
    class YooAssetManagerImpl
    {
        private readonly string _packageName;
        private ResourcePackage _package;

        public YooAssetManagerImpl(string packageName = "DefaultPackage")
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
        /// 异步加载资源。每次调用均从 YooAsset 获取新句柄，返回 <see cref="XAsset.AssetHandle{T}"/> 包装。
        /// </summary>
        public async UniTask<XAsset.AssetHandle<T>> LoadAsync<T>(string location, uint priority = 0, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            var package = GetOrCreatePackage();
            if (package == null)
                return default;

            var operation = package.LoadAssetAsync(location, priority);
            await operation.WithCancellation(cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetManager] Failed to load asset '{location}': {operation.LastError}");
                return default;
            }

            return new XAsset.AssetHandle<T>(operation);
        }

        /// <summary>
        /// 预加载资源。加载后立即释放句柄，YooAsset 底层保留 bundle 缓存，后续加载秒回。
        /// </summary>
        public async UniTask PreloadAsync(string location)
        {
            var handle = await LoadAsync<UnityEngine.Object>(location);
            if (handle.IsValid)
                handle.Dispose();
        }

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        public async UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null)
        {
            var package = GetOrCreatePackage();
            if (package == null) return default;

            var mode = additive ? LoadSceneMode.Additive : LoadSceneMode.Single;
            var operation = package.LoadSceneAsync(location, mode);

            while (!operation.IsDone)
            {
                progress?.Invoke(operation.Progress);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            progress?.Invoke(1f);

            if (operation.Status != EOperationStatus.Succeed)
                return default;

            var scene = SceneManager.GetSceneByName(operation.SceneName);
            return scene;
        }

        /// <summary>
        /// 销毁服务。无需清理——所有句柄由外部持有者 Dispose。
        /// </summary>
        public void Destroy()
        {
            _package = null;
        }
    }
}