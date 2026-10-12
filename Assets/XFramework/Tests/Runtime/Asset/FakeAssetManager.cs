using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using XFramework.XAsset;

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// <see cref="IAssetManager"/> 的最小假件：**只让会话复位用例用到的那两个成员可编程**
    /// （<see cref="InitializeAsync"/> / <see cref="Dispose"/>），其余一律抛 <see cref="NotSupportedException"/>。
    /// <para><b>为什么是「抛」而不是空实现</b>：空实现会让「门面把调用转发错了成员」这类缺陷静默通过；
    /// 抛出来则立刻暴露。体例照 <c>FakeInputProvider</c>。</para>
    /// <para><b>为什么 Runtime 树要另有一份</b>：<c>Tests/Editor/Asset/FakeAssetManager</c> 在 Editor 测试
    /// 程序集里（两个程序集互不可见），<c>Tests/Runtime/UI/FakeAssetManager</c> 则是 UI 侧专用的池替身、
    /// 对 <c>InitializeAsync</c> 抛 NotUsed——同名假件按测试树各存一份是既有先例。</para>
    /// </summary>
    internal sealed class FakeAssetManager : IAssetManager
    {
        /// <summary><see cref="InitializeAsync"/> 的调用计数。</summary>
        public int InitCallCount;

        /// <summary>
        /// <see cref="InitializeAsync"/> 返回的任务。默认立即完成；用例可注入挂起任务
        /// （<c>UniTaskCompletionSource</c>）来模拟「初始化在途时被复位」。
        /// </summary>
        public UniTask InitTask = UniTask.CompletedTask;

        /// <summary>是否被 <see cref="Dispose"/> 过（断言「复位只丢引用、不调 Dispose」用）。</summary>
        public bool Disposed;

        public UniTask InitializeAsync(AssetInitOptions options = null,
            IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
        {
            InitCallCount++;
            return InitTask;
        }

        public void Dispose() => Disposed = true;

        #region 其余成员 — 一律不支持

        private static NotSupportedException NotUsed([CallerMemberName] string member = null)
            => new NotSupportedException($"{member} 未被最小假件支持——它只服务会话复位用例（初始化 / 释放）。");

        public UniTask InitializePackageAsync(AssetInitOptions options,
            IProgress<AssetInitReport> progress = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        public UniTask UnloadUnusedAssetsAsync(string packageName = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        public void TryUnloadUnusedAsset(string location, string packageName = null) => throw NotUsed();

        public bool CheckLocationValid(string location, string packageName = null) => throw NotUsed();

        public bool IsNeedDownloadFromRemote(string location, string packageName = null) => throw NotUsed();

        public UniTask<string> RequestPackageVersionAsync(string packageName = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        public UniTask UpdatePackageManifestAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        public UniTask PreDownloadContentAsync(string packageVersion, string packageName = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        public string GetPackageVersion(string packageName = null) => throw NotUsed();

        public AssetDownloaderHandle CreateDownloader(string[] tags = null, int downloadingMaxNumber = 8,
            int failedRetryCount = 3, string packageName = null) => throw NotUsed();

        public UniTask<bool> DownloadAssetsAsync(string[] tags = null, Action<float> progress = null,
            string packageName = null, CancellationToken cancellationToken = default) => throw NotUsed();

        public UniTask<AssetHandle<T>> LoadAsync<T>(string location, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object => throw NotUsed();

        public UniTask<AssetHandle<T>> LoadAsync<T>(string location, int priority, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object => throw NotUsed();

        public UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
            => throw NotUsed();

        public UniTask<GameObject> InstantiateAsync(string location, Vector3 position, Quaternion rotation,
            Transform parent = null, CancellationToken cancellationToken = default) => throw NotUsed();

        public UniTask<T> InstantiateAsync<T>(string location, Transform parent = null, CancellationToken cancellationToken = default)
            where T : Component => throw NotUsed();

        public UniTask<T> InstantiateAsync<T>(string location, Vector3 position, Quaternion rotation,
            Transform parent = null, CancellationToken cancellationToken = default) where T : Component => throw NotUsed();

        public UniTask<Scene> LoadSceneAsync(string location, bool additive = false, Action<float> progress = null,
            CancellationToken cancellationToken = default) => throw NotUsed();

        public UniTask PreloadAllAsync(IEnumerable<string> locations, Action<float> progress = null,
            CancellationToken cancellationToken = default) => throw NotUsed();

        public UniTask<AssetHandle<T>[]> LoadAllAsync<T>(IReadOnlyList<string> locations, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object => throw NotUsed();

        public AssetHandle<T> LoadSync<T>(string location) where T : UnityEngine.Object => throw NotUsed();

        public GameObject InstantiateSync(string location, Transform parent = null) => throw NotUsed();

        public GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation, Transform parent = null)
            => throw NotUsed();

        public UniTask<SubAssetsHandle> LoadSubAssetsAsync(string location, CancellationToken cancellationToken = default)
            => throw NotUsed();

        public SubAssetsHandle LoadSubAssetsSync(string location) => throw NotUsed();

        public UniTask<RawFileHandle> LoadRawFileAsync(string location, CancellationToken cancellationToken = default)
            => throw NotUsed();

        public RawFileHandle LoadRawFileSync(string location) => throw NotUsed();

        public void SetPoolMaxSize(string location, int maxSize) => throw NotUsed();

        public (int pooledCount, int activeCount, int maxPoolSize) GetPoolStatus(string location) => throw NotUsed();

        public void DestroyInstance(GameObject instance) => throw NotUsed();

        public void DestroyInstance<T>(T component) where T : Component => throw NotUsed();

        #endregion
    }
}
