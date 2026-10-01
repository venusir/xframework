using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XLog;

namespace XFramework.XAsset
{

    /// <summary>
    /// 全局资源管理器外观。提供静态方法直接访问资源加载、实例化与生命周期管理。
    /// <para>内部持有 <see cref="IAssetManager"/> 实例（<see cref="AssetManagerImpl"/>），所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="InitializeAsync"/> 初始化。</para>
    /// </summary>
    public static class AssetManager
    {
        #region Static — Global Singleton

        private static IAssetManager _instance;
        private static bool _instanceInitialized;

        /// <summary>进行中的初始化任务。并发调用共享同一任务，避免重复创建实例；完成后清空，允许失败重试与 Destroy 后重建。</summary>
        private static UniTask _initializeTask;

        /// <summary>等待初始化的加入者登记表。<b>按代际分区</b>——作废后旧代际的创建者不得放行新代际的加入者（见 <see cref="BroadcastInitResult"/>）。</summary>
        private static readonly List<InitWaiter> _initWaiters = new();

        /// <summary>初始化代际号。Destroy()/SetInstance() 时递增，使在途初始化结果作废（创建者抛异常、同代际加入者一并了结），防止销毁后实例"复活"。</summary>
        private static int _initGeneration;

        /// <summary>作废在途初始化时给创建者与加入者的统一消息（带修复提示）。</summary>
        private const string InitInvalidatedMessage =
            "[AssetManager] 初始化在完成前被 Destroy()/SetInstance() 作废，结果已丢弃。请重新调用 InitializeAsync() 完成初始化。";

        /// <summary>测试钩子：实例工厂。默认创建 <see cref="AssetManagerImpl"/>；测试注入假实现以验证并发共享语义。</summary>
        internal static Func<IAssetManager> ImplFactory;

        /// <summary>池清理能力缺失的告警是否已发出（只发一次，避免调用方在循环里刷屏）。</summary>
        private static bool _poolCapabilityWarned;

        /// <summary>越线程访问是否已报首错（只报一次，避免每帧刷屏）。</summary>
        private static bool _mainThreadViolationLogged;

        /// <summary>
        /// 全局资源管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized => _instanceInitialized && _instance != null;

        /// <summary>
        /// 初始化全局资源管理器（默认包）。
        /// <para>并发调用共享同一进行中的初始化任务；<b>options、progress 与取消令牌一律取首个调用者</b>，
        /// 其余调用者的这三项不参与该任务（不告警）。</para>
        /// <para>初始化在完成前被 <see cref="Destroy"/> / <see cref="SetInstance"/> 作废时，
        /// 创建者与加入者都抛 <see cref="InvalidOperationException"/>——<c>await</c> 正常返回必然等价于「已初始化」。</para>
        /// </summary>
        /// <param name="options">初始化配置。为 null 时使用默认配置（默认包 + 离线模式）。</param>
        /// <param name="progress">初始化进度上报（可空），见 <see cref="AssetInitReport"/>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async UniTask InitializeAsync(AssetInitOptions options = null, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            AssertMainThread(nameof(InitializeAsync));

            if (_instanceInitialized)
            {
                LogManager.Warning(LogCategories.AssetManager, "InitializeAsync was called more than once. Ignoring duplicate.");
                return;
            }

            // 并发调用共享同一进行中的任务；加入者不直接 await 共享任务
            //（UniTask promise 只支持单个 continuation），注册自己的信号等待创建者广播
            if (_initializeTask.Status == UniTaskStatus.Pending)
            {
                await AwaitInitBroadcast();
                return;
            }

