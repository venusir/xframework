namespace XFramework.Editor.UI
{
    /// <summary>
    /// 预制体校验发现的严重度。数值越大越严重，报告按此排序。
    /// </summary>
    internal enum UIPrefabIssueSeverity
    {
        /// <summary>提示：多半是疏忽，但也可能是有意为之。</summary>
        Info = 0,

        /// <summary>警告：不会报错，但运行时会静默失效或行为不一致。</summary>
        Warning = 1,

        /// <summary>错误：运行时会加载失败或找不到组件。</summary>
        Error = 2,
    }

    /// <summary>
    /// 规则号常量。
    /// <para>格式固定为 <c>UI-PREFAB-xxx</c>，由测试校验唯一性与格式；新增规则时同步更新使用方 README 的校验器说明。</para>
    /// </summary>
    internal static class UIPrefabRuleIds
    {
        /// <summary>根节点缺少框架管理的 UI 组件，组件挂在了子节点上。</summary>
        public const string RootComponentMissing = "UI-PREFAB-001";

        /// <summary><c>UIPanelBinding</c> 不在面板根节点。</summary>
        public const string BindingNotOnRoot = "UI-PREFAB-002";

        /// <summary>约定节点超过 5 层深度。</summary>
        public const string ConventionDepthExceeded = "UI-PREFAB-003";

        /// <summary>约定前缀节点的组件类型与约定不符。</summary>
        public const string ConventionTypeMismatch = "UI-PREFAB-004";

        /// <summary>约定节点名重复。</summary>
        public const string ConventionNameDuplicated = "UI-PREFAB-005";

        /// <summary>面板根 RectTransform 仍是 Unity 默认值。</summary>
        public const string DefaultRootRectTransform = "UI-PREFAB-006";

        /// <summary>面板根节点使用了非单位变换。</summary>
        public const string NonIdentityRootTransform = "UI-PREFAB-007";

        /// <summary>预制体未被 YooAsset 主资源采集器收录。</summary>
        public const string NotCollectedByYooAsset = "UI-PREFAB-008";

        /// <summary>Tip 预制体缺少 TMP 文本。</summary>
        public const string TipWithoutTmpText = "UI-PREFAB-009";
    }

    /// <summary>
    /// 一条预制体校验发现。
    /// </summary>
    internal readonly struct UIPrefabIssue
    {
        /// <summary>
        /// 创建一条校验发现。
        /// </summary>
        /// <param name="ruleId">规则号，见 <see cref="UIPrefabRuleIds"/>。</param>
        /// <param name="severity">严重度。</param>
        /// <param name="message">面向制作/开发的说明。</param>
        /// <param name="nodePath">出问题的节点相对预制体根的路径；不针对具体节点时为空。</param>
        public UIPrefabIssue(string ruleId, UIPrefabIssueSeverity severity, string message, string nodePath = "")
        {
            RuleId = ruleId;
            Severity = severity;
            Message = message;
            NodePath = nodePath ?? string.Empty;
        }

        /// <summary>规则号，见 <see cref="UIPrefabRuleIds"/>。</summary>
        public string RuleId { get; }

        /// <summary>严重度。</summary>
        public UIPrefabIssueSeverity Severity { get; }

        /// <summary>面向制作/开发的说明。</summary>
        public string Message { get; }

        /// <summary>出问题的节点相对预制体根的路径；不针对具体节点时为空。</summary>
        public string NodePath { get; }
    }
}
