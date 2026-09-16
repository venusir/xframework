namespace XFramework.XAsset
{
    /// <summary>
    /// 资源实例池的受控清理能力。
    /// <para><b>与 <see cref="IAssetManager"/> 分开是刻意的</b>：不扩已有接口，故不会破坏第三方
    /// 自定义的 <see cref="IAssetManager"/> 实现；需要该能力的调用方按能力接口索取，门面负责能力探测。</para>
    /// <para><b>为什么需要它</b>：回池时实例会保留 <c>AssetHandle</c> 以保活资源，于是池里只要还留着
    /// 一个闲置实例，该预制体的引用计数就不会归零，<c>UnloadUnusedAssetsAsync</c> 也就回收不掉它。
    /// 想真正释放内存，必须先清池。</para>
    /// </summary>
    public interface IAssetPoolController
    {
        /// <summary>
        /// 销毁指定地址的全部闲置池实例（不影响正在使用的实例）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>实际销毁的实例数；该地址没有池时返回 0。</returns>
        int ClearPool(string location);

        /// <summary>
        /// 销毁全部闲置池实例（不影响正在使用的实例）。
        /// </summary>
        /// <returns>实际销毁的实例总数。</returns>
        int ClearAllPools();
    }
}
