using UnityEngine;

namespace XFramework.XAudio
{
    /// <summary>
    /// 音频模块的隐藏宿主。所有池化播放源都是它的子节点。
    /// <para><b>为什么需要一个 MonoBehaviour</b>：Unity 的播放源必须挂在 GameObject 上。宿主是整个模块
    /// 唯一的自持对象，随首个播放惰性创建、随 <c>Destroy</c> 销毁——从不播放的项目一个对象都不会建。</para>
    /// <para><b>为什么 <c>DontDestroyOnLoad</c></b>：BGM 必须跨场景连续，播放源若随场景卸载被销毁，
    /// 音乐会在切场景时断掉。</para>
    /// <para>本类刻意不含任何逻辑：它只是一个挂载点。</para>
    /// </summary>
    internal sealed class AudioHost : MonoBehaviour
    {
    }
}
