namespace XFramework.XAsset
{
    /// <summary>
    /// 远端资源地址服务。供 HostPlayMode 使用,提供资源文件的主/备用下载地址。
    /// <para>实现方通常基于 CDN 配置或运行时下发的地图文件返回 URL。</para>
    /// </summary>
    public interface IAssetRemoteServices
    {
        /// <summary>
        /// 获取文件的主下载地址。
        /// </summary>
        string GetRemoteMainURL(string fileName);

        /// <summary>
        /// 获取文件的备用下载地址。主地址失败时使用。
        /// </summary>
        string GetRemoteFallbackURL(string fileName);
    }
}
