namespace XFramework.XAudio
{
    /// <summary>
    /// 播放引擎的<b>逐次参数能力</b>（事件参数 / RTPC）。可选能力接口：provider 不实现时，
    /// <see cref="AudioManager.SetParameter(AudioHandle, string, float)"/> 等方法返回 <c>false</c>。
    /// <para><b>官方扩展能力接口</b>：本类型为 internal，经 <c>InternalsVisibleTo</c> 提供给官方 FMOD
    /// 扩展程序集；它不是第三方可自由实现的公开契约。第三方 provider 仍只实现
    /// <see cref="IAudioProvider"/>，逐次控制可经 <see cref="AudioManager.TryGetProviderHandle"/> 自行处理。</para>
    /// <para><b>线程</b>：全部成员限主线程调用。</para>
    /// </summary>
    internal interface IAudioParameterProvider
    {
        /// <summary>
        /// 写入一个逐次播放参数。返回 <c>false</c> 表示句柄已失效或参数未被接受。
        /// </summary>
        /// <param name="handle">provider 句柄。</param>
        /// <param name="parameter">参数（数值或标签）。</param>
        /// <returns>是否已应用。</returns>
        bool TrySetParameter(AudioProviderHandle handle, AudioParameter parameter);

        /// <summary>
        /// 写入一个全局参数（不绑定具体播放）。返回 <c>false</c> 表示参数未被接受。
        /// </summary>
        /// <param name="parameter">参数（数值或标签）。</param>
        /// <returns>是否已应用。</returns>
        bool TrySetGlobalParameter(AudioParameter parameter);
    }
}
