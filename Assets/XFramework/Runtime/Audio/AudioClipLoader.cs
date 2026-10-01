using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XAudio
{
    /// <summary>
    /// 默认的音频资源加载缝：经 <c>XAsset</c> 加载，并把结果包成 <see cref="AudioClipLease"/>。
    /// <para>之所以值得单独一层，是因为它是本模块唯一的资源来源——公开面只接受 location 字符串，
    /// 中间件实现可以把 location 解释成自己的事件名，而测试可以在这里注入时序与失败。</para>
    /// <para>本文件与 <see cref="AudioClipLease"/> 是本模块<b>唯一</b>接触 <c>XAsset</c> 类型的两处。</para>
    /// </summary>
    internal sealed class AudioClipLoader : IAudioClipLoader
    {
        /// <inheritdoc/>
        public async UniTask<AudioClipLease> LoadAsync(string location, CancellationToken cancellationToken)
        {
            var handle = await AssetManager.LoadAsync<AudioClip>(location, cancellationToken);
            return new AudioClipLease(handle);
        }
    }
}
