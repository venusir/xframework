using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XLoader;

namespace XFramework.XAsset
{

    /// <summary>
    /// 资源管理器公共接口。与节点树无关，可供任何对象（MonoBehaviour、纯 C# 类等）直接使用。
    /// <para>通过 <see cref="AssetManager"/> 的静态方法直接调用，或注入 <see cref="IAssetManager"/> 实例使用。</para>
    /// </summary>
    public interface IAssetManager : IDisposable
    {
        #region Initialize

        /// <summary>
        /// 初始化资源服务（默认包）。
        /// </summary>
        /// <param name="progress">初始化进度回调，<see cref="LoadProgress"/> 包含进度和描述信息。</param>
        /// <param name="options">初始化配置。为 null 时使用默认配置（默认包 + 离线模式）。</param>
        UniTask InitializeAsync(LoadProgress progress, AssetInitOptions options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 初始化额外资源包（多包场景）。
        /// <para>包已存在且初始化成功时跳过初始化，直接刷新版本与清单（包复用语义）。</para>
        /// </summary>
        UniTask InitializePackageAsync(AssetInitOptions options, LoadProgress progress, CancellationToken cancellationToken = default);

        #endregion

        #region Unload & Query

        /// <summary>
        /// 卸载指定包中所有未使用的资源（引用计数为 0 且未被引用的 bundle）。
        /// <para>正在被 <see cref="AssetHandle{T}"/> 或实例引用的资源不会卸载。典型场景：内存告警、关卡切换后回收。</para>
        /// </summary>
        UniTask UnloadUnusedAssetsAsync(string packageName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试立即卸载单个未使用的资源。该资源仍被引用（引用计数大于 0）时无效果。
        /// </summary>
        void TryUnloadUnusedAsset(string location, string packageName = null);

        /// <summary>
        /// 检查资源定位路径在指定包中是否存在且合法（即 <see cref="LoadAsync{T}(string, CancellationToken)"/> 可成功加载）。
        /// <para>包不存在时返回 false。</para>
        /// </summary>
        bool CheckLocationValid(string location, string packageName = null);

        /// <summary>
        /// 检查资源是否来自远端（加载前需先下载）。Offline 模式恒为 false。
        /// <para>包不存在时返回 false。</para>
        /// </summary>
        bool IsNeedDownloadFromRemote(string location, string packageName = null);

        #endregion

        #region Load — UniTask

        /// <summary>
        /// 异步加载资源，返回 <see cref="AssetHandle{T}"/> 句柄。调用方应通过 <c>using</c> 块自动管理资源生命周期。
        /// </summary>
        /// <example>
        /// <code>
        /// using (var handle = await LoadAsync<TextAsset>(location, ct))
        /// {
        ///     var text = handle.Asset.text;
        /// }
        /// </code>
        /// </example>
        UniTask<AssetHandle<T>> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载资源（带优先级），返回 <see cref="AssetHandle{T}"/> 句柄。
        /// </summary>
        UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object;

        /// <summary>
        /// 加载资源并实例化，返回实例 GameObject（自动管理引用生命周期）。
        /// </summary>
        UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 加载资源并实例化，带位置旋转，返回实例 GameObject。
        /// </summary>
        UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 加载资源并实例化，返回实例上 GetComponent{T}() 的结果。
        /// </summary>
        UniTask<T> InstantiateAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component;

        /// <summary>
        /// 加载资源并实例化，带位置旋转，返回实例上 GetComponent{T}() 的结果。
        /// </summary>
        UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default) where T : Component;

        /// <summary>
        /// 异步加载场景。
        /// <para>失败时返回无效的 <c>default(Scene)</c>，调用方需用 <see cref="Scene.IsValid"/> 校验。</para>
        /// </summary>
        UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量预加载资源到缓存（引用计数不增加）。
        /// </summary>
        UniTask PreloadAllAsync(IEnumerable<string> locations, CancellationToken cancellationToken = default);

        #endregion

        #region Pool Config

        /// <summary>
        /// 设置指定预制体的对象池最大容量。
        /// </summary>
        void SetPoolMaxSize(string location, int maxSize);

        /// <summary>
        /// 获取指定资源地址的对象池状态（调试用）。
        /// </summary>
        (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location);

        #endregion

        #region Lifecycle

        /// <summary>
        /// 销毁/回收实例。回池时保留资源引用（保活），实例真正销毁时才释放。
        /// </summary>
        void DestroyInstance(GameObject instance);

        /// <summary>
        /// 销毁/回收实例（Component 版本）。
        /// </summary>
        void DestroyInstance<T>(T component) where T : Component;

        #endregion
    }
}
