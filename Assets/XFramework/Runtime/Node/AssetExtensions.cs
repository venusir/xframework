using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XNode;

namespace XFramework.XAsset
{

    /// <summary>
    /// <see cref="IBaseNode"/> 的资源加载扩展方法。允许节点树中的任意节点直接使用全局 <see cref="AssetManager"/> 加载资源。
    /// <para>所有方法委托到 <see cref="AssetManager"/> 的静态方法，需先调用 <see cref="AssetManager.InitializeAsync(XFramework.XLoader.LoadProgress, AssetInitOptions, System.Threading.CancellationToken)"/> 初始化。</para>
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
        public static UniTask<AssetHandle<T>> LoadAssetAsync<T>(this IBaseNode self, string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => AssetManager.LoadAsync<T>(location, cancellationToken);

        /// <inheritdoc cref="IAssetManager.LoadAsync{T}(string, int, CancellationToken)"/>
        public static UniTask<AssetHandle<T>> LoadAssetAsync<T>(this IBaseNode self, string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => AssetManager.LoadAsync<T>(location, priority, cancellationToken);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Transform, CancellationToken)"/>
        public static UniTask<GameObject> InstantiateAssetAsync(this IBaseNode self, string location, Transform parent = null, CancellationToken cancellationToken = default)
            => AssetManager.InstantiateAsync(location, parent, cancellationToken);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync(string, Vector3, Quaternion, Transform, CancellationToken)"/>
        public static UniTask<GameObject> InstantiateAssetAsync(this IBaseNode self, string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
            => AssetManager.InstantiateAsync(location, position, rotation, parent, cancellationToken);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Transform, CancellationToken)"/>
        public static UniTask<T> InstantiateAssetAsync<T>(this IBaseNode self, string location, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
            => AssetManager.InstantiateAsync<T>(location, parent, cancellationToken);

        /// <inheritdoc cref="IAssetManager.InstantiateAsync{T}(string, Vector3, Quaternion, Transform, CancellationToken)"/>
        public static UniTask<T> InstantiateAssetAsync<T>(this IBaseNode self, string location, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default) where T : Component
            => AssetManager.InstantiateAsync<T>(location, position, rotation, parent, cancellationToken);

        /// <inheritdoc cref="IAssetManager.LoadSceneAsync(string, bool, Action{float}, CancellationToken)"/>
        public static UniTask<Scene> LoadSceneAssetAsync(this IBaseNode self, string location, bool additive = false, Action<float> progress = null, CancellationToken cancellationToken = default)
            => AssetManager.LoadSceneAsync(location, additive, progress, cancellationToken);

        /// <inheritdoc cref="IAssetManager.PreloadAllAsync(IEnumerable{string}, Action{float}, CancellationToken)"/>
        public static UniTask PreloadAssetsAsync(this IBaseNode self, IEnumerable<string> locations, CancellationToken cancellationToken = default)
            => AssetManager.PreloadAllAsync(locations, cancellationToken: cancellationToken);

        #endregion


        #region Pool Config

        /// <inheritdoc cref="IAssetManager.SetPoolMaxSize(string, int)"/>
        public static void SetAssetPoolMaxSize(this IBaseNode self, string location, int maxSize)
            => AssetManager.SetPoolMaxSize(location, maxSize);

        /// <inheritdoc cref="IAssetManager.GetPoolStatus(string)"/>
        public static (int pooledCount, int activeCount, int maxPoolSize) GetAssetPoolStatus(this IBaseNode self, string location)
            => AssetManager.GetPoolStatus(location);

        #endregion

        #region Lifecycle

        /// <inheritdoc cref="IAssetManager.DestroyInstance(GameObject)"/>
        public static void DestroyAssetInstance(this IBaseNode self, GameObject instance)
            => AssetManager.DestroyInstance(instance);

        /// <inheritdoc cref="IAssetManager.DestroyInstance{T}(T)"/>
        public static void DestroyAssetInstance<T>(this IBaseNode self, T component) where T : Component
            => AssetManager.DestroyInstance(component);

        #endregion
    }
}