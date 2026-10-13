using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using YooAsset;

// 名字必须区分开：本文件同时可见 UnityEngine.SceneManagement 与 YooAsset，两边各有一个
// SceneHandle（Unity 6 的 UnityEngine.SceneManagement.SceneHandle 与 YooAsset 的同名类型），
// 裸写会 CS0104 二义。判的是**类型名**，与成员名无关——所以这里给 YooAsset 那个起别名，
// 而不是改成员名。同 Message/Event 的 DispatchListPool 那条先例。
using YooAssetSceneHandle = YooAsset.SceneHandle;

namespace XFramework.XAsset
{
    /// <summary>
    /// <see cref="ISceneLoadSource"/> 的默认实现：把 YooAsset 的 <c>SceneHandle</c> 包成
    /// <see cref="ISceneLoadOperation"/>。
    /// <para><b>这一层刻意做成纯转发，且没有用例覆盖</b>：它落在测试缝之下。每一处调用都对着
    /// YooAsset 2.3.18 的源码核对过（<c>SceneHandle.SceneObject</c> / <c>HandleBase.Release</c> /
    /// <c>SceneHandle.UnloadAsync</c> 与 <c>UnloadSceneOperation</c>）——<b>改它之前请先读那几处</b>，
    /// 别照直觉改：语义细节（如「卸载成功会自动释放引用计数」）在直觉里都是反的。</para>
    /// <para>资源包由外部解析好后经委托注入：本类不持包注册表，「包名的唯一真相」仍留在
    /// <see cref="YooAssetManagerImpl"/>。</para>
    /// </summary>
    internal sealed class YooAssetSceneLoadSource : ISceneLoadSource
    {
        #region Private Fields

        private readonly Func<ResourcePackage> _packageProvider;

        #endregion

        #region Lifecycle

        /// <param name="packageProvider">取当前主包；返回 <c>null</c> 表示包不可用。</param>
        internal YooAssetSceneLoadSource(Func<ResourcePackage> packageProvider)
        {
            _packageProvider = packageProvider ?? throw new ArgumentNullException(nameof(packageProvider));
        }

        #endregion

        #region ISceneLoadSource

        public ISceneLoadOperation LoadScene(string location, LoadSceneMode mode)
        {
            var package = _packageProvider();
            if (package == null) return null;
            return new YooAssetSceneLoadOperation(package.LoadSceneAsync(location, mode));
        }

        #endregion

        #region Internal — YooAsset 适配器

        /// <summary>
        /// 包住一个 YooAsset <c>SceneHandle</c> 的操作适配器。
        /// <para>句柄一旦交出（<see cref="Release"/> 或 <see cref="UnloadAsync"/>）就置空，重复调用即空操作。</para>
        /// </summary>
        private sealed class YooAssetSceneLoadOperation : ISceneLoadOperation
        {
            private YooAssetSceneHandle _handle;

            internal YooAssetSceneLoadOperation(YooAssetSceneHandle handle)
            {
                _handle = handle;
            }

            /// <summary>句柄已交出的操作视为已结清——继续报「未完成」会让轮询永远转下去。</summary>
            public bool IsDone => _handle?.IsDone ?? true;

            public bool IsSucceed => _handle != null && _handle.Status == EOperationStatus.Succeed;

            public string LastError => _handle?.LastError ?? string.Empty;

            public float Progress => _handle?.Progress ?? 0f;

            public Scene SceneObject => _handle?.SceneObject ?? default;

            public void Release()
            {
                var handle = _handle;
                _handle = null;
                handle?.Release();
            }

            public async UniTask<bool> UnloadAsync(CancellationToken cancellationToken)
            {
                var handle = _handle;
                if (handle == null) return false;
                _handle = null;

                var operation = handle.UnloadAsync();
                try
                {
                    await operation.WithCancellation(cancellationToken);
                }
                finally
                {
                    // 走到这里只有两种可能：① 卸载已结清——成功时 YooAsset 自动释放引用计数，
                    // 失败时（场景已失效 / 引擎拒绝 / 句柄无效）它不会释放，这里补一次，
                    // 否则该 provider 的引用计数永久 +1；② 等待被取消而卸载仍在后台跑——由 YooAsset 自己结清。
                    if (operation.IsDone && operation.Status != EOperationStatus.Succeed)
                        handle.Release();
                }

                return operation.Status == EOperationStatus.Succeed;
            }
        }

        #endregion
    }
}
