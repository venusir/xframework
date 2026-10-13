using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace XFramework.XAsset
{
    /// <summary>
    /// 场景加载与卸载能力。
    /// <para><b>与 <see cref="IAssetManager"/> 分开是刻意的</b>：不扩已有接口，故不会破坏第三方自定义的
    /// <see cref="IAssetManager"/> 实现；需要该能力的调用方按能力接口索取，门面负责能力探测
    /// （形状同 <see cref="IAssetPoolController"/>）。</para>
    /// <para><b>为什么场景单独成一项能力</b>：场景的存活是 YooAsset <c>SceneHandle</c> 的引用计数，
    /// 只有持有后端的一侧管得了；把它关在这份可选契约里，将来要补 <c>physicsMode</c> / <c>suspendLoad</c>
    /// 之类的参数时也不必再动主接口一次。</para>
    /// <para>门面入口见 <see cref="AssetManager.LoadSceneAsync(string, LoadSceneMode, Action{float}, CancellationToken)"/>
    /// 与 <see cref="AssetManager.UnloadSceneAsync"/>。</para>
    /// </summary>
    public interface IAssetSceneLoader
    {
        /// <summary>
        /// 异步加载场景，返回可卸载的句柄。
        /// <para><b>失败返回 <c>default(AssetSceneHandle)</c> 而不抛</b>（同 <see cref="IAssetManager"/> 的失败口径）；
        /// 取消抛 <see cref="OperationCanceledException"/>。</para>
        /// <para>取消<b>不中止</b>底层加载（YooAsset 的场景加载没有对外可用的中止入口），善后由实现承担。</para>
        /// </summary>
        /// <param name="location">场景资源地址。</param>
        /// <param name="mode">单场景（默认）或叠加。</param>
        /// <param name="progress">进度上报（0~1）；成功时以 <c>1f</c> 收尾，失败路径不补发。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask<AssetSceneHandle> LoadSceneAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, Action<float> progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 卸载句柄对应的场景。
        /// <para>以下情况返回 <c>false</c> 而不抛：句柄无效（<c>default</c>）、句柄与实现方登记的加载对不上、
        /// 场景已被别人卸载、它是当前唯一的场景（卸载它会让工程没有任何场景，引擎自己也会拒绝）。</para>
        /// </summary>
        /// <param name="handle">加载时拿到的句柄。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask<bool> UnloadSceneAsync(AssetSceneHandle handle, CancellationToken cancellationToken = default);
    }
}
