using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 资源服务接口。提供统一的资源加载、实例化与生命周期管理。
    /// <para>实现此接口的服务节点可通过 <see cref="BaseNode.Get{T}"/> 被其他节点访问。</para>
    /// </summary>
    public interface IAssetService
    {
        #region UniTask

        /// <summary>
        /// 异步加载资源，返回资源本体（引用计数 +1）。
        /// </summary>
        UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object;

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

        /// <summary>
        /// 销毁服务，清理所有缓存和池。
        /// </summary>
        void Destroy();

        #endregion
    }
}
