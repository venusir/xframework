using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XAudio
{
    /// <summary>
    /// 音频资源的加载缝：默认实现走 <c>XAsset</c>，测试注入假实现以便控制加载时序与失败。
    /// <para><b>它是测试缝，不是第二个后端替换点。</b>后端整体替换走 <see cref="IAudioManager"/>
    /// （同 <c>XInput</c> 换 Rewired 的形态）；本接口刻意保持 <c>internal</c>，避免出现「只换资源来源、
    /// 不换播放引擎」这条不完整、也无人验证的中间道路。</para>
    /// <para><b>契约</b>：失败返回 <see cref="AudioClipLease.IsValid"/> 为 <c>false</c> 的租约而不抛
    /// （与 <c>XAsset</c> 的「失败返回 default」一致）；仅取消时抛
    /// <see cref="System.OperationCanceledException"/>。</para>
    /// </summary>
    internal interface IAudioClipLoader
    {
        /// <summary>按地址加载音频资源。</summary>
        /// <param name="location">资源地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>资源租约；失败时 <see cref="AudioClipLease.IsValid"/> 为 <c>false</c>，Dispose 安全。</returns>
        UniTask<AudioClipLease> LoadAsync(string location, CancellationToken cancellationToken);
    }
}
