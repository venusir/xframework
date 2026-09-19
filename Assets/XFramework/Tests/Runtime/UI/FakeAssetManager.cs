using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XAsset;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// <see cref="IAssetManager"/> 假实现：只让「实例化一个挂好组件的空物体 → 回收它」这条路真的能跑，
    /// 其余成员一律抛 <see cref="NotImplementedException"/>。
    /// <para><b>为什么需要它</b>：<c>UIHudManagerImpl</c> 与 <c>UITipManagerImpl</c> 都硬编码了
    /// <see cref="XAsset.AssetManager"/>，于是 Runtime 测试里根本造不出 HUD / Tip——这两条通路此前
    /// 只能靠评审。用公开的 <see cref="AssetManager.SetInstance"/> 注入本实现后，它们才谈得上测试。</para>
    /// <para><b>刻意不做对象池</b>：本替身只回答「创建发生了没有、回收发生了没有、发生了几次」。
    /// 池语义由真实的 <c>AssetManager</c> 负责，在这里再模拟一份，只会得到一份自说自话的实现。
    /// 回收照真实池的做法（失活 + 脱离父节点、不销毁），这样用例在回收之后仍能检查实例状态。</para>
    /// </summary>
    internal sealed class FakeAssetManager : IAssetManager
    {
        #region Fields

        /// <summary>创建出的全部物体，供 fixture 收尾销毁（本替身不销毁任何东西）。</summary>
        private readonly List<GameObject> _created = new List<GameObject>();

        /// <summary>按地址登记要挂的组件类型。未登记的地址返回空物体，用于覆盖「预制体缺组件」分支。</summary>
        private readonly Dictionary<string, Type[]> _componentsByLocation = new Dictionary<string, Type[]>();

        #endregion

        #region Test Hooks

        /// <summary>InstantiateAsync 调用次数。</summary>
        public int InstantiateCount { get; private set; }

        /// <summary>DestroyInstance 调用次数。</summary>
        public int DestroyCount { get; private set; }

        /// <summary>最近一次创建出的物体（断言回收前后的状态用）。</summary>
        public GameObject LastCreated { get; private set; }

        /// <summary>置 true 后 InstantiateAsync 直接返回 null，用于覆盖「加载失败」分支。</summary>
        public bool FailInstantiate { get; set; }

        /// <summary>登记某个地址应当挂上什么组件（可多个，按顺序添加）。</summary>
        public void RegisterPrefab(string location, params Type[] componentTypes)
            => _componentsByLocation[location] = componentTypes;

        /// <summary>登记某个地址应当挂上什么组件。</summary>
        public void RegisterPrefab<T>(string location) where T : Component
            => RegisterPrefab(location, typeof(T));

        /// <summary>销毁本替身造出的全部物体（fixture TearDown 调用）。</summary>
        public void DestroyAll()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        #endregion

        #region Implemented — 实例化与回收

        /// <inheritdoc/>
        public UniTask<GameObject> InstantiateAsync(string location, Transform parent = null,
            CancellationToken cancellationToken = default)
            => UniTask.FromResult(CreateInstance(location, parent));

        /// <inheritdoc/>
        public UniTask<T> InstantiateAsync<T>(string location, Transform parent = null,
            CancellationToken cancellationToken = default) where T : Component
        {
            var go = CreateInstance(location, parent);
            return UniTask.FromResult(go == null ? null : go.GetComponent<T>());
        }

        /// <inheritdoc/>
        public void DestroyInstance(GameObject instance)
        {
            DestroyCount++;

            if (instance == null)
                return;

            // 照真实池的做法：失活 + 脱离父节点，不销毁——用例回收后还要能检查实例
            instance.SetActive(false);
            instance.transform.SetParent(null, false);
        }

        /// <inheritdoc/>
        public void DestroyInstance<T>(T component) where T : Component
            => DestroyInstance(component == null ? null : component.gameObject);

        /// <inheritdoc/>
        public void Dispose() { }

        #endregion

        #region Private

        private GameObject CreateInstance(string location, Transform parent)
        {
            InstantiateCount++;

            if (FailInstantiate)
                return null;

            var go = new GameObject($"Fake_{location}", typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent, false);

            if (_componentsByLocation.TryGetValue(location, out var componentTypes))
            {
                for (int i = 0; i < componentTypes.Length; i++)
                    go.AddComponent(componentTypes[i]);
            }

            _created.Add(go);
            LastCreated = go;
            return go;
        }

        /// <summary>
        /// 本替身不涉及的成员一律抛这个：带调用者名字，现场就能看出是哪条通路摸到了未实现的接口。
        /// <para>刻意不返回默认值——假实现悄悄返回 default 会让「其实没走通」看起来像走通了。</para>
        /// </summary>
        private static Exception NotUsed([CallerMemberName] string member = null)
            => new NotImplementedException($"[FakeAssetManager] {member} 未被本测试替身实现");

        #endregion

        #region Not Implemented — 其余成员

        /// <inheritdoc/>
        public UniTask InitializeAsync(AssetInitOptions options = null,
            IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask InitializePackageAsync(AssetInitOptions options,
            IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask UnloadUnusedAssetsAsync(string packageName = null,
            CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public void TryUnloadUnusedAsset(string location, string packageName = null) => throw NotUsed();

        /// <inheritdoc/>
        public bool CheckLocationValid(string location, string packageName = null) => throw NotUsed();

        /// <inheritdoc/>
        public bool IsNeedDownloadFromRemote(string location, string packageName = null) => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<string> RequestPackageVersionAsync(string packageName = null,
            CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask UpdatePackageManifestAsync(string packageVersion, string packageName = null,
            CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask PreDownloadContentAsync(string packageVersion, string packageName = null,
            CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public string GetPackageVersion(string packageName = null) => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<bool> DownloadAssetsAsync(string[] tags = null, Action<float> progress = null,
            string packageName = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<AssetHandle<T>> LoadAsync<T>(string location,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation,
            Transform parent = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation,
            Transform parent = null, CancellationToken cancellationToken = default) where T : Component
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<Scene> LoadSceneAsync(string location, bool additive = false,
            Action<float> progress = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask PreloadAllAsync(IEnumerable<string> locations, Action<float> progress = null,
            CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<AssetHandle<T>[]> LoadAllAsync<T>(IReadOnlyList<string> locations,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
            => throw NotUsed();

        /// <inheritdoc/>
        public AssetHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object => throw NotUsed();

        /// <inheritdoc/>
        public GameObject InstantiateSync(string location, Transform parent = null) => throw NotUsed();

        /// <inheritdoc/>
        public GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation,
            Transform parent = null)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<SubAssetsHandle> LoadSubAssetsAsync(string location,
            CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public UniTask<RawFileHandle> LoadRawFileAsync(string location,
            CancellationToken cancellationToken = default)
            => throw NotUsed();

        /// <inheritdoc/>
        public AssetDownloaderHandle CreateDownloader(string[] tags = null, int downloadingMaxNumber = 8,
            int failedRetryCount = 3, string packageName = null)
            => throw NotUsed();

        /// <inheritdoc/>
        public SubAssetsHandle LoadSubAssetsSync(string location) => throw NotUsed();

        /// <inheritdoc/>
        public RawFileHandle LoadRawFileSync(string location) => throw NotUsed();

        /// <inheritdoc/>
        public (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location)
            => throw NotUsed();

        /// <inheritdoc/>
        public void SetPoolMaxSize(string location, int maxSize) => throw NotUsed();

        #endregion
    }
}
