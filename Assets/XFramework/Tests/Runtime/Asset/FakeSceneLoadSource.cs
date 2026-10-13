using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using XFramework.XAsset;

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// <see cref="ISceneLoadSource"/> 的假件：把「何时完成 / 成败 / 是否响应取消」交给用例掌控。
    /// <para><b>它模拟 YooAsset 2.3.18 的哪几条实测语义</b>（本仓对 <c>ModuleAudit</c> 判据 D3
    /// 「测试的假绿」的纪律：假件必须写明自己模拟的是哪几条真语义，否则语义漂移无人发现）：</para>
    /// <list type="number">
    /// <item>加载失败不产生场景（真实现里 <c>SceneObject</c> 返回 <c>default</c>）；</item>
    /// <item><c>UnloadAsync</c> 会把场景对象一并销毁（真实现走 <c>SceneManager.UnloadSceneAsync</c>）；</item>
    /// <item>一旦发起就<b>无法中止</b>——所以「取消」在本假件里同样只是让等待方提前退出，
    /// 操作仍会被用例推向完成。</item>
    /// </list>
    /// </summary>
    internal sealed class FakeSceneLoadSource : ISceneLoadSource
    {
        /// <summary>最近一次 <see cref="LoadScene"/> 的地址。</summary>
        public string LastLocation;

        /// <summary>最近一次 <see cref="LoadScene"/> 的模式。</summary>
        public LoadSceneMode LastMode;

        /// <summary><see cref="LoadScene"/> 的调用次数。</summary>
        public int LoadCount;

        /// <summary>下一次加载返回的操作；为 <c>null</c> 时模拟「资源包不可用」。</summary>
        public FakeSceneLoadOperation NextOperation;

        public ISceneLoadOperation LoadScene(string location, LoadSceneMode mode)
        {
            LoadCount++;
            LastLocation = location;
            LastMode = mode;
            return NextOperation;
        }
    }

    /// <summary>可编程的场景加载操作假件。</summary>
    internal sealed class FakeSceneLoadOperation : ISceneLoadOperation
    {
        public bool IsDone { get; set; }

        public bool IsSucceed { get; set; }

        public string LastError { get; set; } = string.Empty;

        public float Progress { get; set; }

        public Scene SceneObject { get; set; }

        /// <summary><see cref="Release"/> 的调用次数。</summary>
        public int ReleaseCount { get; private set; }

        /// <summary><see cref="UnloadAsync"/> 的调用次数。</summary>
        public int UnloadCount { get; private set; }

        /// <summary><see cref="UnloadAsync"/> 的返回值。</summary>
        public bool UnloadResult { get; set; } = true;

        /// <summary>卸载前等待的帧数（模拟真实卸载跨帧完成）。</summary>
        public int UnloadDelayFrames { get; set; }

        public void Release() => ReleaseCount++;

        public async UniTask<bool> UnloadAsync(CancellationToken cancellationToken)
        {
            UnloadCount++;
            for (int i = 0; i < UnloadDelayFrames; i++)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            SceneObject = default;
            return UnloadResult;
        }
    }
}
