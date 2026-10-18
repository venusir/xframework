using System;

namespace XFramework.XAsset
{
    /// <summary>
    /// 资源运行模式。框架映射 Offline / Host / EditorSimulate 三种；WebPlayMode 与 CustomPlayMode 不在支持范围内。
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

        /// <summary>
        /// 编辑器模拟模式。资源直接经 <c>AssetDatabase</c> 加载（虚拟 bundle），无需先构建资源包；
        /// 初始化时按包名执行一次模拟构建。
        /// <para><b>仅 Unity 编辑器可用</b>；打包后的运行时传此值会抛
        /// <see cref="PlatformNotSupportedException"/>。Player 构建请使用 Offline 或 Host。</para>
        /// </summary>
        EditorSimulate = 2,
    }

    /// <summary>
    /// 资源包初始化配置。传给 <see cref="AssetManager.InitializeAsync"/> 或 <see cref="AssetManager.InitializePackageAsync"/>。
    /// <para>为 null 时使用默认配置：默认包名 + 离线模式。</para>
    /// </summary>
    public sealed class AssetInitOptions
    {
        /// <summary>
        /// 主包名。<b>它同时决定「初始化哪个包」与「加载族作用于哪个包」</b>——加载（<c>LoadAsync</c> /
        /// <c>InstantiateAsync</c> / 预载 / 场景）不带包名，统一作用于主包，故两者必须是同一个名字。
        /// null 或空白时用默认包 <c>DefaultPackage</c>（须与 YooAsset 构建侧的包名一致）。
        /// <para>额外包请走 <c>InitializePackageAsync</c>，它不会改变主包。</para>
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 运行模式，默认离线。
        /// </summary>
        public AssetPlayMode PlayMode = AssetPlayMode.Offline;

        /// <summary>
        /// 远端地址服务。HostPlayMode 必填；Offline 与 EditorSimulate 忽略。
        /// </summary>
        public IAssetRemoteServices RemoteServices;

        /// <summary>
        /// 低内存自动回收。为 true 时监听 <see cref="UnityEngine.Application.lowMemory"/>，自动释放对象池闲置实例
        /// 并卸载所有包中未使用的资源（引用计数为 0）。默认 true。
        /// <para>关闭场景：接入方自行管理回收策略（如对低内存事件敏感的机型、自管资源上限的项目）。</para>
        /// </summary>
        public bool AutoReclaimOnLowMemory = true;

        /// <summary>
        /// 解密服务；为 <c>null</c> 时资源不加密（默认）。非空时必须与打包侧的加密方式对应
        /// （见 <see cref="IAssetDecryptionServices"/>）。
        /// <para><b>Offline 与 Host 两种模式都会接线</b>——加密项目的单机 / 热更模式同样要能读。</para>
        /// <para><b>EditorSimulate 忽略该字段</b>：模拟模式直接读取原始资源，不经过加密 bundle，
        /// 因此无法用它验证加密链路。</para>
        /// </summary>
        public IAssetDecryptionServices DecryptionServices;
    }
}
