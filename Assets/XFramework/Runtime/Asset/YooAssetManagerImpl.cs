using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using YooAsset;
using XFramework.XLoader;

namespace XFramework.XAsset
{
    /// <summary>
    /// 基于 YooAsset 的资源服务底层实现。
    /// <para>内部类，不对外暴露。外部通过 <see cref="IAssetManager"/> 接口或 <see cref="AssetManager"/> 访问。</para>
    /// <para>职责：资源加载、场景加载、预加载、多包管理与热更链路。</para>
    /// <para>生命周期由外部 <see cref="AssetHandle{T}"/> 管理，每次 LoadAsync 返回独立句柄，
    /// 用户 Dispose 句柄时直接调用 <see cref="YooAsset.AssetHandle.Release"/>。</para>
    /// </summary>
    class YooAssetManagerImpl
    {
        private readonly string _defaultPackageName;
        private readonly Dictionary<string, ResourcePackage> _packages = new Dictionary<string, ResourcePackage>(2);

        public YooAssetManagerImpl(string defaultPackageName = "DefaultPackage")
        {
            _defaultPackageName = defaultPackageName;
        }

        /// <summary>
        /// 解析包名：null/空白 → 默认包名。
        /// </summary>
        private string ResolvePackageName(string packageName)
            => string.IsNullOrWhiteSpace(packageName) ? _defaultPackageName : packageName;

        /// <summary>
        /// 获取资源包：本地已注册则返回，否则从 YooAssets 获取；均不存在时创建并注册。
        /// </summary>
        private ResourcePackage GetOrCreatePackage(string packageName = null)
        {
            string name = ResolvePackageName(packageName);
            if (_packages.TryGetValue(name, out var package)) return package;

            package = YooAssets.TryGetPackage(name);
            if (package == null)
            {
                package = YooAssets.CreatePackage(name);
            }
            _packages[name] = package;
            return package;
        }

        /// <summary>
        /// 获取资源包：只查不建。不存在时 LogError 并返回 null。
        /// </summary>
        private ResourcePackage GetPackage(string packageName = null)
        {
            string name = ResolvePackageName(packageName);
            if (_packages.TryGetValue(name, out var package)) return package;

            package = YooAssets.TryGetPackage(name);
            if (package == null)
            {
                Debug.LogError($"[YooAssetManager] Package '{name}' not found. " +
                               "Call AssetManager.InitializeAsync() or AssetManager.InitializePackageAsync() first.");
                return null;
            }
            _packages[name] = package;
            return package;
        }

        /// <summary>
        /// 初始化资源包（默认包与额外包共用此链路）。
        /// <para>包复用语义：包已初始化成功时跳过 InitializeAsync，直接刷新版本与清单
        /// （支持 AssetManager.Destroy() 后重新初始化）；初始化失败时重新初始化。</para>
        /// </summary>
        public async UniTask InitializePackageAsync(AssetInitOptions options, LoadProgress progress, CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.PlayMode != AssetPlayMode.Offline && options.PlayMode != AssetPlayMode.Host)
                throw new ArgumentException($"[AssetManager] Unsupported PlayMode: {options.PlayMode}");
            if (options.PlayMode == AssetPlayMode.Host && options.RemoteServices == null)
                throw new ArgumentException("[AssetManager] HostPlayMode requires RemoteServices. Provide AssetInitOptions.RemoteServices.");

            string packageName = ResolvePackageName(options.PackageName);

            // 1. 初始化 YooAsset 全局环境（只需一次）
            if (!YooAssets.Initialized)
            {
                YooAssets.Initialize();
            }

            // 2. 获取或创建包
            var package = GetOrCreatePackage(packageName);

            // 3. 初始化资源包（包复用：已成功则跳过）
            if (package.InitializeStatus != EOperationStatus.Succeed)
            {
                ReportProgress(progress, 0.2f, $"Initializing package '{packageName}'...");

                var initParameters = CreatePlayModeParameters(options);
                var initOperation = package.InitializeAsync(initParameters);
                await initOperation.WithCancellation(cancellationToken);

                if (initOperation.Status != EOperationStatus.Succeed)
                {
                    throw new InvalidOperationException(
                        $"[AssetManager] Package '{packageName}' init failed: {initOperation.Error}. " +
                        "Check init options and resource configuration.");
                }
            }

