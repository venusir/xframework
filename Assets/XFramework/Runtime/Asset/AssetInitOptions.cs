using System;

namespace XFramework.XAsset
{
    /// <summary>
    /// 资源运行模式。
    /// </summary>
    public enum AssetPlayMode
    {
        /// <summary>
        /// 离线模式。资源全部内嵌（StreamingAssets），无热更。
        /// </summary>
        Offline = 0,

        /// <summary>
        /// 热更模式。内置包 + 远端资源站，支持版本检查与下载更新。
        /// </summary>
        Host = 1,
    }

    /// <summary>
    /// 资源包初始化配置。传给 <see cref="AssetManager.InitializeAsync"/> 或 <see cref="AssetManager.InitializePackageAsync"/>。
    /// <para>为 null 时使用默认配置：默认包名 + 离线模式。</para>
    /// </summary>
    public sealed class AssetInitOptions
    {
        /// <summary>
        /// 资源包名。null 或空白时使用默认包（DefaultPackage）。
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 运行模式，默认离线。
        /// </summary>
        public AssetPlayMode PlayMode = AssetPlayMode.Offline;

        /// <summary>
        /// 远端地址服务。HostPlayMode 必填，Offline 忽略。
        /// </summary>
        public IAssetRemoteServices RemoteServices;
    }
}
