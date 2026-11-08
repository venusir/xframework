namespace XFramework.XUI.Authoring.Editor
{
    /// <summary>
    /// 校验发现的严重度；数值越大越严重。
    /// </summary>
    internal enum UIAuthoringIssueSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    /// <summary>
    /// 声明式 UI 校验规则号。格式固定为 <c>UI-AUTH-xxx</c>。
    /// </summary>
    internal static class UIAuthoringRuleIds
    {
        internal const string RootComponentMissing = "UI-AUTH-001";
        internal const string DefinitionMissing = "UI-AUTH-002";
        internal const string KindMismatch = "UI-AUTH-003";

        internal const string TextNodeMissing = "UI-AUTH-010";
        internal const string TextTargetMissing = "UI-AUTH-011";
        internal const string NodeDuplicated = "UI-AUTH-012";

        internal const string ButtonNodeMissing = "UI-AUTH-020";
        internal const string ButtonMissing = "UI-AUTH-021";

        internal const string ActionOrder = "UI-AUTH-030";
        internal const string ActionResultInvalid = "UI-AUTH-031";
        internal const string ActionNotAllowed = "UI-AUTH-032";

        internal const string TargetInvalid = "UI-AUTH-040";
        internal const string AddressUnresolved = "UI-AUTH-041";

        internal const string NodeMissing = "UI-AUTH-050";
        internal const string TabInvalid = "UI-AUTH-051";

        internal const string GlobalAction = "UI-AUTH-060";

        internal const string HudRootRectTransformMissing = "UI-AUTH-HUD-001";
        internal const string HudActionNotSupported = "UI-AUTH-HUD-002";
        internal const string HudTabNotSupported = "UI-AUTH-HUD-003";
        internal const string HudNodeDepthExceeded = "UI-AUTH-HUD-004";
    }

    /// <summary>
    /// 一条校验发现。
    /// </summary>
    internal readonly struct UIAuthoringIssue
    {
        internal UIAuthoringIssue(string ruleId, UIAuthoringIssueSeverity severity, string message,
            string nodePath = "")
        {
            RuleId = ruleId;
            Severity = severity;
            Message = message;
            NodePath = nodePath ?? string.Empty;
        }

        internal string RuleId { get; }

        internal UIAuthoringIssueSeverity Severity { get; }

        internal string Message { get; }

        internal string NodePath { get; }
    }
}
