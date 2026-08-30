using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using YooAsset;
using XFramework.XPipeline;

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
        /// 卸载指定包中所有未使用的资源（引用计数为 0 且未被引用的 bundle）。
        /// <para>包不存在时 LogError 并安全返回。</para>
        /// </summary>
        public async UniTask UnloadUnusedAssetsAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            var package = GetPackage(packageName);
            if (package == null) return;

            var operation = package.UnloadUnusedAssetsAsync();
            await operation.WithCancellation(cancellationToken);
        }

        /// <summary>
        /// 卸载所有已注册包中未使用的资源（低内存回收用，等价于对每个包调用 <see cref="UnloadUnusedAssetsAsync"/>）。
        /// <para>快照键名遍历，避免 await 间隙 GetOrCreatePackage 修改字典导致枚举异常。</para>
        /// </summary>
        public async UniTask UnloadUnusedAssetsAllAsync(CancellationToken cancellationToken = default)
        {
            var names = new List<string>(_packages.Keys);
            foreach (var name in names)
            {
                if (_packages.TryGetValue(name, out var package))
                {
                    var operation = package.UnloadUnusedAssetsAsync();
                    await operation.WithCancellation(cancellationToken);
                }
            }
        }

        /// <summary>
        /// 尝试立即卸载单个未使用的资源。该资源仍被引用（引用计数大于 0）时无效果。
        /// </summary>
        public void TryUnloadUnusedAsset(string location, string packageName = null)
        {
            var package = GetPackage(packageName);
            if (package == null) return;
            package.TryUnloadUnusedAsset(location);
        }

        /// <summary>
        /// 检查资源定位路径在指定包中是否存在且合法。包不存在时返回 false。
        /// </summary>
        public bool CheckLocationValid(string location, string packageName = null)
        {
            var package = GetPackage(packageName);
            return package != null && package.CheckLocationValid(location);
        }

        /// <summary>
        /// 检查资源是否来自远端（加载前需先下载）。包不存在时返回 false。
        /// </summary>
        public bool IsNeedDownloadFromRemote(string location, string packageName = null)
        {
            var package = GetPackage(packageName);
            return package != null && package.IsNeedDownloadFromRemote(location);
        }

        /// <summary>
        /// 请求指定包的最新资源版本号（Host 模式）。失败抛 InvalidOperationException。
        /// </summary>
        public async UniTask<string> RequestPackageVersionAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            var package = GetPackage(packageName);
            if (package == null) return null;

            var operation = package.RequestPackageVersionAsync();
            await operation.WithCancellation(cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"[AssetManager] Version request failed: {operation.Error}");

            return operation.PackageVersion;
        }

        /// <summary>
        /// 将指定包的活动清单更新到指定版本（激活新版本资源）。失败抛 InvalidOperationException。
        /// </summary>
        public async UniTask UpdatePackageManifestAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(packageVersion))
                throw new ArgumentException("[AssetManager] packageVersion is null or empty.", nameof(packageVersion));

            var package = GetPackage(packageName);
            if (package == null) return;

            var operation = package.UpdatePackageManifestAsync(packageVersion);
            await operation.WithCancellation(cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"[AssetManager] Manifest update failed: {operation.Error}");
        }

        /// <summary>
        /// 预检指定版本的清单（不激活）。失败抛 InvalidOperationException。
        /// </summary>
        public async UniTask PreDownloadContentAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(packageVersion))
                throw new ArgumentException("[AssetManager] packageVersion is null or empty.", nameof(packageVersion));

            var package = GetPackage(packageName);
            if (package == null) return;

            var operation = package.PreDownloadContentAsync(packageVersion);
            await operation.WithCancellation(cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"[AssetManager] Pre-download check failed: {operation.Error}");
        }

        /// <summary>
        /// 获取指定包当前激活的资源版本号。包不存在时返回 null。
        /// </summary>
        public string GetPackageVersion(string packageName = null)
        {
            var package = GetPackage(packageName);
            return package?.GetPackageVersion();
        }

        /// <summary>
        /// 创建资源下载器（基于当前激活清单）。tags 为 null/空时下载全部待更新资源。
        /// </summary>
        public AssetDownloaderHandle CreateDownloader(string[] tags = null, int downloadingMaxNumber = 8, int failedRetryCount = 3, string packageName = null)
        {
            var package = GetPackage(packageName);
            if (package == null) return null;

            var operation = tags == null || tags.Length == 0
                ? package.CreateResourceDownloader(downloadingMaxNumber, failedRetryCount)
                : package.CreateResourceDownloader(tags, downloadingMaxNumber, failedRetryCount);
            return new AssetDownloaderHandle(operation);
        }

        /// <summary>
        /// 一键下载：创建下载器、自动启动、聚合进度，返回是否全部成功。
        /// </summary>
        public async UniTask<bool> DownloadAssetsAsync(string[] tags = null, Action<float> progress = null, string packageName = null, CancellationToken cancellationToken = default)
        {
            var handle = CreateDownloader(tags, packageName: packageName);
            if (handle == null) return false;

            handle.ProgressChanged += progress;
            handle.Begin();

            bool success = await handle.WaitAsync(cancellationToken);
            // 下载器结束时可能不触发最终进度回调（如无待下载内容），补发一次最终值
            progress?.Invoke(success ? 1f : handle.Progress);
            handle.Dispose();
            return success;
        }

        /// <summary>
        /// 同步加载资源（阻塞至完成）。失败时 LogError 并返回 default 句柄。
        /// </summary>
        public AssetHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object
        {
            var package = GetOrCreatePackage();
            if (package == null) return default;

            var operation = package.LoadAssetSync<T>(location);
            if (operation.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetManager] Failed to load asset '{location}': {operation.LastError}");
                return default;
            }
            return new AssetHandle<T>(operation);
        }

        /// <summary>
        /// 异步加载子资源集合（图集、多 Sprite 贴图等）。
        /// </summary>
        public async UniTask<SubAssetsHandle> LoadSubAssetsAsync(string location, CancellationToken cancellationToken = default)
        {
            var package = GetOrCreatePackage();
            if (package == null) return default;

            var operation = package.LoadSubAssetsAsync(location);
            while (!operation.IsDone)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            if (operation.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetManager] Failed to load sub assets '{location}': {operation.LastError}");
                return default;
            }
            return new SubAssetsHandle(operation);
        }

        /// <summary>
        /// 同步加载子资源集合（阻塞至完成）。失败时 LogError 并返回 default 句柄。
        /// </summary>
        public SubAssetsHandle LoadSubAssetsSync(string location)
        {
            var package = GetOrCreatePackage();
            if (package == null) return default;

            var operation = package.LoadSubAssetsSync(location);
            if (operation.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetManager] Failed to load sub assets '{location}': {operation.LastError}");
                return default;
            }
            return new SubAssetsHandle(operation);
        }

        /// <summary>
        /// 异步加载原始文件（txt/json/二进制，不经过 Unity 资源管线）。
        /// </summary>
        public async UniTask<RawFileHandle> LoadRawFileAsync(string location, CancellationToken cancellationToken = default)
        {
            var package = GetOrCreatePackage();
            if (package == null) return default;

            var operation = package.LoadRawFileAsync(location);
            while (!operation.IsDone)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            if (operation.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetManager] Failed to load raw file '{location}': {operation.LastError}");
                return default;
            }
            return new RawFileHandle(operation);
        }

        /// <summary>
        /// 同步加载原始文件（阻塞至完成）。失败时 LogError 并返回 default 句柄。
        /// </summary>
        public RawFileHandle LoadRawFileSync(string location)
        {
            var package = GetOrCreatePackage();
            if (package == null) return default;

            var operation = package.LoadRawFileSync(location);
            if (operation.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetManager] Failed to load raw file '{location}': {operation.LastError}");
                return default;
            }
            return new RawFileHandle(operation);
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