            // 4. 获取资源版本号
            ReportProgress(progress, 0.6f, "Requesting package version...");
            var versionOperation = package.RequestPackageVersionAsync();
            await versionOperation.WithCancellation(cancellationToken);

            if (versionOperation.Status != EOperationStatus.Succeed)
            {
                throw new InvalidOperationException($"[AssetManager] Version request failed: {versionOperation.Error}");
            }

            // 5. 更新资源清单
            ReportProgress(progress, 0.8f, "Updating package manifest...");
            var updateOperation = package.UpdatePackageManifestAsync(versionOperation.PackageVersion);
            await updateOperation.WithCancellation(cancellationToken);

            if (updateOperation.Status != EOperationStatus.Succeed)
            {
                throw new InvalidOperationException($"[AssetManager] Manifest update failed: {updateOperation.Error}");
            }

            ReportProgress(progress, 1f, $"Package '{packageName}' initialized.");
        }

        /// <summary>
        /// 按选项映射 YooAsset 初始化参数。Offline 用内置包；Host 用内置 + 缓存（远端）双文件系统。
        /// </summary>
        private static InitializeParameters CreatePlayModeParameters(AssetInitOptions options)
        {
            if (options.PlayMode == AssetPlayMode.Host)
            {
                return new HostPlayModeParameters
                {
                    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                    CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(
                        new YooAssetRemoteServicesAdapter(options.RemoteServices)),
                };
            }
            return new OfflinePlayModeParameters();
        }

        private static void ReportProgress(LoadProgress progress, float value, string description)
        {
            if (progress != null)
            {
                progress.SetOverallProgress(value);
                progress.SetDescription(description);
            }
        }

        /// <summary>
        /// 异步加载资源。每次调用均从 YooAsset 获取新句柄，返回 <see cref="AssetHandle{T}"/> 包装。
        /// </summary>
        public async UniTask<AssetHandle<T>> LoadAsync<T>(string location, uint priority = 0, CancellationToken cancellationToken = default)
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

            return new AssetHandle<T>(operation);
        }

        /// <summary>
        /// 预加载资源。加载后立即释放句柄，YooAsset 底层保留 bundle 缓存，后续加载秒回。
        /// </summary>
        public async UniTask PreloadAsync(string location, CancellationToken cancellationToken = default)
        {
            var handle = await LoadAsync<UnityEngine.Object>(location, cancellationToken: cancellationToken);
            if (handle.IsValid)
                handle.Dispose();
        }

        /// <summary>
        /// 异步加载场景。
        /// <para>失败时返回无效的 <c>default(Scene)</c>，调用方需用 <see cref="Scene.IsValid"/> 校验。</para>
        /// </summary>
        public async UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            var package = GetOrCreatePackage();
            if (package == null) return default;

            var mode = additive ? LoadSceneMode.Additive : LoadSceneMode.Single;
            var operation = package.LoadSceneAsync(location, mode);

            while (!operation.IsDone)
            {
                progress?.Invoke(operation.Progress);
                // 取消时抛出 OperationCanceledException
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            progress?.Invoke(1f);

            if (operation.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetManager] Failed to load scene '{location}': {operation.LastError}");
                return default;
            }

            var scene = SceneManager.GetSceneByName(operation.SceneName);
            return scene;
        }

        /// <summary>
        /// 销毁服务。清空包引用（不从 YooAssets 注册表移除，允许后续重新初始化复用）。
        /// </summary>
        public void Destroy()
        {
            _packages.Clear();
        }

        /// <summary>
        /// 框架 <see cref="IAssetRemoteServices"/> → YooAsset <see cref="YooAsset.IRemoteServices"/> 适配器。
        /// </summary>
        private sealed class YooAssetRemoteServicesAdapter : IRemoteServices
        {
            private readonly IAssetRemoteServices _inner;

            public YooAssetRemoteServicesAdapter(IAssetRemoteServices inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public string GetRemoteMainURL(string fileName) => _inner.GetRemoteMainURL(fileName);

            public string GetRemoteFallbackURL(string fileName) => _inner.GetRemoteFallbackURL(fileName);
        }
    }
}
