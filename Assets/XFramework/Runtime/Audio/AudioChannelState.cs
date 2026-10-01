namespace XFramework.XAudio
{
    /// <summary>
    /// 一个通道的音量状态。
    /// <para><b>惰性创建</b>：未声明的通道在第一次被<b>写入</b>时按默认值建状态（音量 <c>1</c>、不静音）；
    /// 查询类成员（<c>GetChannelVolume</c> / <c>IsChannelMuted</c>）不创建状态，未知通道直接返回默认值。
    /// 这与 <c>PoolManager</c> 的「首次 <c>Get&lt;T&gt;()</c> 自动建池」是同一取舍——只读取的人不该留下副作用。</para>
    /// <para>通道名以<b>序数（Ordinal）</b>比较，区分大小写：<c>"BGM"</c> 与 <c>"bgm"</c> 是两个通道。</para>
    /// </summary>
    internal sealed class AudioChannelState
    {
        /// <summary>通道音量，线性值，恒在 <c>[0, 1]</c>。</summary>
        internal float Volume = 1f;

        /// <summary>通道是否静音。静音时该通道下所有播放的实际音量按 <c>0</c> 处理，但不停止播放。</summary>
        internal bool Muted;
    }
}
