using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using XFramework.XCore;
using XFramework.XLoad;
using XFramework.XAsset;


namespace XFramework.XAsset

{
    /// <summary>
    /// 资源服务节点。作为 <see cref="LeafNode"/> 挂载到节点树中，提供全局资源加载能力。
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
    /// <para>内部委托到 <see cref="AssetSystem"/> 的全局 <see cref="IAssetService"/> 实例。</para>
    /// <para>自动管理引用计数、对象池、延迟卸载、场景加载、预加载。</para>
    /// </summary>
    public class AssetNode : LeafNode, IAssetNode, ILoadable
    {
        #region Private Fields

        private IAssetService _service;

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            // AssetNode 不销毁 AssetSystem 的全局实例（由 GameLauncher 统一管理）
            _service = null;
            base.OnDestroy();
        }

        #endregion

        #region ILoadable

        int ILoadable.Phase => 0;

        async UniTask ILoadable.LoadAsync(LoadContext context, CancellationToken cancellationToken)
        {
            // 确保 AssetSystem 已初始化（带进度报告）
            if (!AssetSystem.IsInitialized)
            {
                var progress = new LoadContextProgress(context);
                await AssetSystem.InitializeAsync(progress, cancellationToken);
            }

            _service = AssetSystem.Instance;
        }

        #endregion

        #region IAssetNode — UniTask

        public UniTask<T> LoadAsync<T>(string location, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => _service.LoadAsync<T>(location, cancellationToken);

        public UniTask<T> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => _service.LoadAsync<T>(location, priority, cancellationToken);

        public UniTask<GameObject> InstantiateAsync(string location, Transform parent = null)
            => _service.InstantiateAsync(location, parent);

        public UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
            => _service.InstantiateAsync(location, position, rotation, parent);

        public UniTask<T> InstantiateAsync<T>(string location, Transform parent = null) where T : Component
            => _service.InstantiateAsync<T>(location, parent);

        public UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
            => _service.InstantiateAsync<T>(location, position, rotation, parent);

        public UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null)
            => _service.LoadSceneAsync(location, additive, progress);

        public UniTask PreloadAllAsync(IEnumerable<string> locations)
            => _service.PreloadAllAsync(locations);

        #endregion

        #region IAssetNode — Callback

        public void LoadAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null) where T : UnityEngine.Object
            => _service.LoadAsync(location, onCompleted, onError);

        public void InstantiateAsync(string location, Action<GameObject> onCompleted, Action<string> onError = null, Transform parent = null)
            => _service.InstantiateAsync(location, onCompleted, onError, parent);

        public void InstantiateAsync<T>(string location, Action<T> onCompleted, Action<string> onError = null, Transform parent = null) where T : Component
            => _service.InstantiateAsync(location, onCompleted, onError, parent);

        #endregion

        #region IAssetNode — Pool Config

        public void SetPoolMaxSize(string location, int maxSize)
            => _service.SetPoolMaxSize(location, maxSize);

        public (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
            => _service.GetPoolStatus(location);

        #endregion

        #region IAssetNode — Lifecycle

        public void Release(UnityEngine.Object asset)
            => _service.Release(asset);

        public void DestroyInstance(GameObject instance)
            => _service.DestroyInstance(instance);

        public void DestroyInstance<T>(T component) where T : Component
            => _service.DestroyInstance(component);

        #endregion
    }
}


