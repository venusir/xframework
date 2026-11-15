using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.XData;
using XFramework.XInput;
using XFramework.XLocalization;
using XFramework.XSave;
using XFramework.XUI;
using BootstrapRegistry = XFramework.XBootstrap.Bootstrap;

namespace XFramework.Editor.BootstrapValidation
{
    /// <summary>Bootstrap 校验发现的严重度。数值越大越严重。</summary>
    internal enum BootstrapIssueSeverity
    {
        /// <summary>提示：多半是疏忽，但也可能是有意为之。</summary>
        Info = 0,

        /// <summary>警告：不会报错，但运行时会静默失效或行为不一致。</summary>
        Warning = 1,

        /// <summary>错误：启动会失败或形成同相位双份。</summary>
        Error = 2,
    }

    /// <summary>规则号常量。格式固定为 <c>BST-VAL-xxx</c>。</summary>
    internal static class BootstrapRuleIds
    {
        /// <summary>Build Settings 没有启动场景 / 索引 0 未启用。</summary>
        public const string MissingLaunchScene = "BST-VAL-001";

        /// <summary>当前打开的场景里出现多个 <see cref="GameLauncher"/>。</summary>
        public const string MultipleLaunchers = "BST-VAL-002";

        /// <summary>Asset 阶段配了 Host 却没有注入 <c>RemoteServices</c>。</summary>
        public const string HostWithoutRemoteServices = "BST-VAL-003";

        /// <summary>同一内置阶段类型登记了多次（<c>RegisterDefaults</c> 与启动器混用等）。</summary>
        public const string DuplicateBuiltInStages = "BST-VAL-004";

        /// <summary>启动器没有赋 Profile（零配置路径——提示级）。</summary>
        public const string LauncherWithoutProfile = "BST-VAL-005";

        /// <summary>启动器没有填 UI 根（不会登记 UI 阶段——提示级）。</summary>
        public const string LauncherWithoutUiRoot = "BST-VAL-006";

        /// <summary>当前打开的场景里没有启动器（可能用静态入口——提示级）。</summary>
        public const string NoLauncherInOpenScenes = "BST-VAL-007";
    }

    /// <summary>一条 Bootstrap 校验发现。</summary>
    internal readonly struct BootstrapIssue
    {
        /// <summary>创建一条校验发现。</summary>
        /// <param name="ruleId">规则号，见 <see cref="BootstrapRuleIds"/>。</param>
        /// <param name="severity">严重度。</param>
        /// <param name="message">面向维护者的说明。</param>
        /// <param name="context">可点击定位的对象；不针对具体对象时为空。</param>
        public BootstrapIssue(string ruleId, BootstrapIssueSeverity severity, string message,
                              UnityEngine.Object context = null)
        {
            RuleId = ruleId;
            Severity = severity;
            Message = message;
            Context = context;
        }

        /// <summary>规则号，见 <see cref="BootstrapRuleIds"/>。</summary>
        public string RuleId { get; }

        /// <summary>严重度。</summary>
        public BootstrapIssueSeverity Severity { get; }

        /// <summary>面向维护者的说明。</summary>
        public string Message { get; }

        /// <summary>可点击定位的对象；不针对具体对象时为空。</summary>
        public UnityEngine.Object Context { get; }
    }

    /// <summary>
    /// Bootstrap 的编辑器校验：启动场景、启动器、Profile 与登记表的一致性。
    /// <para><b>只读</b>——不修改场景 / 资产 / 登记表。菜单入口在
    /// <c>Tools/XFramework/Bootstrap/Validate Bootstrap</c>。</para>
    /// <para><b>边界如实</b>：Build Settings 的场景只检查索引与启用状态（不打开场景扫描）；
    /// 登记表检查只在 PlayMode 或使用方手动登记后有效（编辑模式下 <c>Awake</c> 不会跑）。</para>
    /// </summary>
    internal static class BootstrapValidator
    {
        #region Fields

