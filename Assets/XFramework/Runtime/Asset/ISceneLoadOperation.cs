using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace XFramework.XAsset
{
    /// <summary>
    /// 一次场景加载操作的最小面：YooAsset <c>SceneHandle</c> 在框架侧的唯一抽象。
    /// <para><b>为什么有它</b>：场景加载的轮询、失败释放、取消善后都是本模块必须保住的逻辑，而
    /// Runtime 测试程序集不引用 YooAsset（<c>Tests/Runtime</c> 的 asmdef 里没有它），测试环境也没有
    /// 可运行的资源包——真路径在用例里跑不起来。本接口就是那条缝：<b>缝之上</b>是被用例覆盖的框架逻辑，
    /// <b>缝之下</b>是 <see cref="YooAssetSceneLoadSource"/> 里逐条对着 YooAsset 源码写成的机械转发。</para>
    /// <para><b>它是测试缝，不是第二个后端替换点</b>：后端整体替换走 <see cref="IAssetManager"/>。
    /// 刻意保持 <c>internal</c>，避免出现「只换场景来源、不换资源后端」这条不完整也无人验证的中间道路
    /// （同 <c>IAudioClipLoader</c> 的取舍）。</para>
    /// </summary>
    internal interface ISceneLoadOperation
    {
        /// <summary>是否已完成（成功或失败都算）。</summary>
        bool IsDone { get; }

        /// <summary>是否加载成功。</summary>
        bool IsSucceed { get; }

        /// <summary>失败原因；成功时为空串。</summary>
        string LastError { get; }

        /// <summary>加载进度（0~1）。</summary>
        float Progress { get; }

        /// <summary>加载完成的场景；未完成或失败时为 <c>default(Scene)</c>。</summary>
        Scene SceneObject { get; }

        /// <summary>
        /// 释放句柄，<b>不卸载场景</b>。
        /// <para>只许用在「这次加载没有留下活着的场景」的路径上（失败、或场景已被别人卸载）。
        /// <b>场景仍在场时不得调用</b>：释放会把引用计数降到 0，此后一次
        /// <c>AssetManager.UnloadUnusedAssetsAsync()</c> 就能回收掉该场景仍在使用中的 bundle。</para>
        /// <para>对已释放 / 已销毁的句柄安全。</para>
        /// </summary>
        void Release();

        /// <summary>
        /// 卸载场景并随之释放句柄（YooAsset 的语义：卸载成功后自动释放该 handle 的引用计数）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。<b>取消只中止等待，不中止已经发起的卸载</b>——
        /// YooAsset 的卸载一旦开始就没有对外可用的中止入口。</param>
        /// <returns>卸载成功为 <c>true</c>；句柄失效 / 场景已不在场 / 引擎拒绝时为 <c>false</c>。</returns>
        UniTask<bool> UnloadAsync(CancellationToken cancellationToken);
    }
}
