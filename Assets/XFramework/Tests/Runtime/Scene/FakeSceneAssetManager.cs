using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using XFramework.XAsset;
using XFramework.XAsset.Tests;

namespace XFramework.XScene.Tests
{
    /// <summary>
    /// <see cref="IAssetSceneLoader"/> 的假件：只让本模块用到的成员可编程，其余一律继承自
    /// <see cref="FakeAssetManager"/>（对未用到的成员抛 <c>NotSupportedException</c>）。
    /// <para><b>为什么派生而不是另写一份</b>：<c>IAssetManager</c> 有几十个成员，本模块一个都不用；
    /// Editor 树里 <c>PoolCapableAssetManager</c> 与 <c>FakeAssetManager</c> 就是这种「基类 + 能力派生」的形态，
    /// 两棵树因此对称。基类刻意**不**实现 <see cref="IAssetSceneLoader"/>——「能力缺失」那条分支正需要它。</para>
    /// </summary>
    internal sealed class FakeSceneAssetManager : FakeAssetManager, IAssetSceneLoader
    {
        #region Programmable State

        /// <summary>场景加载调用次数。</summary>
        public int SceneLoadCallCount;

        /// <summary>最近一次场景加载的地址。</summary>
        public string LastSceneLocation;

        /// <summary>最近一次场景加载的模式。</summary>
        public LoadSceneMode LastSceneMode;

        /// <summary>最近一次收到的进度回调（用例可手动触发它，断言「真的接上了」）。</summary>
        public Action<float> LastSceneProgress;

        /// <summary>场景加载的返回值（用例可改）。</summary>
        public AssetSceneHandle SceneLoadResult;

        /// <summary>加载前是否先等一帧——让「取消」与「跨帧」可测。</summary>
        public bool DelayOneFrame;

        /// <summary>场景卸载调用次数。</summary>
        public int SceneUnloadCallCount;

        /// <summary>最近一次收到的卸载句柄。</summary>
        public AssetSceneHandle LastUnloadedHandle;

        /// <summary>场景卸载的返回值。</summary>
        public bool SceneUnloadResult = true;

        /// <summary>资源回收（<c>UnloadUnusedAssetsAsync</c>）调用次数——<c>UnloadUnusedAfterLoad</c> 的观测点。</summary>
        public int UnloadUnusedCallCount;

        /// <summary>最近一次回收调用收到的令牌——断言「回收段是否响应调用方取消」。</summary>
        public CancellationToken LastUnloadUnusedToken;

        /// <summary>回收前等待的门闩（用例控制「加载成功后、回收在途」的时刻）。</summary>
        public UniTaskCompletionSource Gate;

        #endregion

        #region IAssetSceneLoader

        public async UniTask<AssetSceneHandle> LoadSceneAsync(string location,
            LoadSceneMode mode = LoadSceneMode.Single,
            Action<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            SceneLoadCallCount++;
            LastSceneLocation = location;
            LastSceneMode = mode;
            LastSceneProgress = progress;

            // 取消只中止等待（与真实现同语义：YooAsset 的场景加载中止不了）
            if (DelayOneFrame)
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            return SceneLoadResult;
        }

        public UniTask<bool> UnloadSceneAsync(AssetSceneHandle handle, CancellationToken cancellationToken = default)
        {
            SceneUnloadCallCount++;
            LastUnloadedHandle = handle;
            return UniTask.FromResult(SceneUnloadResult);
        }

        #endregion

        #region Overrides

        public override async UniTask UnloadUnusedAssetsAsync(string packageName = null, CancellationToken cancellationToken = default)
        {
            UnloadUnusedCallCount++;
            LastUnloadUnusedToken = cancellationToken;

            if (Gate != null)
                await Gate.Task;

            // 显式检查令牌：不依赖 UniTask.Yield 的取消语义，直接回答「门面传的是哪个令牌」
            cancellationToken.ThrowIfCancellationRequested();
        }

        #endregion
    }
}