        private static readonly HashSet<Type> BuiltInStageTypes = new HashSet<Type>
        {
            typeof(AssetBootstrapStage),
            typeof(DataBootstrapStage),
            typeof(SaveBootstrapStage),
            typeof(UIBootstrapStage),
            typeof(InputBootstrapStage),
            typeof(LocalizationBootstrapStage),
        };

        /// <summary>Asset 阶段的 options 是内部字段（不属于公开契约）；校验器用反射读取，失配时跳过该检查。</summary>
        private static readonly FieldInfo AssetOptionsField =
            typeof(AssetBootstrapStage).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic);

        #endregion

        #region Validation

        /// <summary>执行全部校验，返回发现列表（按严重度降序）。</summary>
        internal static List<BootstrapIssue> Validate()
        {
            var issues = new List<BootstrapIssue>();

            ValidateBuildSettings(issues);

            var launchers = UnityEngine.Object.FindObjectsByType<GameLauncher>(FindObjectsInactive.Include);
            ValidateLaunchers(launchers, issues);
            ValidateRegistry(issues);

            issues.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            return issues;
        }

        private static void ValidateBuildSettings(List<BootstrapIssue> issues)
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0)
            {
                issues.Add(new BootstrapIssue(BootstrapRuleIds.MissingLaunchScene,
                    BootstrapIssueSeverity.Warning,
                    "Build Settings 没有任何场景：N0 启动路径需要启动场景在索引 0。"));
                return;
            }

            if (!scenes[0].enabled || string.IsNullOrEmpty(scenes[0].path))
            {
                issues.Add(new BootstrapIssue(BootstrapRuleIds.MissingLaunchScene,
                    BootstrapIssueSeverity.Warning,
                    $"Build Settings 索引 0 的场景未启用或为空（{scenes[0].path}）：N0 启动路径不会生效。"));
            }
        }

        private static void ValidateLaunchers(GameLauncher[] launchers, List<BootstrapIssue> issues)
        {
            if (launchers.Length == 0)
            {
                issues.Add(new BootstrapIssue(BootstrapRuleIds.NoLauncherInOpenScenes,
                    BootstrapIssueSeverity.Info,
                    "当前打开的场景里没有 GameLauncher（使用静态入口时属正常）。"));
                return;
            }

            if (launchers.Length > 1)
            {
                issues.Add(new BootstrapIssue(BootstrapRuleIds.MultipleLaunchers,
                    BootstrapIssueSeverity.Error,
                    $"发现 {launchers.Length} 个 GameLauncher：同一会话只应有一个启动器，任一实例销毁都会拆掉全局服务。",
                    launchers[0]));
            }

            for (int i = 0; i < launchers.Length; i++)
            {
                var launcher = launchers[i];
                if (!(launcher is DefaultGameLauncher))
                    continue;

                var serialized = new SerializedObject(launcher);
                var profileProperty = serialized.FindProperty("_profile");
                var uiRootProperty = serialized.FindProperty("_uiRoot");

                var profile = profileProperty?.objectReferenceValue as BootstrapProfile;
                if (profile == null)
                {
                    issues.Add(new BootstrapIssue(BootstrapRuleIds.LauncherWithoutProfile,
                        BootstrapIssueSeverity.Info,
                        "启动器没有赋 BootstrapProfile：走零配置路径（默认三件 + UI Root 非空时的 UI 阶段）。",
                        launcher));
                }
                else
                {
                    ValidateProfile(profile, issues);
                }

                if (uiRootProperty != null && uiRootProperty.objectReferenceValue == null)
                {
                    issues.Add(new BootstrapIssue(BootstrapRuleIds.LauncherWithoutUiRoot,
                        BootstrapIssueSeverity.Info,
                        "启动器没有填 UI 根：不会登记 UI 阶段（用 UIRootNode 自动初始化时属正常）。",
                        launcher));
                }
            }
        }

        private static void ValidateProfile(BootstrapProfile profile, List<BootstrapIssue> issues)
        {
            var serialized = new SerializedObject(profile);
            var modeProperty = serialized.FindProperty("_assetPlayMode");
            if (modeProperty == null || modeProperty.enumValueIndex != (int)AssetPlayMode.Host)
                return;

            // 登记表里已有 Asset 阶段时，由 ValidateRegistry 给出权威判定（能读到 options）；
            // 编辑模式下登记表为空，这里只能按 Profile 的值提醒。
            if (FindRegisteredAssetStage() != null)
                return;

            issues.Add(new BootstrapIssue(BootstrapRuleIds.HostWithoutRemoteServices,
                BootstrapIssueSeverity.Warning,
                "Profile 的 Asset Play Mode 是 Host：启动前必须在 ConfigureStages 里给 AssetInitOptions 注入 RemoteServices，" +
                "否则启动抛 ArgumentException（Profile 只装数据，不装服务实例）。",
                profile));
        }

        private static void ValidateRegistry(List<BootstrapIssue> issues)
        {
            var stages = BootstrapRegistry.Stages;
            if (stages.Count == 0)
                return; // 编辑模式下 Awake 不跑，空表是正常态

            var counts = new Dictionary<Type, int>();
            for (int i = 0; i < stages.Count; i++)
            {
                var type = stages[i].GetType();
                counts.TryGetValue(type, out int count);
                counts[type] = count + 1;

                if (stages[i] is AssetBootstrapStage assetStage)
                    ValidateAssetStageOptions(assetStage, issues);
            }

            foreach (var pair in counts)
            {
                if (pair.Value > 1 && BuiltInStageTypes.Contains(pair.Key))
                {
                    issues.Add(new BootstrapIssue(BootstrapRuleIds.DuplicateBuiltInStages,
                        BootstrapIssueSeverity.Error,
                        $"内置阶段 {pair.Key.Name} 登记了 {pair.Value} 次：同一门面会被初始化两次（第二次被幂等守卫挡下，options 被静默忽略）。",
                        null));
                }
            }
        }

        private static void ValidateAssetStageOptions(AssetBootstrapStage stage, List<BootstrapIssue> issues)
        {
            if (AssetOptionsField == null)
                return; // 字段改名：宁可不查，也不误报

            var options = AssetOptionsField.GetValue(stage) as AssetInitOptions;
            if (options == null || options.PlayMode != AssetPlayMode.Host || options.RemoteServices != null)
                return;

            issues.Add(new BootstrapIssue(BootstrapRuleIds.HostWithoutRemoteServices,
                BootstrapIssueSeverity.Error,
                "Asset 阶段配了 Host 但 RemoteServices 为 null：启动会在 Asset 阶段抛 ArgumentException（R3-AST-009）。",
                null));
        }

        private static AssetBootstrapStage FindRegisteredAssetStage()
        {
            var stages = BootstrapRegistry.Stages;
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] is AssetBootstrapStage asset)
                    return asset;
            }

            return null;
        }

        #endregion
    }

    /// <summary>把校验结果输出到 Console；错误 / 警告 / 提示分别用对应级别，可点击定位对象。</summary>
    internal static class BootstrapValidatorMenu
    {
        /// <summary>菜单入口：<c>Tools/XFramework/Bootstrap/Validate Bootstrap</c>。</summary>
        [MenuItem("Tools/XFramework/Bootstrap/Validate Bootstrap")]
        private static void ValidateBootstrap()
        {
            var issues = BootstrapValidator.Validate();

            int errors = 0;
            int warnings = 0;
            int infos = 0;

            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                string message = $"[{issue.RuleId}] {issue.Message}";

                switch (issue.Severity)
                {
                    case BootstrapIssueSeverity.Error:
                        errors++;
                        Debug.LogError(message, issue.Context);
                        break;
                    case BootstrapIssueSeverity.Warning:
                        warnings++;
                        Debug.LogWarning(message, issue.Context);
                        break;
                    default:
                        infos++;
                        Debug.Log(message, issue.Context);
                        break;
                }
            }

            Debug.Log($"[Bootstrap 校验] 完成：错误 {errors}、警告 {warnings}、提示 {infos}。");
        }
    }
}
