namespace XFramework.XAudio
{
    /// <summary>
    /// 播放引擎的<b>snapshot 能力</b>。可选能力接口：provider 不实现时，
    /// <see cref="AudioManager.StartSnapshot(string)"/> 等方法返回 <c>false</c>。
    /// <para><b>官方扩展能力接口</b>：本类型为 internal，经 <c>InternalsVisibleTo</c> 提供给官方 FMOD
    /// 扩展程序集；它不是第三方可自由实现的公开契约。</para>
    /// <para><b>线程</b>：全部成员限主线程调用。</para>
    /// </summary>
    internal interface IAudioSnapshotProvider
    {
        /// <summary>启动一个 snapshot。</summary>
        bool TryStartSnapshot(string snapshot);

        /// <summary>停止一个 snapshot。</summary>
        bool TryStopSnapshot(string snapshot);
    }
}
