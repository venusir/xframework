namespace XFramework.XAudio
{
    /// <summary>
    /// 播放引擎的<b>bus 能力</b>。可选能力接口：provider 不实现时，
    /// <see cref="AudioManager.SetBusVolume(string, float)"/> 等方法返回 <c>false</c>。
    /// <para><b>官方扩展能力接口</b>：本类型为 internal，经 <c>InternalsVisibleTo</c> 提供给官方 FMOD
    /// 扩展程序集；它不是第三方可自由实现的公开契约。</para>
    /// <para><b>线程</b>：全部成员限主线程调用。</para>
    /// </summary>
    internal interface IAudioBusProvider
    {
        /// <summary>设置 bus 音量；核心原样透传，不做钳制。</summary>
        bool TrySetBusVolume(string bus, float volume);

        /// <summary>设置 bus 静音。</summary>
        bool TrySetBusMuted(string bus, bool muted);

        /// <summary>暂停或恢复 bus。</summary>
        bool TrySetBusPaused(string bus, bool paused);
    }
}
