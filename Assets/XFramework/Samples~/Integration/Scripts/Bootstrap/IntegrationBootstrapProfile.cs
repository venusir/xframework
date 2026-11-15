using UnityEngine;
using XFramework.XAsset;
using XFramework.XInput.Default;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例自己的启动配置资产：框架不提供 Profile 形状，字段由使用方决定。
    /// <para>字段与 <c>BootstrapStages</c> 的构造一一对应；换输入后端的项目把 <c>_inputOptions</c>
    /// 换成自己的 provider 资产即可，其余字段按需增删。装配代码在
    /// <see cref="IntegrationLauncher.ConfigureStages"/>。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "Integration.BootstrapProfile",
                     menuName = "XFramework Samples/Integration Bootstrap Profile")]
    public sealed class IntegrationBootstrapProfile : ScriptableObject
    {
        #region Serialized Fields

        [Tooltip("Asset 主包名；空 = 默认包。")]
        [SerializeField] private string _assetPackageName = "";

        [Tooltip("资源运行模式。")]
        [SerializeField] private AssetPlayMode _assetPlayMode = AssetPlayMode.Offline;

        [Tooltip("低内存时自动回收资源。")]
        [SerializeField] private bool _autoReclaimOnLowMemory = true;

        [Tooltip("存档格式版本上限。")]
        [SerializeField] private int _saveVersion = 1;

        [Tooltip("输入配置资产；留空则不登记 Input 阶段（换后端时也留空，改在启动器里登记自己的 provider）。")]
        [SerializeField] private InputSystemOptionsAsset _inputOptions;

        [Tooltip("默认语言；留空则不登记 Localization 阶段。")]
        [SerializeField] private string _defaultLanguage = "";

        [Tooltip("引导语言表（扁平 JSON 的 TextAsset）；可为空。")]
        [SerializeField] private TextAsset _languageTable;

        #endregion

        #region Public Properties

        /// <summary>Asset 主包名；空字符串表示默认包。</summary>
        public string AssetPackageName => _assetPackageName;

        /// <summary>资源运行模式。</summary>
        public AssetPlayMode PlayMode => _assetPlayMode;

        /// <summary>低内存时是否自动回收资源。</summary>
        public bool AutoReclaimOnLowMemory => _autoReclaimOnLowMemory;

        /// <summary>存档格式版本上限。</summary>
        public int SaveVersion => _saveVersion;

        /// <summary>输入配置资产；null 表示不登记 Input 阶段。</summary>
        public InputSystemOptionsAsset InputOptions => _inputOptions;

        /// <summary>默认语言；空字符串表示不登记 Localization 阶段。</summary>
        public string DefaultLanguage => _defaultLanguage;

        /// <summary>引导语言表；可为 null。</summary>
        public TextAsset LanguageTable => _languageTable;

        #endregion
    }
}
