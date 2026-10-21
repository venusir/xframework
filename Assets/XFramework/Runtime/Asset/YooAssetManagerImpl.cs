using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using Cysharp.Threading.Tasks;
using YooAsset;
using XFramework.XLog;

namespace XFramework.XAsset
{
    /// <summary>
    /// 基于 YooAsset 的资源服务底层实现。
    /// <para>内部类，不对外暴露。外部通过 <see cref="IAssetManager"/> 接口或 <see cref="AssetManager"/> 访问。</para>
    /// <para>职责：资源加载、场景加载、预加载、多包管理与热更链路。</para>
    /// <para>生命周期由外部 <see cref="AssetHandle{T}"/> 管理，每次 LoadAsync 返回独立句柄，
    /// 用户 Dispose 句柄时直接调用 <see cref="YooAsset.HandleBase.Release"/>。</para>
    /// </summary>
    class YooAssetManagerImpl
    {
        /// <summary>
        /// 资源加载入口的 Profiler 标记。只包住同步的 operation 创建段——真正的等待跨 <c>await</c>，
        /// 把标记跨挂起保持会把 scope 绑到错误的线程上。
        /// </summary>
        private static readonly ProfilerMarker LoadMarker = new("XFramework.Asset.Load");

        /// <summary>场景加载入口的 Profiler 标记（同样只覆盖同步的 operation 创建段）。</summary>
        private static readonly ProfilerMarker SceneLoadMarker = new("XFramework.Asset.LoadScene");

        private readonly string _defaultPackageName;
        private readonly Dictionary<string, ResourcePackage> _packages = new Dictionary<string, ResourcePackage>(2);
        private readonly ISceneLoadSource _sceneLoadSource;

        /// <summary>已成功加载、仍由本适配器持有的场景：句柄 <c>Id</c> → 加载操作与场景快照。</summary>
        private readonly Dictionary<int, SceneRegistration> _sceneOperations = new Dictionary<int, SceneRegistration>();

        /// <summary>
        /// 登记项：加载操作 + **加载时捕获的 <see cref="Scene"/> 快照**。
        /// <para>不能只靠 <c>Operation.SceneObject</c> 比对：真适配器下 YooAsset 会在自己的
        /// <c>sceneUnloaded</c> 订阅里先 <c>Release</c>，等本适配器的回调跑到时 <c>SceneObject</c> 已是
        /// default——比较恒 false，登记项永远清不掉（R3-SCN-001）。快照不随句柄释放而变。</para>
        /// </summary>
        private readonly struct SceneRegistration
        {
            public readonly ISceneLoadOperation Operation;
            public readonly Scene Scene;

            public SceneRegistration(ISceneLoadOperation operation, Scene scene)
            {
                Operation = operation;
                Scene = scene;
            }
        }

        /// <summary>场景句柄的发号器。从 1 起（<c>0</c> 保留给 <c>default</c>），只增不减。</summary>
        private int _nextSceneId;

