namespace XFramework.XAudio
{
    /// <summary>
    /// 通道的初始配置，供 <see cref="AudioManager.RegisterChannel"/> 声明。
    /// <para>通道也可以不声明——未声明的通道首次使用时按默认值惰性建状态（音量为 <c>1</c>、不静音）。
    /// 声明的意义只在于「让某个通道一开始就带着指定的音量与静音状态」，省掉初始化时的一串设置调用。</para>
    /// <para>本类是可变的普通对象（非句柄）：<see cref="AudioManager.RegisterChannel"/> 会在调用时
    /// <b>读取一次</b>并快照，之后再改这个实例不会影响已注册的通道。</para>
    /// </summary>
    public sealed class AudioChannelConfig
    {
        /// <summary>
        /// 通道音量，线性值。由 <see cref="AudioManager.SetChannelVolume"/> 钳到 <c>[0, 1]</c>。
        /// </summary>
        public float Volume = 1f;

        /// <summary>通道是否静音。静音时该通道下所有播放的实际音量按 <c>0</c> 处理（但不停止播放）。</summary>
        public bool Muted;
    }
}
