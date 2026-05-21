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
        /// 初始化资源服务。
        /// </summary>
        /// <param name="progress">初始化进度回调，<see cref="LoadProgress"/> 包含进度和描述信息。</param>
        UniTask InitializeAsync(LoadProgress progress, CancellationToken cancellationToken = default);

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
        UniTask<GameObject> InstantiateAsync(string location, Transform parent = null);

        /// <summary>
        /// 加载资源并实例化，带位置旋转，返回实例 GameObject。
        /// </summary>
        UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null);

        /// <summary>
        /// 加载资源并实例化，返回实例上 GetComponent{T}() 的结果。
        /// </summary>
        UniTask<T> InstantiateAsync<T>(string location, Transform parent = null) where T : Component;

        /// <summary>
        /// 加载资源并实例化，带位置旋转，返回实例上 GetComponent{T}() 的结果。
        /// </summary>
        UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component;

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null);

        /// <summary>
        /// 批量预加载资源到缓存（引用计数不增加）。
        /// </summary>
        UniTask PreloadAllAsync(IEnumerable<string> locations);

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
        /// 销毁/回收实例。实例回池时自动释放对应的资源引用。
        /// </summary>
        void DestroyInstance(GameObject instance);

        /// <summary>
        /// 销毁/回收实例（Component 版本）。
        /// </summary>
        void DestroyInstance<T>(T component) where T : Component;

        #endregion
    }
}