        /// <summary>
        /// 构造适配器并固定「主包名」。
        /// <para><b>刻意不给默认参数</b>：默认值只该有一处真相（<c>AssetManagerImpl</c> 的解析，
        /// 它会参考 <see cref="AssetInitOptions.PackageName"/>）；这里再写一份"DefaultPackage"
        /// 只会在两个调用点都显式传参的情况下成为永不生效的第二份字面量。</para>
        /// </summary>
        /// <param name="defaultPackageName">主包名；由调用方解析好后传入。</param>
        /// <param name="sceneLoadSource">
        /// 场景加载来源；省略即生产路径（默认的 YooAsset 适配器）。
        /// <para><b>它为什么反而有默认值</b>：它不是「第二份真相」，而是<b>测试缝</b>——
        /// 只有用例会传参（见 <see cref="ISceneLoadSource"/>）。包名那个参数没有默认值，
        /// 与这个参数有默认值，两条并不矛盾。</para>
        /// </param>
        public YooAssetManagerImpl(string defaultPackageName, ISceneLoadSource sceneLoadSource = null)
        {
            _defaultPackageName = defaultPackageName;
            _sceneLoadSource = sceneLoadSource ?? new YooAssetSceneLoadSource(() => GetOrCreatePackage());
            EnsureSceneUnloadedSubscribed();
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
                LogManager.Error(LogCategories.YooAssetManager,
                    "Package '{0}' not found. Call AssetManager.InitializeAsync() or AssetManager.InitializePackageAsync() first.",
                    name);
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
        public async UniTask InitializePackageAsync(AssetInitOptions options, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.PlayMode != AssetPlayMode.Offline
                && options.PlayMode != AssetPlayMode.Host
                && options.PlayMode != AssetPlayMode.EditorSimulate)
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

                var initParameters = CreatePlayModeParameters(options, packageName);
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

            // 这里刻意没有状态检查：实测 YooAsset 的 UnloadUnusedAssetsOperation **没有失败路径**
            // （只置 Status = Succeed，从不设 Error）。加一个恒真的检查只会让人以为「失败会被报告」。
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
                    // 无状态检查的理由同 UnloadUnusedAssetsAsync：该操作在 YooAsset 侧没有失败路径
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
        /// 获取指定包当前激活的资源版本号。包不存在**或尚未初始化成功**时返回 null。
        /// <para>未初始化的判定不能省：YooAsset 的 <c>ResourcePackage.GetPackageVersion()</c> 会先做
        /// 初始化检查并<b>抛异常</b>，而本方法的对外契约是「包不存在时返回 null」——
        /// 包存在但没初始化成功（如初始化失败后残留）会从这条缝里漏出去，把 YooAsset 的异常抛给调用方。</para>
        /// </summary>
        public string GetPackageVersion(string packageName = null)
        {
            var package = GetPackage(packageName);
            if (package == null || package.InitializeStatus != EOperationStatus.Succeed)
                return null;

            return package.GetPackageVersion();
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
            try
            {
                handle.Begin();

                bool success = await handle.WaitAsync(cancellationToken);
                // 下载器结束时可能不触发最终进度回调（如无待下载内容），补发一次最终值
                progress?.Invoke(success ? 1f : handle.Progress);
                return success;
            }
            finally
            {
                // 退订必须在 finally：取消时 WaitAsync 抛出，原先写在 await 之后的 Dispose 会被整条跳过，
                // 订阅与回调永久留在下载器上
                handle.Dispose();
            }
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
                LogManager.Error(LogCategories.YooAssetManager, "Failed to load asset '{0}': {1}", location, operation.LastError);
                ReleaseFailedHandle(operation);
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
            await WaitOrReleaseAsync(new HandleBaseWait(operation), cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
            {
                LogManager.Error(LogCategories.YooAssetManager, "Failed to load sub assets '{0}': {1}", location, operation.LastError);
                ReleaseFailedHandle(operation);
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
                LogManager.Error(LogCategories.YooAssetManager, "Failed to load sub assets '{0}': {1}", location, operation.LastError);
                ReleaseFailedHandle(operation);
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
            await WaitOrReleaseAsync(new HandleBaseWait(operation), cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
            {
                LogManager.Error(LogCategories.YooAssetManager, "Failed to load raw file '{0}': {1}", location, operation.LastError);
                ReleaseFailedHandle(operation);
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
                LogManager.Error(LogCategories.YooAssetManager, "Failed to load raw file '{0}': {1}", location, operation.LastError);
                ReleaseFailedHandle(operation);
                return default;
            }
            return new RawFileHandle(operation);
        }

        /// <summary>
        /// 按选项映射 YooAsset 初始化参数。Offline 用内置包；Host 用内置 + 缓存（远端）双文件系统；
        /// EditorSimulate 用编辑器文件系统（模拟构建产物）。
        /// <para><b>内置文件系统参数必须显式给出</b>：YooAsset 的 <c>InitializationOperation</c> 在参数列表为空时
        /// 直接判失败（<c>The file system parameters is empty !</c>）——留空不是「用默认」，是启动不了。</para>
        /// <para>解密服务在 Offline / Host 下接线（加密项目的单机 / 热更模式同样要能读）；
        /// EditorSimulate 直接读取原始资源，不使用解密服务。</para>
        /// </summary>
        /// <param name="options">初始化配置。</param>
        /// <param name="packageName">已解析的包名（<see cref="AssetInitOptions.PackageName"/> 的解析结果）。</param>
        internal static InitializeParameters CreatePlayModeParameters(AssetInitOptions options, string packageName)
        {
            if (options.PlayMode == AssetPlayMode.EditorSimulate)
            {
#if UNITY_EDITOR
                var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                if (buildResult == null)
                {
                    throw new InvalidOperationException(
                        $"[AssetManager] Editor simulate build for package '{packageName}' returned no result. " +
                        "Check that the YooAsset Editor assembly is installed and the package exists in YooAsset build settings.");
                }

                return CreateEditorSimulateParameters(buildResult.PackageRootDirectory);
#else
                throw new PlatformNotSupportedException(
                    $"[AssetManager] EditorSimulate play mode (package '{packageName}') is only supported in the Unity Editor. " +
                    "Use Offline or Host for player builds.");
#endif
            }

            var decryption = options.DecryptionServices == null
                ? null
                : new YooAssetDecryptionServicesAdapter(options.DecryptionServices);

            if (options.PlayMode == AssetPlayMode.Host)
            {
                return new HostPlayModeParameters
                {
                    BuildinFileSystemParameters =
                        FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryption),
                    CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(
                        new YooAssetRemoteServicesAdapter(options.RemoteServices), decryption),
                };
            }

            return new OfflinePlayModeParameters
            {
                BuildinFileSystemParameters =
                    FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryption),
            };
        }

        /// <summary>
        /// 构造编辑器模拟模式的初始化参数。<paramref name="packageRoot"/> 由模拟构建产出。
        /// <para>独立成纯函数是为了让 EditMode 用例验证接线而不真的执行一次模拟构建；
        /// 该模式使用虚拟 bundle、直接读取原始资源，因此不接线解密服务。</para>
        /// </summary>
        /// <param name="packageRoot">模拟构建的输出根目录。</param>
        /// <exception cref="ArgumentException"><paramref name="packageRoot"/> 为 null / 空白。</exception>
        internal static EditorSimulateModeParameters CreateEditorSimulateParameters(string packageRoot)
        {
            if (string.IsNullOrWhiteSpace(packageRoot))
            {
                throw new ArgumentException(
                    "[AssetManager] Editor simulate package root is null or empty.", nameof(packageRoot));
            }

            return new EditorSimulateModeParameters
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot),
            };
        }

