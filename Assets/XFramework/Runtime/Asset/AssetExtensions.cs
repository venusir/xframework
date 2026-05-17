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
    /// <see cref="IBaseNode"/> 的资源加载扩展方法。允许节点树中的任意节点直接使用全局 <see cref="AssetManager"/> 加载资源。
    /// <para>所有方法委托到 <see cref="AssetManager.Instance"/>，需先调用 <see cref="AssetManager.InitializeAsync(System.Threading.CancellationToken)"/> 初始化。</para>
    /// <para>使用示例：</para>
    /// <code>
    /// // 在任意节点中直接调用
    /// var prefab = await this.LoadAssetAsync<GameObject>("characters/player");
    /// var go = await this.InstantiateAssetAsync("characters/player");
    /// this.ReleaseAsset(prefab);
    /// this.DestroyAssetInstance(go);
    /// </code>
    /// </summary>
    public static class AssetExtensions
    {
        #region UniTask — Load

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, CancellationToken)"/>
        public static UniTask<T> LoadAssetAsync<T>(this IBaseNode self, string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => AssetManager.Instance.LoadAsync<T>(location, cancellationToken);

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, int, CancellationToken)"/>
        public static UniTask<T> LoadAssetAsync<T>(this IBaseNode self, string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => AssetManager.Instance.LoadAsync<T>(location, priority, cancellationToken);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Transform)"/>
        public static UniTask<GameObject> InstantiateAssetAsync(this IBaseNode self, string location, Transform parent = null)
            => AssetManager.Instance.InstantiateAsync(location, parent);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Vector3, Quaternion, Transform)"/>
        public static UniTask<GameObject> InstantiateAssetAsync(this IBaseNode self, string location, Vector3 position, Quaternion rotation, Transform parent = null)
            => AssetManager.Instance.InstantiateAsync(location, position, rotation, parent);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Transform)"/>
        public static UniTask<T> InstantiateAssetAsync<T>(this IBaseNode self, string location, Transform parent = null) where T : Component
            => AssetManager.Instance.InstantiateAsync<T>(location, parent);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Vector3, Quaternion, Transform)"/>
        public static UniTask<T> InstantiateAssetAsync<T>(this IBaseNode self, string location, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
            => AssetManager.Instance.InstantiateAsync<T>(location, position, rotation, parent);

        /// <inheritdoc cref="IAssetManager.LoadSceneAsync(string, bool, Action{float})"/>
        public static UniTask<Scene> LoadSceneAssetAsync(this IBaseNode self, string location, bool additive = false, Action<float> progress = null)
            => AssetManager.Instance.LoadSceneAsync(location, additive, progress);

        /// <inheritdoc cref="IAssetManager.PreloadAllAsync(IEnumerable{string})"/>
        public static UniTask PreloadAssetsAsync(this IBaseNode self, IEnumerable<string> locations)
            => AssetManager.Instance.PreloadAllAsync(locations);

        #endregion

        #region Callback — Load

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, Action{T}, Action{string})"/>
        public static void LoadAssetAsync<T>(this IBaseNode self, string location, Action<T> onCompleted, Action<string> onError = null) where T : UnityEngine.Object
            => AssetManager.Instance.LoadAsync(location, onCompleted, onError);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Action{GameObject}, Action{string}, Transform)"/>
        public static void InstantiateAssetAsync(this IBaseNode self, string location, Action<GameObject> onCompleted, Action<string> onError = null, Transform parent = null)
            => AssetManager.Instance.InstantiateAsync(location, onCompleted, onError, parent);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Action{T}, Action{string}, Transform)"/>
        public static void InstantiateAssetAsync<T>(this IBaseNode self, string location, Action<T> onCompleted, Action<string> onError = null, Transform parent = null) where T : Component
            => AssetManager.Instance.InstantiateAsync(location, onCompleted, onError, parent);

        #endregion

        #region Pool Config

        /// <inheritdoc cref="IAssetManager.SetPoolMaxSize(string, int)"/>
        public static void SetAssetPoolMaxSize(this IBaseNode self, string location, int maxSize)
            => AssetManager.Instance.SetPoolMaxSize(location, maxSize);

        /// <inheritdoc cref="IAssetManager.GetPoolStatus(string)"/>
        public static (int pooledCount, int activeCount, int maxPoolSize) GetAssetPoolStatus(this IBaseNode self, string location)
            => AssetManager.Instance.GetPoolStatus(location);

        #endregion

        #region Lifecycle

        /// <inheritdoc cref="IAssetManager.Release(UnityEngine.Object)"/>
        public static void ReleaseAsset(this IBaseNode self, UnityEngine.Object asset)
            => AssetManager.Instance.Release(asset);

        /// <inheritdoc cref="IAssetManager.DestroyInstance(GameObject)"/>
        public static void DestroyAssetInstance(this IBaseNode self, GameObject instance)
            => AssetManager.Instance.DestroyInstance(instance);

        /// <inheritdoc cref="IAssetManager.DestroyInstance{T}(T)"/>
        public static void DestroyAssetInstance<T>(this IBaseNode self, T component) where T : Component
            => AssetManager.Instance.DestroyInstance(component);

        #endregion
    }
}