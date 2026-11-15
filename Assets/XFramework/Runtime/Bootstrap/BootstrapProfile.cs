using UnityEngine;
using XFramework.XAsset;
using XFramework.XInput.Default;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 启动配置资产：装 <see cref="DefaultGameLauncher"/> 原先的 7 个数据字段。
    /// <para><b>只装数据</b>：Asset 包名 / 运行模式 / 低内存回收、存档版本、输入配置资产、默认语言与语言表。
    /// 服务实例（<c>RemoteServices</c> / <c>CryptoProvider</c> / 自定义 provider）是代码对象，Unity 反序列化不了，
    /// 仍走启动器子类的 <c>ConfigureStages</c> 覆写——<b>Profile 不是配置容器，也不是统一入口</b>。</para>
    /// <para><b>零配置不受影响</b>：不给 <see cref="DefaultGameLauncher"/> 赋 Profile 时，登记表与旧行为一致
    /// （默认三件 + UI Root 非空时的 UI 阶段）。</para>
    /// <para><b>迁移</b>：旧场景里 7 个字段的值不会自动搬进 Profile——新建本资产、复制旧值、赋给启动器的
    /// <c>_profile</c>。字段重命名时请加 <c>[FormerlySerializedAs]</c>，否则旧资产会静默变回默认值。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "XFramework/Bootstrap Profile", fileName = "BootstrapProfile")]
    public sealed class BootstrapProfile : ScriptableObject
    {
        #region Serialized Fields

        [Header("Asset")]
        [Tooltip("主包名；空 = 默认包 DefaultPackage（须与 YooAsset 构建侧的包名一致）。")]
        [SerializeField] private string _assetPackageName = "";

        [Tooltip("资源运行模式：Offline 内嵌、无热更；Host 支持版本检查与下载（需在引导代码里" +
                 "给 AssetInitOptions 注入 RemoteServices，否则启动抛 ArgumentException）；" +
                 "EditorSimulate 仅编辑器可用（资源直接走 AssetDatabase，无需打包；" +
                 "Player 构建请改用 Offline 或 Host）。")]
        [SerializeField] private AssetPlayMode _assetPlayMode = AssetPlayMode.Offline;

        [Tooltip("低内存时自动回收：释放对象池闲置实例并卸载未引用资源。")]
        [SerializeField] private bool _autoReclaimOnLowMemory = true;

        [Header("Save")]
        [Tooltip("当前客户端支持的存档格式版本上限（发布新存档格式时递增）。")]
        [SerializeField] private int _saveVersion = 1;

        [Header("Input")]
        [Tooltip("默认后端（Unity Input System）的配置资产：输入资产 + 初始 ActionMap 名。" +
                 "留空则不登记 Input 引导阶段。换后端（Rewired 等）请改继承 GameLauncher 底座。")]
        [SerializeField] private InputSystemOptionsAsset _inputOptions;

        [Header("Localization")]
        [Tooltip("默认语言标识（如 zh_Hans / en）。留空则不登记 Localization 引导阶段。")]
        [SerializeField] private string _defaultLanguage = "";

        [Tooltip("语言表（扁平 JSON 的 TextAsset，与资源地址加载同一种格式）。" +
                 "可空——此时该阶段打一条警告后跳过初始化。")]
        [SerializeField] private TextAsset _languageTable;

        #endregion

        #region Internal Accessors

        /// <summary>主包名；空串表示默认包。</summary>
        internal string AssetPackageName => _assetPackageName;

        /// <summary>资源运行模式。</summary>
        internal AssetPlayMode PlayMode => _assetPlayMode;

        /// <summary>低内存时是否自动回收。</summary>
        internal bool AutoReclaimOnLowMemory => _autoReclaimOnLowMemory;

        /// <summary>存档格式版本上限。</summary>
        internal int SaveVersion => _saveVersion;

        /// <summary>输入配置资产；null 表示不登记 Input 阶段。</summary>
        internal InputSystemOptionsAsset InputOptions => _inputOptions;

        /// <summary>默认语言；空串表示不登记 Localization 阶段。</summary>
        internal string DefaultLanguage => _defaultLanguage;

        /// <summary>语言表资产；可为 null。</summary>
        internal TextAsset LanguageTable => _languageTable;

        #endregion
    }
}
