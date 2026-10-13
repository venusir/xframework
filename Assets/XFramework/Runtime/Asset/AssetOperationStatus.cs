using YooAsset;

namespace XFramework.XAsset
{
    /// <summary>
    /// 资源异步操作的状态。四个句柄的 <c>Status</c> 成员（<see cref="AssetHandle{T}"/> /
    /// <see cref="RawFileHandle"/> / <see cref="SubAssetsHandle"/> / <see cref="AssetDownloaderHandle"/>）
    /// 都返回它。
    /// <para><b>为什么不用 YooAsset 的 <c>EOperationStatus</c></b>：本包对后端的约定是**公开面不出现
    /// YooAsset 类型**（同 <see cref="IAssetRemoteServices"/> / <see cref="IAssetDecryptionServices"/>
    /// 的包法，第三方接口变更由 internal 适配器吸收）。直接透出上游枚举会把我们的公开面绑在它的类型上——
    /// 上游一旦挪动该类型（YooAsset 3.x 就把一批类型移进了默认不编译的兼容层），本包即被动破坏。</para>
    /// <para>四个成员与上游<b>一一对应</b>，所以迁移是机械的：把 <c>EOperationStatus</c> 换成
    /// <c>AssetOperationStatus</c> 即可，语义与取值时机完全不变。</para>
    /// </summary>
    public enum AssetOperationStatus
    {
        /// <summary>尚未开始。无效句柄（<c>default</c>）也报这个值。</summary>
        None = 0,

        /// <summary>进行中。</summary>
        Processing = 1,

        /// <summary>已成功完成。</summary>
        Succeed = 2,

        /// <summary>已失败。失败原因见句柄的 <c>Error</c> / <c>LastError</c>。</summary>
        Failed = 3,
    }

    /// <summary>
    /// <see cref="AssetOperationStatus"/> 与 YooAsset <c>EOperationStatus</c> 的**唯一**换算实现
    /// （四个句柄共用；映射写两遍就会漂移）。internal——换算只发生在框架内部，不是使用方接口。
    /// </summary>
    internal static class AssetOperationStatusMapper
    {
        /// <summary>
        /// 换算上游状态。<b>未知值一律回落 <see cref="AssetOperationStatus.None"/></b>：上游将来新增成员时，
        /// 我们宁可报「尚未开始」也不要误报成「已成功」（回落成 Succeed 会让失败被当成成功）。
        /// 用 <c>switch</c> 而非按整型值强转，是为了不依赖上游的枚举编号。
        /// </summary>
        internal static AssetOperationStatus From(EOperationStatus status)
        {
            switch (status)
            {
                case EOperationStatus.Processing: return AssetOperationStatus.Processing;
                case EOperationStatus.Succeed: return AssetOperationStatus.Succeed;
                case EOperationStatus.Failed: return AssetOperationStatus.Failed;
                default: return AssetOperationStatus.None;
            }
        }
    }
}
