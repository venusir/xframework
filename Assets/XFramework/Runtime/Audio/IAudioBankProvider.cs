namespace XFramework.XAudio
{
    /// <summary>
    /// 播放引擎的<b>bank 能力</b>。可选能力接口：provider 不实现时，
    /// <see cref="AudioManager.LoadBank(string, bool)"/> 等方法返回 <c>false</c>。
    /// <para><b>官方扩展能力接口</b>：本类型为 internal，经 <c>InternalsVisibleTo</c> 提供给官方 FMOD
    /// 扩展程序集；它不是第三方可自由实现的公开契约。</para>
    /// <para><b>线程</b>：全部成员限主线程调用；加载可能是同步阻塞的。</para>
    /// </summary>
    internal interface IAudioBankProvider
    {
        /// <summary>同步加载一个 bank；<paramref name="loadSamples"/> 的含义由 provider 定义。</summary>
        bool TryLoadBank(string bank, bool loadSamples);

        /// <summary>卸载一个 bank。</summary>
        bool TryUnloadBank(string bank);

        /// <summary>查询 bank 是否已加载。</summary>
        bool IsBankLoaded(string bank);
    }
}