            // 代际号在创建者选举处捕获一次：此后 Destroy()/SetInstance() 递增它即作废本次初始化
            int generation = _initGeneration;
            var task = InitializeAsyncCore(generation, progress, options, cancellationToken);
            _initializeTask = task;
            Exception error = null;
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                // 成功/失败/取消后均广播**本代际**的加入者并清空缓存，允许再次初始化（失败重试、Destroy 后重建）；
                // 仅当缓存仍指向本次任务时清除，避免误清并发中新启动的任务
                BroadcastInitResult(generation, error);
                if (_initializeTask.Equals(task))
                {
                    _initializeTask = default;
                }
            }
        }

        /// <summary>
        /// 加入者等待初始化完成广播。注册自己的完成信号而非直接 await 共享任务
        /// （UniTask promise 只支持单个 continuation）；代际号按**登记时刻**记录，
        /// 此后 Destroy()/SetInstance() 作废的是同一批登记项。
        /// </summary>
        private static async UniTask AwaitInitBroadcast()
        {
            var tcs = new UniTaskCompletionSource();
            _initWaiters.Add(new InitWaiter(_initGeneration, tcs));
            await tcs.Task;
        }

        /// <summary>
        /// 广播结果给指定代际的加入者并移除这些登记项（成功 TrySetResult，失败/取消 TrySetException 同一异常实例）。
        /// <para><b>只放行同代际</b>：作废后仍可能有两个创建者并存（旧代际的收尾在新代际启动之后完成），
        /// 若不过滤，旧创建者会把新代际的加入者提前放行——它们醒来时 <see cref="IsInitialized"/> 仍为 false，
        /// 或者收到与本代际无关的异常。</para>
        /// </summary>
        private static void BroadcastInitResult(int generation, Exception error)
        {
            for (int i = 0; i < _initWaiters.Count; i++)
            {
                var waiter = _initWaiters[i];
                if (waiter.Generation != generation)
                    continue;

                if (error != null)
                    waiter.Completion.TrySetException(error);
                else
                    waiter.Completion.TrySetResult();
            }

            for (int i = _initWaiters.Count - 1; i >= 0; i--)
            {
                if (_initWaiters[i].Generation == generation)
                    _initWaiters.RemoveAt(i);
            }
        }

        /// <summary>
        /// 作废当前代际的在途初始化：递增代际号使创建者结果失效，并以同一个异常了结该代际的加入者
        /// （创建者在代际校验处抛同类异常，两条路径的调用方看到同一种结果）。
        /// <para>无等待者时不构造异常——Destroy() 的常态就是没有在途初始化。</para>
        /// </summary>
        private static void InvalidateInflightInit()
        {
            int generation = _initGeneration;
            _initGeneration++;

            for (int i = 0; i < _initWaiters.Count; i++)
            {
                if (_initWaiters[i].Generation == generation)
                {
                    BroadcastInitResult(generation, new InvalidOperationException(InitInvalidatedMessage));
                    return;
                }
            }
        }

        /// <summary>
        /// 实际初始化流程：创建实例 → 初始化 → 校验代际号后置入全局。
        /// <para>代际不匹配（被 Destroy()/SetInstance() 作废）时抛出异常而非静默返回：
        /// 「await 正常返回」必须等价于「已初始化」。</para>
        /// </summary>
        private static async UniTask InitializeAsyncCore(int generation, IProgress<AssetInitReport> progress, AssetInitOptions options, CancellationToken cancellationToken)
        {
            var impl = ImplFactory?.Invoke() ?? new AssetManagerImpl();
            try
            {
                await impl.InitializeAsync(options, progress, cancellationToken);
            }
            catch
            {
                // 初始化失败：释放半初始化状态，不让 _instance 占用，允许重试
                impl.Dispose();
                throw;
            }

            // Destroy()/SetInstance() 与初始化并发时，丢弃在途结果并告知调用方，防止销毁后实例"复活"
            if (generation != _initGeneration)
            {
                impl.Dispose();
                throw new InvalidOperationException(InitInvalidatedMessage);
            }

            _instance = impl;
            _instanceInitialized = true;
        }

        /// <inheritdoc cref="IAssetManager.InitializePackageAsync(AssetInitOptions, IProgress{AssetInitReport}, CancellationToken)"/>
        public static UniTask InitializePackageAsync(AssetInitOptions options, IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.InitializePackageAsync(options, progress, cancellationToken);
        }

        /// <summary>
        /// 设置外部已创建的实例作为全局管理器。
        /// <para>适用于依赖注入或单元测试场景。在途初始化会一并作废（见 <see cref="InitializeAsync"/>）。</para>
        /// <para><b>不释放原实例</b>：与 <see cref="Destroy"/> 不同，本方法只替换引用——重复调用会静默丢弃
        /// 前一个实例，需要释放请先自行处置。</para>
        /// </summary>
        public static void SetInstance(IAssetManager manager)
        {
            AssertMainThread(nameof(SetInstance));

            _instance = manager ?? throw new ArgumentNullException(nameof(manager));
            _instanceInitialized = true;

            // 作废在途初始化：注入实例优先，在途任务结果（及它的加入者）不得覆盖
            InvalidateInflightInit();
            _initializeTask = default;
        }

        /// <summary>
        /// 销毁全局资源管理器：释放托管实例（含池中闲置实例）与包引用表。
        /// <para><b>不卸载 YooAsset 侧的包与已加载资源</b>——它们刻意保持存活，这正是「<c>Destroy()</c> 后可重新
        /// 初始化复用」的前提。要真正回收内存请用 <see cref="UnloadUnusedAssetsAsync"/>（配合
        /// <see cref="ClearPool"/> 清掉闲置实例）。</para>
        /// </summary>
        public static void Destroy()
        {
            AssertMainThread(nameof(Destroy));

            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
            _instanceInitialized = false;

            // 作废在途初始化任务：结果丢弃、加入者一并了结，可立即重新初始化
            InvalidateInflightInit();
            _initializeTask = default;
        }

        #endregion

        #region Public API — Unload & Query

        /// <inheritdoc cref="IAssetManager.UnloadUnusedAssetsAsync(string, CancellationToken)"/>
        public static UniTask UnloadUnusedAssetsAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.UnloadUnusedAssetsAsync(packageName, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.TryUnloadUnusedAsset(string, string)"/>
        public static void TryUnloadUnusedAsset(string location, string packageName = null)
        {
            EnsureGlobalInitialized();
            _instance.TryUnloadUnusedAsset(location, packageName);
        }

        /// <inheritdoc cref="IAssetManager.CheckLocationValid(string, string)"/>
        public static bool CheckLocationValid(string location, string packageName = null)
        {
            EnsureGlobalInitialized();
            return _instance.CheckLocationValid(location, packageName);
        }

        /// <inheritdoc cref="IAssetManager.IsNeedDownloadFromRemote(string, string)"/>
        public static bool IsNeedDownloadFromRemote(string location, string packageName = null)
        {
            EnsureGlobalInitialized();
            return _instance.IsNeedDownloadFromRemote(location, packageName);
        }

        #endregion

        #region Public API — Hot Update

        /// <inheritdoc cref="IAssetManager.RequestPackageVersionAsync(string, CancellationToken)"/>
        public static UniTask<string> RequestPackageVersionAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.RequestPackageVersionAsync(packageName, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.UpdatePackageManifestAsync(string, string, CancellationToken)"/>
        public static UniTask UpdatePackageManifestAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.UpdatePackageManifestAsync(packageVersion, packageName, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.PreDownloadContentAsync(string, string, CancellationToken)"/>
        public static UniTask PreDownloadContentAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.PreDownloadContentAsync(packageVersion, packageName, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.GetPackageVersion(string)"/>
        public static string GetPackageVersion(string packageName = null)
        {
            EnsureGlobalInitialized();
            return _instance.GetPackageVersion(packageName);
        }

        /// <inheritdoc cref="IAssetManager.CreateDownloader(string[], int, int, string)"/>
        public static AssetDownloaderHandle CreateDownloader(string[] tags = null, int downloadingMaxNumber = 8, int failedRetryCount = 3, string packageName = null)
        {
            EnsureGlobalInitialized();
            return _instance.CreateDownloader(tags, downloadingMaxNumber, failedRetryCount, packageName);
        }

        /// <inheritdoc cref="IAssetManager.DownloadAssetsAsync(string[], Action{float}, string, CancellationToken)"/>
        public static UniTask<bool> DownloadAssetsAsync(string[] tags = null, Action<float> progress = null, string packageName = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.DownloadAssetsAsync(tags, progress, packageName, cancellationToken);
        }

        #endregion

        #region Public API — Load (UniTask)

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, CancellationToken)"/>
        public static UniTask<AssetHandle<T>> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadAsync<T>(location, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, int, CancellationToken)"/>
        public static UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadAsync<T>(location, priority, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Transform, CancellationToken)"/>
        public static UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync(location, parent, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Vector3, Quaternion, Transform, CancellationToken)"/>
        public static UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync(location, position, rotation, parent, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Transform, CancellationToken)"/>
        public static UniTask<T> InstantiateAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync<T>(location, parent, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Vector3, Quaternion, Transform, CancellationToken)"/>
        public static UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateAsync<T>(location, position, rotation, parent, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadSceneAsync(string, bool, Action{float}, CancellationToken)"/>
        public static UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.LoadSceneAsync(location, additive, progress, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.PreloadAllAsync(IEnumerable{string}, Action{float}, CancellationToken)"/>
        public static UniTask PreloadAllAsync(IEnumerable<string> locations, Action<float> progress = null, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.PreloadAllAsync(locations, progress, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadAllAsync{T}(IReadOnlyList{string}, CancellationToken)"/>
        public static UniTask<AssetHandle<T>[]> LoadAllAsync<T>(IReadOnlyList<string> locations, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadAllAsync<T>(locations, cancellationToken);
        }

        #endregion

        #region Public API — Load (Sync)

        /// <inheritdoc cref="IAssetManager.LoadSync{T}(string)"/>
        public static AssetHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object
        {
            EnsureGlobalInitialized();
            return _instance.LoadSync<T>(location);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateSync(string, Transform)"/>
        public static GameObject InstantiateSync(string location, Transform parent = null)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateSync(location, parent);
        }

        /// <inheritdoc cref="IAssetManager.InstantiateSync(string, Vector3, Quaternion, Transform)"/>
        public static GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            EnsureGlobalInitialized();
            return _instance.InstantiateSync(location, position, rotation, parent);
        }

        #endregion

        #region Public API — Sub Assets

        /// <inheritdoc cref="IAssetManager.LoadSubAssetsAsync(string, CancellationToken)"/>
        public static UniTask<SubAssetsHandle> LoadSubAssetsAsync(string location, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.LoadSubAssetsAsync(location, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadSubAssetsSync(string)"/>
        public static SubAssetsHandle LoadSubAssetsSync(string location)
        {
            EnsureGlobalInitialized();
            return _instance.LoadSubAssetsSync(location);
        }

        #endregion

        #region Public API — Raw File

        /// <inheritdoc cref="IAssetManager.LoadRawFileAsync(string, CancellationToken)"/>
        public static UniTask<RawFileHandle> LoadRawFileAsync(string location, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.LoadRawFileAsync(location, cancellationToken);
        }

        /// <inheritdoc cref="IAssetManager.LoadRawFileSync(string)"/>
        public static RawFileHandle LoadRawFileSync(string location)
        {
            EnsureGlobalInitialized();
            return _instance.LoadRawFileSync(location);
        }

        #endregion

        #region Public API — Pool Config

        /// <inheritdoc cref="IAssetManager.SetPoolMaxSize(string, int)"/>
        public static void SetPoolMaxSize(string location, int maxSize)
        {
            EnsureGlobalInitialized();
            _instance.SetPoolMaxSize(location, maxSize);
        }

        /// <inheritdoc cref="IAssetManager.GetPoolStatus(string)"/>
        public static (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
        {
            EnsureGlobalInitialized();
            return _instance.GetPoolStatus(location);
        }

        #endregion

        #region Public API — Pool Control

        /// <summary>
        /// 销毁指定地址的全部闲置池实例（不影响正在使用的实例）。
        /// <para><b>真正释放资源前的必要一步</b>：回池时实例会保留 <c>AssetHandle</c> 以保活资源，
        /// 于是池里只要还留着一个闲置实例，该预制体的引用计数就不会归零，
        /// <see cref="UnloadUnusedAssetsAsync"/> 也就回收不掉它。</para>
        /// <para>当前实现不支持该能力时返回 0 并告警一次。</para>
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>实际销毁的实例数；该地址没有池时返回 0。</returns>
        public static int ClearPool(string location)
        {
            EnsureGlobalInitialized();
            return ResolvePoolController()?.ClearPool(location) ?? 0;
        }

        /// <summary>
        /// 销毁全部闲置池实例（不影响正在使用的实例）。
        /// <para>当前实现不支持该能力时返回 0 并告警一次（同 <see cref="ClearPool"/>）。</para>
        /// </summary>
        /// <returns>实际销毁的实例总数。</returns>
        public static int ClearAllPools()
        {
            EnsureGlobalInitialized();
            return ResolvePoolController()?.ClearAllPools() ?? 0;
        }

        /// <summary>
        /// 取当前实现的能力接口。不支持时告警一次——这是调用方的编程错误，
        /// 但不该每次调用都刷屏。
        /// </summary>
        private static IAssetPoolController ResolvePoolController()
        {
            if (_instance is IAssetPoolController controller)
                return controller;

            if (!_poolCapabilityWarned)
            {
                _poolCapabilityWarned = true;
                LogManager.Warning(LogCategories.AssetManager,
                    "当前 IAssetManager 实现不支持 IAssetPoolController，池清理调用被忽略。" +
                    "若要使用该能力，请在自定义实现上实现 IAssetPoolController。");
            }

            return null;
        }

        #endregion

        #region Public API — Lifecycle

        /// <inheritdoc cref="IAssetManager.DestroyInstance(GameObject)"/>
        public static void DestroyInstance(GameObject instance)
        {
            EnsureGlobalInitialized();
            _instance.DestroyInstance(instance);
        }

        /// <inheritdoc cref="IAssetManager.DestroyInstance{T}(T)"/>
        public static void DestroyInstance<T>(T component) where T : Component
        {
            EnsureGlobalInitialized();
            _instance.DestroyInstance(component);
        }

        #endregion

        #region Internal

        /// <summary>
        /// 加入者登记项：登记时的代际号 + 自己的完成信号。
        /// <para>代际号是「作废」语义的载体——只有同代际的广播才能放行本项。</para>
        /// </summary>
        private readonly struct InitWaiter
        {
            /// <summary>登记时的初始化代际号。</summary>
            public readonly int Generation;

            /// <summary>加入者的完成信号（UniTask promise 只支持单个 continuation，故每个加入者各持一个）。</summary>
            public readonly UniTaskCompletionSource Completion;

            public InitWaiter(int generation, UniTaskCompletionSource completion)
            {
                Generation = generation;
                Completion = completion;
            }
        }

        /// <summary>
        /// 门面入口的主线程契约断言（仅 Editor 编译；Release 下整段是空操作，调用方不必再各自包一层条件编译）。
        /// <para><b>为什么需要</b>：门面的初始化协调是一段无锁的「检查-再赋值」序列（创建者选举、等待者登记、
        /// 代际号递增），没有任何一处能容忍两个线程同时进入——并发发起会造出两个实例（前者不被 Dispose），
        /// 后台线程调用则可能在任意时刻撕裂协议状态。而全仓此前没有任何一句声明这条契约。</para>
        /// <para><b>覆盖边界（有意）：</b>只管门面入口。底层 <see cref="IAssetManager"/> 实现与
        /// <see cref="InstanceTracker"/> 的静态计数同样按主线程设计，但不在这里断言。</para>
        /// <para><b>只报首错</b>：越线程调用通常每帧重复发生，记一处即可防洪泛。主线程检测复用 UniTask 的
        /// <see cref="Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread"/>（与 Message 的 <c>MainThreadGuard</c>、
        /// Pipeline 的阶段写入断言同源）；这里**内联本地副本**而非引用 <c>XMessage.Internal.MainThreadGuard</c>
        /// ——生产代码不得跨模块引用 Internal 命名空间（先例：UI 内联十行适配器）。</para>
        /// </summary>
        /// <param name="api">被调用的入口名，用于拼装报错文案。</param>
        private static void AssertMainThread(string api)
        {
#if UNITY_EDITOR
            if (_mainThreadViolationLogged || Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
                return;

            _mainThreadViolationLogged = true;
            LogManager.Error(LogCategories.AssetManager,
                "{0} 必须在 Unity 主线程调用：门面的初始化协调是无锁的检查-再赋值序列，" +
                "跨线程进入会并发改写实例、等待者表与代际号。从非主线程访问请先切回主线程；" +
                "线程契约见模块 README「线程契约」段。本提示每个会话只报一次。", api);
#endif
        }

        private static void EnsureGlobalInitialized()
        {
            if (!_instanceInitialized || _instance == null)
                throw new InvalidOperationException(
                    "[AssetManager] AssetManager 尚未初始化。请先调用 AssetManager.InitializeAsync() 完成初始化（或登记 AssetBootstrapStage 交由启动流程完成）。");
        }

        #endregion
    }
}