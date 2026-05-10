using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XCore;

namespace XFramework.XAsset


{
    /// <summary>
    /// 资源服务接口。提供统一的资源加载、实例化与生命周期管理。
    /// <para>实现此接口的服务节点可通过 <see cref="BaseNode.Get{T}"/> 被其他节点访问。</para>
    /// </summary>
    public interface IAssetNode
    {
        #region UniTask

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
        /// <param name="location">场景资源地址。</param>
        /// <param name="additive">是否叠加加载（<see cref="LoadSceneMode.Additive"/>）。</param>
        /// <param name="progress">加载进度回调（0~1）。</param>
        UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null);

        /// <summary>
        /// 批量预加载资源到缓存（引用计数不增加）。
        /// </summary>
        /// <param name="locations">要预加载的资源地址列表。</param>
        UniTask PreloadAllAsync(IEnumerable<string> locations);

        #endregion

        #region Callback

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
        /// <param name="location">预制体资源地址。</param>
        /// <param name="maxSize">最大闲置实例数。</param>
        void SetPoolMaxSize(string location, int maxSize);

        /// <summary>
        /// 获取指定资源地址的对象池状态（调试用）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>池中闲置数、活跃实例数、池最大容量。</returns>
        (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location);

        #endregion

        #region Lifecycle

        /// <summary>
        /// 释放资源（引用计数 -1）。通过资源实例查找映射 location。
        /// <para>引用计数归零时触发延迟卸载。</para>
        /// </summary>
        void Release(UnityEngine.Object asset);

        /// <summary>
        /// 销毁/回收实例。引用计数归零时自动释放资源。
        /// </summary>
        void DestroyInstance(GameObject instance);

        /// <summary>
        /// 销毁/回收实例（Component 版本）。内部调用 <see cref="DestroyInstance(GameObject)"/>。
        /// </summary>
        void DestroyInstance<T>(T component) where T : Component;

        #endregion
    }
}