        private static void ReportProgress(IProgress<AssetInitReport> progress, float value, string description)
        {
            if (progress != null)
            {
                progress.Report(new AssetInitReport(value, description));
            }
        }

        /// <summary>
        /// 释放加载失败路径上的句柄。对外仍返回 <c>default</c> 句柄（契约不变），但底层包装句柄必须先释放。
        /// <para><b>为什么必须释放</b>：YooAsset 在<b>创建</b>句柄时就已 <c>RefCount++</c> 并登记
        /// （<c>ProviderOperation.CreateHandle</c>），而 <c>CanDestroyProvider()</c> 要求 <c>RefCount &lt;= 0</c>。
        /// 失败的句柄若直接丢掉，该 provider 及其 bundle 引用将<b>永久驻留</b>——此后
        /// <c>UnloadUnusedAssetsAsync</c> 再也回收不掉它。</para>
        /// <para>对已释放/已销毁的句柄安全：<c>HandleBase.Release()</c> 对无效句柄提前返回。</para>
        /// </summary>
        private static void ReleaseFailedHandle(HandleBase handle)
        {
            handle?.Release();
        }

        /// <summary>
        /// 等待一次资产加载；取消时先释放已创建的 YooAsset 句柄再重抛。
        /// <para><b>为什么必须释放</b>：YooAsset 在 <c>CreateHandle</c> 时即 <c>RefCount++</c>，取消后若把
        /// 异常直接抛给调用方，句柄无人 <c>Release</c> → provider 与 bundle 引用永久驻留，
        /// <c>UnloadUnusedAssetsAsync</c> 再也回收不掉（AST-001）。与场景路径的
        /// <c>SettleCancelledSceneLoad</c> 是同一条理由——此前只有场景族做了取消善后，资产族没有。</para>
        /// </summary>
        /// <param name="operation">加载等待缝；生产用 <see cref="HandleBaseWait"/> 包住 YooAsset 句柄。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <remarks>
        /// 泛型而非接口形参：<see cref="HandleBaseWait"/> 是 struct，按接口形参传递会在每次加载时装箱一次
        /// （R3-AST-007）；泛型实例化对值类型直接调用，不装箱。测试替身是 class，同样可用——不为装箱
        /// 加 <c>struct</c> 约束。
        /// </remarks>
        internal static async UniTask WaitOrReleaseAsync<TWait>(TWait operation, CancellationToken cancellationToken)
            where TWait : IAssetLoadWait
        {
            try
            {
                while (!operation.IsDone)
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                operation.Release();
                throw;
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

            AssetHandle operation;
            using (LoadMarker.Auto())
            {
                operation = package.LoadAssetAsync(location, priority);
            }
            await WaitOrReleaseAsync(new HandleBaseWait(operation), cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
            {
                LogManager.Error(LogCategories.YooAssetManager, "Failed to load asset '{0}': {1}", location, operation.LastError);
                ReleaseFailedHandle(operation);
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
        /// <para><b>取消不等于中止</b>：YooAsset 的场景加载没有对外可用的中止入口（<c>ProviderOperation</c>
        /// 的 <c>AbortOperation</c> 是 internal），取消只让本方法提前抛出——底层照跑完、场景最终仍会进来。
        /// 本方法因此把善后接力到后台（见 <see cref="SettleCancelledSceneLoad"/>），调用方不必自己收拾。</para>
        /// </summary>
        public async UniTask<AssetSceneHandle> LoadSceneAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            // Destroy 之后复用同一实例时重新挂上 sceneUnloaded（AST-003 / SCN-003）
            EnsureSceneUnloadedSubscribed();

            ISceneLoadOperation operation;
            using (SceneLoadMarker.Auto())
            {
                operation = _sceneLoadSource.LoadScene(location, mode);
            }
            if (operation == null) return default;

            try
            {
                while (!operation.IsDone)
                {
                    InvokeSceneProgress(progress, operation.Progress, location);
                    // 取消时抛出 OperationCanceledException（善后见 catch）
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                SettleCancelledSceneLoad(operation, mode, location);
                throw;
            }

            if (!operation.IsSucceed)
            {
                LogManager.Error(LogCategories.YooAssetManager, "Failed to load scene '{0}': {1}", location, operation.LastError);
                // 失败路径的句柄必须显式释放：失败时没有场景，YooAsset 挂在 sceneUnloaded 上的兜底
                // 永不触发，该 provider 会永久留在 SceneHandles 里——此后 UnloadUnusedAssetsAsync
                // 再也回收不掉它（与资源加载失败路径的 ReleaseFailedHandle 同一条理由）
                operation.Release();
                return default;
            }

            // 1f 只在成功路径补发：进度面与结果面必须一致——失败时先报 100% 再返回无效 Scene
            // 会让调用方的进度条停在满格，而它拿到的场景根本不存在（AST-007）。
            // 取操作自己的场景对象，而不是按名字回查：SceneManager.GetSceneByName 在「同名场景已存在」
            // 或「场景尚未进入查询表」时会给错（或给不出），而操作自己持有的才是权威来源
            var scene = operation.SceneObject;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                LogManager.Error(LogCategories.YooAssetManager,
                    "Scene '{0}' reported success but its scene object is not valid/loaded; releasing the handle.", location);
                operation.Release();
                return default;
            }

            InvokeSceneProgress(progress, 1f, location);

            // 登记后才交得出手柄：卸载入口就是靠这张表把 Id 找回加载操作的
            int id = ++_nextSceneId;
            _sceneOperations[id] = new SceneRegistration(operation, scene);
            return new AssetSceneHandle(id, scene);
        }

        /// <summary>
        /// 调用场景加载进度回调，并隔离订阅者异常（R3-AST-005）。
        /// <para>进度回调是使用方代码；抛出非 OCE 异常会从 <see cref="LoadSceneAsync"/> 逃出，
        /// 此时操作既未释放也未登记——变成永久泄漏的 provider。这里记 Error 后继续。</para>
        /// </summary>
        private static void InvokeSceneProgress(Action<float> progress, float value, string location)
        {
            if (progress == null) return;

            try
            {
                progress(value);
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.YooAssetManager, e,
                    $"场景加载进度回调抛出异常（location '{location}'），已忽略并继续。");
            }
        }

        /// <summary>
        /// 卸载句柄对应的场景。
        /// <para>四种情况返回 <c>false</c> 而不抛：句柄无效、找不到登记项、<b>句柄与登记项不是同一次加载</b>
        /// （句柄可能来自上一任适配器，而 <c>Id</c> 各自从 1 开始分配——只比 <c>Id</c> 会把「卸载 A」变成
        /// 「卸载 B」）、以及它是当前唯一的场景（卸载会让工程没有任何场景，引擎与 YooAsset 都会失败，
        /// 这里提前挡掉并给一条能看懂的告警）。</para>
        /// </summary>
        public async UniTask<bool> UnloadSceneAsync(AssetSceneHandle handle, CancellationToken cancellationToken = default)
        {
            if (handle.IsDefault) return false;
            if (!_sceneOperations.TryGetValue(handle.Id, out var registration)) return false;
            if (registration.Scene != handle.Scene) return false;

            // 场景已被别人卸掉（例如 Single 模式切场景时 Unity 卸的旧场景）：登记项已失效，顺手清掉
            if (!handle.Scene.IsValid() || !handle.Scene.isLoaded)
            {
                _sceneOperations.Remove(handle.Id);
                registration.Operation.Release();
                return false;
            }

            if (SceneManager.sceneCount <= 1)
            {
                LogManager.Warning(LogCategories.YooAssetManager,
                    "拒绝卸载场景 '{0}'：它是当前唯一的场景。", handle.Scene.name);
                return false;
            }

            // 拿到终态前不丢登记项与句柄（R3-AST-003）：失败时适配器仍持有句柄，调用方可重试；
            // 只有卸载成功才移除登记项。
            bool unloaded = await registration.Operation.UnloadAsync(cancellationToken);
            if (unloaded) _sceneOperations.Remove(handle.Id);
            return unloaded;
        }

        /// <summary>
        /// 取消后的善后入口（fire-and-forget：调用方立刻拿到取消异常，不被加载时长拖住）。
        /// </summary>
        private void SettleCancelledSceneLoad(ISceneLoadOperation operation, LoadSceneMode mode, string location)
            => SettleCancelledSceneLoadAsync(operation, mode, location).Forget();

        /// <summary>
        /// 取消后的善后：等那个「其实中止不了」的加载跑完，再按模式收干净。
        /// <para><b>Additive</b>：把它卸掉，世界回到调用前的样子。</para>
        /// <para><b>Single</b>：<b>什么都不做，只告警</b>。卸载会把工程退化成「一个场景都没有」，
        /// 比留着更糟；而释放会让引用计数归零，此后一次 <c>UnloadUnusedAssetsAsync</c> 就能回收掉
        /// 这个<b>仍在场</b>的场景所引用的 bundle。句柄留给 YooAsset 自己的账本：该场景将来被卸载时，
        /// <c>sceneUnloaded</c> 会自动释放它。</para>
        /// <para>从接力那一刻起不再响应取消——它唯一的目的是把事情收干净。</para>
        /// </summary>
        private async UniTaskVoid SettleCancelledSceneLoadAsync(ISceneLoadOperation operation, LoadSceneMode mode, string location)
        {
            try
            {
                while (!operation.IsDone)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                if (!operation.IsSucceed)
                {
                    operation.Release();
                    return;
                }

                if (mode == LoadSceneMode.Additive)
                {
                    if (!await operation.UnloadAsync(CancellationToken.None))
                    {
                        LogManager.Warning(LogCategories.YooAssetManager,
                            "A cancelled additive load of scene '{0}' completed but could not be undone; the scene stays loaded.", location);
                    }
                }
                else
                {
                    LogManager.Warning(LogCategories.YooAssetManager,
                        "A cancelled Single-mode load of scene '{0}' completed anyway (YooAsset scene loads cannot be aborted); the scene stays loaded and later unloads normally.", location);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error(LogCategories.YooAssetManager,
                    "Failed to settle a cancelled scene load of '{0}': {1}", location, ex.Message);
            }
        }

        /// <summary>当前仍在册的场景加载操作数（诊断/测试用）。</summary>
        internal int SceneOperationCount => _sceneOperations.Count;

        /// <summary>
        /// 场景被卸载（外部卸载或 Single 模式切场景）时清掉对应登记项。
        /// <para>原实现只在 <see cref="UnloadSceneAsync"/> 里按句柄清；Single 模式切场景时 Unity 自己卸载旧场景，
        /// 登记项无人清理，会话内线性增长（AST-003 / SCN-003）；真适配器下「外部卸载后清登记项」分支
        /// 也因 YooAsset 同步 Release 后 <c>SceneObject</c> 归 default 而不可达。订阅 <c>sceneUnloaded</c>
        /// 是唯一可靠的回收点；YooAsset 侧已自行释放，这里只清表、不再 Release。</para>
        /// </summary>
        private void OnSceneUnloaded(Scene scene)
        {
            if (_sceneOperations.Count == 0)
                return;

            List<int> stale = null;
            foreach (var pair in _sceneOperations)
            {
                if (pair.Value.Scene == scene)
                    (stale ?? (stale = new List<int>())).Add(pair.Key);
            }

            if (stale == null)
                return;

            for (int i = 0; i < stale.Count; i++)
                _sceneOperations.Remove(stale[i]);
        }

        private void EnsureSceneUnloadedSubscribed()
        {
            if (_sceneUnloadedSubscribed)
                return;

            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _sceneUnloadedSubscribed = true;
        }

        /// <summary>
        /// 退订 <c>SceneManager.sceneUnloaded</c>（幂等，不 Dispose）。会话复位用（R3-AST-002）：
        /// 旧实例如被静态事件钉住，关闭域重载后会跨会话存活并继续回调。
        /// </summary>
        internal void DetachSceneUnloadedHandler()
        {
            if (!_sceneUnloadedSubscribed)
                return;

            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _sceneUnloadedSubscribed = false;
        }

        /// <summary>当前是否订阅着 <c>SceneManager.sceneUnloaded</c>（测试缝 / 诊断）。</summary>
        internal bool IsSceneUnloadedHandlerAttached => _sceneUnloadedSubscribed;

        /// <summary>是否已订阅 <c>SceneManager.sceneUnloaded</c>（Destroy 后复用同一实例时要重新挂上）。</summary>
        private bool _sceneUnloadedSubscribed;

        /// <summary>
        /// 销毁服务。清空包引用（不从 YooAssets 注册表移除，允许后续重新初始化复用）。
        /// </summary>
        public void Destroy()
        {
            DetachSceneUnloadedHandler();

            _packages.Clear();
            // 只丢引用，不 Release：与「Destroy 不卸载 YooAsset 侧已加载资源」同一口径
            _sceneOperations.Clear();
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

        /// <summary>
        /// 框架 <see cref="IAssetDecryptionServices"/> → YooAsset <see cref="YooAsset.IDecryptionServices"/> 适配器。
        /// <para>纯转发：参数由 YooAsset 的 <c>DecryptFileInfo</c> 展开，返回值装回 <c>DecryptResult</c>——
        /// 两边字段一一对应，无信息损失。</para>
        /// </summary>
        internal sealed class YooAssetDecryptionServicesAdapter : IDecryptionServices
        {
            private readonly IAssetDecryptionServices _inner;

            internal YooAssetDecryptionServicesAdapter(IAssetDecryptionServices inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public DecryptResult LoadAssetBundle(DecryptFileInfo fileInfo)
                => ToYooAsset(_inner.LoadAssetBundle(fileInfo.BundleName, fileInfo.FileLoadPath, fileInfo.FileLoadCRC));

            public DecryptResult LoadAssetBundleAsync(DecryptFileInfo fileInfo)
                => ToYooAsset(_inner.LoadAssetBundleAsync(fileInfo.BundleName, fileInfo.FileLoadPath, fileInfo.FileLoadCRC));

            public DecryptResult LoadAssetBundleFallback(DecryptFileInfo fileInfo)
                => ToYooAsset(_inner.LoadAssetBundleFallback(fileInfo.BundleName, fileInfo.FileLoadPath, fileInfo.FileLoadCRC));

            public byte[] ReadFileData(DecryptFileInfo fileInfo)
                => _inner.ReadFileData(fileInfo.BundleName, fileInfo.FileLoadPath, fileInfo.FileLoadCRC);

            public string ReadFileText(DecryptFileInfo fileInfo)
                => _inner.ReadFileText(fileInfo.BundleName, fileInfo.FileLoadPath, fileInfo.FileLoadCRC);

            private static DecryptResult ToYooAsset(AssetDecryptResult result)
                => new DecryptResult
                {
                    Result = result.Bundle,
                    CreateRequest = result.CreateRequest,
                    ManagedStream = result.ManagedStream,
                };
        }
    }
}
