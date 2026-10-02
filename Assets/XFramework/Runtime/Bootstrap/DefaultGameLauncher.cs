using UnityEngine;
using XFramework.XAsset;
using XFramework.XInput;
using XFramework.XInput.Default;
using XFramework.XLocalization;
using XFramework.XSave;
using XFramework.XUI;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 开箱默认的启动器：把 <see cref="GameLauncher"/> 的生命周期底座接到<b>一组 Inspector 配置字段</b>上。
    /// <para>挂到场景里即可——<c>Awake</c> 里按字段组装各模块的 options 并登记引导阶段，
    /// <b>字段全为默认或留空时与零配置完全一致</b>。</para>
    /// <para><b>三种用法</b>：① 直接挂本组件（零配置起步）；② 填字段（Asset / Save / UI / Input / Localization
    /// 五组）；③ 继承本类覆写 <see cref="ConfigureStages"/>——在字段面之上追加或替换（服务实例类配置从这里进）。
    /// <b>要完全自控</b>（Inspector 里一个字段都不要）请改继承 <see cref="GameLauncher"/> 并实现
    /// <see cref="ConfigureStages"/>。</para>
    /// </summary>
    public class DefaultGameLauncher : GameLauncher
    {
        #region Configuration — Asset

        [Header("Asset")]
        [Tooltip("主包名；空 = 默认包 DefaultPackage（须与 YooAsset 构建侧的包名一致）。")]
        [SerializeField] private string _assetPackageName = "";

        [Tooltip("资源运行模式：Offline 内嵌、无热更；Host 支持版本检查与下载。")]
        [SerializeField] private AssetPlayMode _assetPlayMode = AssetPlayMode.Offline;

        [Tooltip("低内存时自动回收：释放对象池闲置实例并卸载未引用资源。")]
        [SerializeField] private bool _autoReclaimOnLowMemory = true;

        #endregion

        #region Configuration — Save

        [Header("Save")]
        [Tooltip("当前客户端支持的存档格式版本上限（发布新存档格式时递增）。")]
        [SerializeField] private int _saveVersion = 1;

        #endregion

        #region Configuration — UI

        [Header("UI")]
        [Tooltip("UI 根节点（场景里的 Canvas 根）。留空则不登记 UI 引导阶段——" +
                 "用 UIRootNode 自动初始化，或自行调 UIManager.Initialize。")]
        [SerializeField] private Transform _uiRoot;

        #endregion

        #region Configuration — Input

        [Header("Input")]
        [Tooltip("默认后端（Unity Input System）的配置资产：输入资产 + 初始 ActionMap 名。" +
                 "留空则不登记 Input 引导阶段——自行调 InputManager.Initialize。" +
                 "换后端（Rewired 等）请改继承 GameLauncher 底座并只实现 ConfigureStages。" +
                 "（字段是框架类型而非 InputActionAsset：Unity 输入类型按模块边界只允许住在 XInput.Default 内。）")]
        [SerializeField] private InputSystemOptionsAsset _inputOptions;

        #endregion

        #region Configuration — Localization

        [Header("Localization")]
        [Tooltip("默认语言标识（如 zh_Hans / en）。留空则不登记 Localization 引导阶段。")]
        [SerializeField] private string _defaultLanguage = "";

        [Tooltip("语言表（扁平 JSON 的 TextAsset，与资源地址加载同一种格式）。" +
                 "可空——此时该阶段打一条警告后跳过初始化。")]
        [SerializeField] private TextAsset _languageTable;

        #endregion

        #region Extension Points

        /// <summary>
        /// 登记启动阶段。<b>默认实现 = 默认组合 + 上面那组字段的注入</b>：
        /// <list type="bullet">
        /// <item>Asset / Save 用字段重建阶段（字段保持默认值时，与原内置阶段语义逐字相同）；</item>
        /// <item>UI / Input / Localization 只在对应字段填了时才登记——留空等于不初始化那个模块，
        /// 与不用本组件时一致。</item>
        /// </list>
        /// <para><b>覆写</b>：调 <c>base.ConfigureStages()</c> 之后追加或替换（服务实例类配置在这里注入，
        /// 例：先 <see cref="Bootstrap.Unregister{T}()"/> 掉内置的 Asset 阶段，再
        /// <see cref="Bootstrap.Register"/> 一个带 <c>RemoteServices</c> / <c>DecryptionServices</c> 的）；
        /// 不调 <c>base</c> 则完全不登记默认组合，登记什么自己说了算。</para>
        /// <para>时机：<c>Awake</c> 内、<c>Start</c> 跑管线之前。登记先后不影响执行顺序
        /// （执行序由相位决定），但整张表必须在 <see cref="Bootstrap.RunAsync"/> 之前定稿。</para>
        /// </summary>
        protected override void ConfigureStages()
        {
            // Asset / Save 都用字段重建（字段保持默认值时，构造出的 options 与默认值逐字相同，
            // 语义与原有无参阶段等价——省掉「判断是否偏离默认」所需的第二份字面量）。
            // 顺序按 README 推荐的替换配方：**先 Register 自定义的 Asset，再 RegisterDefaults**——
            // 后者按类型跳过已存在的，于是登记表的顺序仍是「Asset → Data → Save」，
            // 与相位表一致（执行序本就由相位决定，这里照顾的是 Stages 这个给人看的视图）。
            Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions
            {
                PackageName = string.IsNullOrEmpty(_assetPackageName) ? null : _assetPackageName,
                PlayMode = _assetPlayMode,
                AutoReclaimOnLowMemory = _autoReclaimOnLowMemory,
            }));

            Bootstrap.RegisterDefaults();

            // Save 是后置替换：先摘掉默认那份，再登记自定义的（同样保持它在末尾）
            Bootstrap.Unregister<SaveBootstrapStage>();
            Bootstrap.Register(new SaveBootstrapStage(new SaveOptions
            {
                CurrentVersion = _saveVersion,
            }));

            if (_uiRoot != null)
            {
                Bootstrap.Register(new UIBootstrapStage(_uiRoot));
            }

            if (_inputOptions != null)
            {
                Bootstrap.Register(new InputBootstrapStage(_inputOptions.ToOptions()));
            }

            if (!string.IsNullOrEmpty(_defaultLanguage))
            {
                Bootstrap.Register(new LocalizationBootstrapStage(_defaultLanguage, _languageTable));
            }
        }

        #endregion
    }
}
