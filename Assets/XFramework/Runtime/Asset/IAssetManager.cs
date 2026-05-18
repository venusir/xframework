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
        /// 初始化资源服务（无进度版本）。
        /// </summary>
        UniTask InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 初始化资源服务（带进度报告版本）。
        /// </summary>
        /// <param name="progress">初始化进度回调，<see cref="LoadContext"/> 包含进度和描述信息。</param>
        UniTask InitializeAsync(IProgress<LoadContext> progress, CancellationToken cancellationToken = default);

        #endregion

        #region Load — UniTask

        /// <summary>
        /// 异步加载资源，返回资源本体（引用计数 +1）。
        /// </summary>
        UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载资源，带优先级。
        /// </summary>
        UniTask<T> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object;

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

        #region Load — Callback

        /// <summary>
        /// 异步加载资源（回调版本）。
        /// </summary>
        void LoadAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null) where T : UnityEngine.Object;

        /// <summary>
        /// 加载资源并实例化（回调版本）。
        /// </summary>
        void InstantiateAsync(string location, Action<GameObject> onCompleted, Action<string> onError = null, Transform parent = null);

        /// <summary>
        /// 加载资源并实例化，返回组件（回调版本）。
        /// </summary>
        void InstantiateAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null, Transform parent = null) where T : Component;

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
        /// 释放资源（引用计数 -1）。通过资源实例查找映射 location。
        /// </summary>
        void Release(UnityEngine.Object asset);

        /// <summary>
        /// 销毁/回收实例。引用计数归零时自动释放资源。
        /// </summary>
        void DestroyInstance(GameObject instance);

        /// <summary>
        /// 销毁/回收实例（Component 版本）。
        /// </summary>
        void DestroyInstance<T>(T component) where T : Component;

        #endregion
    }
}
