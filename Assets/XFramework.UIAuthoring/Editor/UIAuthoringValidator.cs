using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring.Editor
{
    /// <summary>
    /// 声明式 UI 纯规则引擎：对内存中的预制体与定义资产做只读检查，不修改任何资产。
    /// </summary>
    internal static class UIAuthoringValidator
    {
        private const int MaxNodeDepth = 5;

        /// <summary>HUD 深度诊断的搜索硬上限，避免异常层级拖垮校验。</summary>
        private const int MaxHudSearchDepth = 32;

        internal static List<UIAuthoringIssue> Validate(
            GameObject root,
            UIPanelDefinition definition,
            UIAuthoringAddressCatalog addresses,
            bool addressCheckPerformed,
            Func<string, GameObject> loadPrefab)
        {
            var issues = new List<UIAuthoringIssue>();
            if (root == null)
                return issues;

            UIDeclarativePanel panel = root.GetComponent<UIDeclarativePanel>();
            UIDeclarativePopupPanel popup = root.GetComponent<UIDeclarativePopupPanel>();
            UIDeclarativeDialogPanel dialog = root.GetComponent<UIDeclarativeDialogPanel>();
            UIDeclarativeHudItem hud = root.GetComponent<UIDeclarativeHudItem>();
            if (panel == null && popup == null && dialog == null && hud == null)
            {
                Add(issues, UIAuthoringRuleIds.RootComponentMissing, UIAuthoringIssueSeverity.Error,
                    "根节点没有 UIDeclarativePanel / UIDeclarativePopupPanel / " +
                    "UIDeclarativeDialogPanel / UIDeclarativeHudItem。");
                return issues;
            }

            if (definition == null)
            {
                Add(issues, UIAuthoringRuleIds.DefinitionMissing, UIAuthoringIssueSeverity.Warning,
                    "根节点没有绑定 UIPanelDefinition；若是静态预制体可忽略，否则请检查引用。");
                return issues;
            }

            UIPanelDefinitionKind rootKind = panel != null
                ? UIPanelDefinitionKind.Panel
                : popup != null
                    ? UIPanelDefinitionKind.Popup
                    : dialog != null
                        ? UIPanelDefinitionKind.Dialog
                        : UIPanelDefinitionKind.Hud;

            if (definition.Kind != rootKind)
            {
                Add(issues, UIAuthoringRuleIds.KindMismatch, UIAuthoringIssueSeverity.Error,
                    "定义的 Kind 与根组件不匹配：定义=" + definition.Kind + "，根组件=" + rootKind + "。");
                return issues;
            }

            Dictionary<string, int> nodeCounts = CountNodes(root.transform);

            if (rootKind == UIPanelDefinitionKind.Hud)
            {
                ValidateHud(root, definition, nodeCounts, issues);
                return issues;
            }

            var resultIds = new HashSet<string>(StringComparer.Ordinal);

            ValidateTextBindings(root.transform, definition, nodeCounts, issues);
            ValidateDataBindings(root.transform, definition, rootKind, nodeCounts, issues);
            ValidateActionBindings(root.transform, definition, rootKind, nodeCounts, resultIds,
                addresses, addressCheckPerformed, loadPrefab, issues);
            ValidateTabGroups(root.transform, definition, nodeCounts, issues);

            return issues;
        }

        /// <summary>
        /// HUD（阶段 2b）只支持静态文本：根必须有 RectTransform，定义里不能有动作/页签。
        /// 节点约定规则核心校验器不查（只跑 001/008），由这里补上。
        /// </summary>
        private static void ValidateHud(GameObject root, UIPanelDefinition definition,
            Dictionary<string, int> nodeCounts, List<UIAuthoringIssue> issues)
        {
            if (!(root.transform is RectTransform))
            {
                Add(issues, UIAuthoringRuleIds.HudRootRectTransformMissing,
                    UIAuthoringIssueSeverity.Error, "HUD 根节点必须有 RectTransform。");
            }

            if (definition.ActionBindings != null && definition.ActionBindings.Count > 0)
            {
                Add(issues, UIAuthoringRuleIds.HudActionNotSupported,
                    UIAuthoringIssueSeverity.Error,
                    "HUD 不支持动作绑定；关闭 HUD 请用程序调用 UIManager.HideHud。");
            }

            if (definition.TabGroups != null && definition.TabGroups.Count > 0)
            {
                Add(issues, UIAuthoringRuleIds.HudTabNotSupported,
                    UIAuthoringIssueSeverity.Warning, "HUD 不支持页签组，TabGroups 会被忽略。");
            }

            if (definition.TextBindings != null)
            {
                for (int i = 0; i < definition.TextBindings.Count; i++)
                    WarnIfHudNodeTooDeep(root.transform, definition.TextBindings[i]?.NodeName, issues);
            }

            if (definition.DataBindings != null)
            {
                for (int i = 0; i < definition.DataBindings.Count; i++)
                    WarnIfHudNodeTooDeep(root.transform, definition.DataBindings[i]?.NodeName, issues);
            }

            ValidateTextBindings(root.transform, definition, nodeCounts, issues);
            ValidateDataBindings(root.transform, definition, UIPanelDefinitionKind.Hud, nodeCounts, issues);
        }

        /// <summary>
        /// HUD 的深度检查不限规则深度地找节点，只为报出「第几层超限」。
        /// </summary>
        private static void WarnIfHudNodeTooDeep(Transform root, string nodeName,
            List<UIAuthoringIssue> issues)
        {
            if (string.IsNullOrEmpty(nodeName))
                return;

            Transform node = FindNodeAnyDepth(root, nodeName, 0, out int depth);
            if (node != null && depth > MaxNodeDepth)
            {
                Add(issues, UIAuthoringRuleIds.HudNodeDepthExceeded,
                    UIAuthoringIssueSeverity.Warning,
                    "绑定节点位于第 " + depth + " 层，超过约定绑定的 " + MaxNodeDepth +
                    " 层上限（根为第 0 层），运行时不会绑定。",
                    nodeName);
            }
        }

        private static void ValidateTextBindings(Transform root, UIPanelDefinition definition,
            Dictionary<string, int> nodeCounts, List<UIAuthoringIssue> issues)
        {
            if (definition.TextBindings == null)
                return;

            for (int i = 0; i < definition.TextBindings.Count; i++)
            {
                UITextBinding binding = definition.TextBindings[i];
                if (binding == null)
                    continue;

                if (!RequireNode(root, binding.NodeName, nodeCounts,
                        UIAuthoringRuleIds.TextNodeMissing, "文本绑定", issues))
                    continue;

                Transform node = FindNode(root, binding.NodeName);
                if (node == null)
                    continue;

                if (node.GetComponent<TMP_Text>() == null && node.GetComponent<Text>() == null)
                {
                    Add(issues, UIAuthoringRuleIds.TextTargetMissing, UIAuthoringIssueSeverity.Error,
                        "文本节点既没有 TMP_Text 也没有 UGUI Text。", node.name);
                }

                if (binding.UseLocalization && string.IsNullOrEmpty(binding.LocalizationKey))
                {
                    Add(issues, UIAuthoringRuleIds.TextNodeMissing, UIAuthoringIssueSeverity.Warning,
                        "文本绑定启用了本地化但没有 LocalizationKey。", node.name);
                }
            }
        }

        /// <summary>
        /// 数据绑定规则：节点/深度、空 key、目标组件、TwoWay 与 Format 的适用范围、
        /// 同节点冲突，以及 HUD 的不可交互双向绑定提示。
        /// <para>不检查「定义资产是否被 YooAsset 采集」：被预制体引用的 SO 必进构建图
        /// （构建图探针已证实），该检查会误报。</para>
        /// </summary>
        private static void ValidateDataBindings(Transform root, UIPanelDefinition definition,
            UIPanelDefinitionKind kind, Dictionary<string, int> nodeCounts,
            List<UIAuthoringIssue> issues)
        {
            if (definition.DataBindings == null)
                return;

            HashSet<string> textNodes = CollectTextBindingNodes(definition);
            var boundNodes = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < definition.DataBindings.Count; i++)
            {
                UIDataBinding binding = definition.DataBindings[i];
                if (binding == null)
                    continue;

                if (string.IsNullOrEmpty(binding.DataKey))
                {
                    Add(issues, UIAuthoringRuleIds.DataKeyMissing, UIAuthoringIssueSeverity.Error,
                        "数据绑定的 DataKey 不能为空。", binding.NodeName ?? string.Empty);
                }

                if (!string.IsNullOrEmpty(binding.NodeName))
                {
                    if (!boundNodes.Add(binding.NodeName))
                    {
                        Add(issues, UIAuthoringRuleIds.DataNodeConflict,
                            UIAuthoringIssueSeverity.Warning,
                            "同一节点配置了多条数据绑定，后一条会覆盖前一条。", binding.NodeName);
                    }

                    if (textNodes.Contains(binding.NodeName))
                    {
                        Add(issues, UIAuthoringRuleIds.DataNodeConflict,
                            UIAuthoringIssueSeverity.Warning,
                            "同一节点同时配置了文本绑定与数据绑定，数据绑定会覆盖静态文本。",
                            binding.NodeName);
                    }
                }

                if (!RequireNode(root, binding.NodeName, nodeCounts,
                        UIAuthoringRuleIds.DataNodeMissing, "数据绑定", issues))
                    continue;

                Transform node = FindNode(root, binding.NodeName);
                ValidateDataTarget(node, binding, issues);

                bool isTwoWayKind = binding.Kind == UIDataBindingKind.Slider
                                    || binding.Kind == UIDataBindingKind.Toggle;

                if (binding.TwoWay && !isTwoWayKind)
                {
                    Add(issues, UIAuthoringRuleIds.DataTwoWayNotSupported,
                        UIAuthoringIssueSeverity.Error,
                        "TwoWay 只支持 Slider / Toggle 数据绑定。", binding.NodeName);
                }

                if (!string.IsNullOrEmpty(binding.Format) && binding.Kind != UIDataBindingKind.Text)
                {
                    Add(issues, UIAuthoringRuleIds.DataFormatIgnored,
                        UIAuthoringIssueSeverity.Warning,
                        "Format 只对 Text 数据绑定生效，其它种类会被忽略。", binding.NodeName);
                }

                if (kind == UIPanelDefinitionKind.Hud && binding.TwoWay && isTwoWayKind)
                {
                    Add(issues, UIAuthoringRuleIds.HudTwoWayNotInteractive,
                        UIAuthoringIssueSeverity.Warning,
                        "HUD 默认关闭 Raycaster，双向绑定只能由代码写回，玩家无法操作。",
                        binding.NodeName);
                }
            }
        }

        private static void ValidateDataTarget(Transform node, UIDataBinding binding,
            List<UIAuthoringIssue> issues)
        {
            switch (binding.Kind)
            {
                case UIDataBindingKind.Text:
                    if (node.GetComponent<TMP_Text>() == null && node.GetComponent<Text>() == null)
                    {
                        Add(issues, UIAuthoringRuleIds.DataTargetMissing,
                            UIAuthoringIssueSeverity.Error,
                            "数据绑定的节点既没有 TMP_Text 也没有 UGUI Text。", node.name);
                    }
                    break;

                case UIDataBindingKind.Sprite:
                case UIDataBindingKind.Color:
                case UIDataBindingKind.FillAmount:
                    if (node.GetComponent<Image>() == null)
                    {
                        Add(issues, UIAuthoringRuleIds.DataTargetMissing,
                            UIAuthoringIssueSeverity.Error,
                            "数据绑定种类 " + binding.Kind + " 需要节点上有 Image。", node.name);
                    }
                    break;

                case UIDataBindingKind.Slider:
                    if (node.GetComponent<Slider>() == null)
                    {
                        Add(issues, UIAuthoringRuleIds.DataTargetMissing,
                            UIAuthoringIssueSeverity.Error,
                            "数据绑定种类 Slider 需要节点上有 Slider。", node.name);
                    }
                    break;

                case UIDataBindingKind.Toggle:
                    if (node.GetComponent<Toggle>() == null)
                    {
                        Add(issues, UIAuthoringRuleIds.DataTargetMissing,
                            UIAuthoringIssueSeverity.Error,
                            "数据绑定种类 Toggle 需要节点上有 Toggle。", node.name);
                    }
                    break;

                case UIDataBindingKind.Active:
                    break;

                default:
                    Add(issues, UIAuthoringRuleIds.DataTargetMissing,
                        UIAuthoringIssueSeverity.Error,
                        "未知的数据绑定种类：" + binding.Kind + "。", node.name);
                    break;
            }
        }

        private static HashSet<string> CollectTextBindingNodes(UIPanelDefinition definition)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (definition.TextBindings == null)
                return names;

            for (int i = 0; i < definition.TextBindings.Count; i++)
            {
                UITextBinding text = definition.TextBindings[i];
                if (text != null && !string.IsNullOrEmpty(text.NodeName))
                    names.Add(text.NodeName);
            }

            return names;
        }

        private static void ValidateActionBindings(Transform root, UIPanelDefinition definition,
            UIPanelDefinitionKind kind, Dictionary<string, int> nodeCounts, HashSet<string> resultIds,
            UIAuthoringAddressCatalog addresses, bool addressCheckPerformed,
            Func<string, GameObject> loadPrefab, List<UIAuthoringIssue> issues)
        {
            if (definition.ActionBindings == null)
                return;

            for (int i = 0; i < definition.ActionBindings.Count; i++)
            {
                UIActionBinding binding = definition.ActionBindings[i];
                if (binding == null)
                    continue;

                if (RequireNode(root, binding.ButtonNodeName, nodeCounts,
                        UIAuthoringRuleIds.ButtonNodeMissing, "按钮动作", issues))
                {
                    Transform node = FindNode(root, binding.ButtonNodeName);
                    if (node != null && node.GetComponent<Button>() == null)
                    {
                        Add(issues, UIAuthoringRuleIds.ButtonMissing, UIAuthoringIssueSeverity.Error,
                            "动作节点没有 Button 组件。", node.name);
                    }
                }

                if (binding.Actions == null)
                    continue;

                for (int j = 0; j < binding.Actions.Count; j++)
                {
                    ValidateAction(root, definition, kind, binding.Actions[j], j, binding.Actions.Count,
                        nodeCounts, resultIds, addresses, addressCheckPerformed, loadPrefab, issues);
                }
            }
        }

        private static void ValidateAction(Transform root, UIPanelDefinition definition,
            UIPanelDefinitionKind kind, UIAction action, int index, int count,
            Dictionary<string, int> nodeCounts, HashSet<string> resultIds,
            UIAuthoringAddressCatalog addresses, bool addressCheckPerformed,
            Func<string, GameObject> loadPrefab, List<UIAuthoringIssue> issues)
        {
            if (action == null)
                return;

            if (action.Kind == UIActionKind.HideSelf)
            {
                Add(issues, UIAuthoringRuleIds.ActionNotAllowed, UIAuthoringIssueSeverity.Error,
                    "HideSelf 只适用于 HUD，2a 的 Panel/Dialog 不支持。");
                return;
            }

            if (kind != UIPanelDefinitionKind.Dialog && action.Kind == UIActionKind.CloseWithResult)
            {
                Add(issues, UIAuthoringRuleIds.ActionNotAllowed, UIAuthoringIssueSeverity.Error,
                    "CloseWithResult 只能用于 Dialog。");
                return;
            }

            if (kind == UIPanelDefinitionKind.Popup && action.Kind == UIActionKind.ShowPopup)
            {
                Add(issues, UIAuthoringRuleIds.ActionNotAllowed, UIAuthoringIssueSeverity.Error,
                    "声明式 Popup 同时只能有一个，弹窗内不能再开声明式 Popup。");
                return;
            }

            if (kind == UIPanelDefinitionKind.Dialog)
            {
                bool allowed = action.Kind == UIActionKind.CloseWithResult
                    || action.Kind == UIActionKind.CloseSelf
                    || action.Kind == UIActionKind.SetActive
                    || action.Kind == UIActionKind.SelectTab
                    || action.Kind == UIActionKind.ShowTip
                    || action.Kind == UIActionKind.Custom;
                if (!allowed)
                {
                    Add(issues, UIAuthoringRuleIds.ActionNotAllowed, UIAuthoringIssueSeverity.Error,
                        "Dialog 不支持动作 " + action.Kind + "。");
                    return;
                }
            }

            if (action.Kind == UIActionKind.CloseSelf || action.Kind == UIActionKind.CloseWithResult)
            {
                if (index != count - 1)
                {
                    Add(issues, UIAuthoringRuleIds.ActionOrder, UIAuthoringIssueSeverity.Error,
                        action.Kind + " 必须是动作链的最后一步。");
                }
            }
            else if (action.Kind == UIActionKind.CloseAll)
            {
                if (index != count - 1)
                {
                    Add(issues, UIAuthoringRuleIds.ActionOrder, UIAuthoringIssueSeverity.Warning,
                        "CloseAll 会关闭当前面板，建议放在动作链最后。");
                }
            }
            else if (action.Kind == UIActionKind.Replace && count > 1)
            {
                Add(issues, UIAuthoringRuleIds.ActionOrder, UIAuthoringIssueSeverity.Error,
                    "Replace 会关闭当前面板，必须单独成链。");
            }

            switch (action.Kind)
            {
                case UIActionKind.CloseAll:
                    Add(issues, UIAuthoringRuleIds.GlobalAction, UIAuthoringIssueSeverity.Warning,
                        "CloseAll 是全局副作用，会连带关闭 HUD/Tip。");
                    break;

                case UIActionKind.SetLayerVisibility:
                case UIActionKind.SetLayerInteractive:
                    Add(issues, UIAuthoringRuleIds.GlobalAction, UIAuthoringIssueSeverity.Warning,
                        action.Kind + " 是全局副作用。");
                    break;

                case UIActionKind.ShowTip:
                    if (string.IsNullOrEmpty(action.Text))
                    {
                        Add(issues, UIAuthoringRuleIds.ActionResultInvalid, UIAuthoringIssueSeverity.Warning,
                            "ShowTip 的文本为空。");
                    }
                    break;

                case UIActionKind.Custom:
                    if (string.IsNullOrEmpty(action.ActionId))
                    {
                        Add(issues, UIAuthoringRuleIds.ActionResultInvalid, UIAuthoringIssueSeverity.Error,
                            "Custom 的 actionId 不能为空。");
                    }
                    break;

                case UIActionKind.ShowDialog:
                    if (string.IsNullOrEmpty(action.ActionId))
                    {
                        Add(issues, UIAuthoringRuleIds.ActionResultInvalid, UIAuthoringIssueSeverity.Error,
                            "ShowDialog 的 actionId 不能为空。");
                    }
                    ValidateTarget(action.AssetPath, UIAuthoringRuleIds.TargetInvalid,
                        target => target.GetComponent<UIDeclarativeDialogPanel>() != null,
                        "ShowDialog 目标根必须是 UIDeclarativeDialogPanel。",
                        addresses, addressCheckPerformed, loadPrefab, issues);
                    break;

                case UIActionKind.ShowPopup:
                    ValidatePopupAnchor(root, action, nodeCounts, issues);
                    ValidateTarget(action.AssetPath, UIAuthoringRuleIds.TargetInvalid,
                        target => target.GetComponent<UIDeclarativePopupPanel>() != null,
                        "ShowPopup 目标根必须是 UIDeclarativePopupPanel。",
                        addresses, addressCheckPerformed, loadPrefab, issues);
                    break;

                case UIActionKind.Open:
                case UIActionKind.Push:
                    ValidateTarget(action.AssetPath, UIAuthoringRuleIds.TargetInvalid,
                        target =>
                        {
                            var panel = target.GetComponent<UIPanelBase>();
                            if (panel == null)
                                return false;
                            if (panel is UIDeclarativePanel)
                                return false;
                            // 声明式弹窗同样是 UIPanelBase：放行会按普通面板层打开，绕过弹窗的锚定/层语义
                            if (panel is UIDeclarativePopupPanel)
                                return false;
                            return !IsDialogPanelType(panel.GetType());
                        },
                        action.Kind + " 目标根必须是具体 UIPanelBase，且不能是声明式面板/声明式弹窗/任何 UIDialogPanel<>。",
                        addresses, addressCheckPerformed, loadPrefab, issues);
                    break;

                case UIActionKind.Replace:
                    ValidateTarget(action.AssetPath, UIAuthoringRuleIds.TargetInvalid,
                        target => target.GetComponent<UIDeclarativePanel>() != null,
                        "Replace 目标根必须是 UIDeclarativePanel。",
                        addresses, addressCheckPerformed, loadPrefab, issues);
                    break;

                case UIActionKind.CloseWithResult:
                    if (string.IsNullOrEmpty(action.ResultId))
                    {
                        Add(issues, UIAuthoringRuleIds.ActionResultInvalid, UIAuthoringIssueSeverity.Error,
                            "CloseWithResult 的 resultId 不能为空。");
                    }
                    else if (!resultIds.Add(action.ResultId))
                    {
                        Add(issues, UIAuthoringRuleIds.ActionResultInvalid, UIAuthoringIssueSeverity.Error,
                            "CloseWithResult 的 resultId 在同一对话框内重复：" + action.ResultId + "。");
                    }
                    break;

                case UIActionKind.SetActive:
                    RequireNode(root, action.NodeName, nodeCounts,
                        UIAuthoringRuleIds.NodeMissing, "SetActive", issues);
                    break;

                case UIActionKind.SelectTab:
                    ValidateSelectTab(root, definition, action, nodeCounts, issues);
                    break;
            }
        }

        private static void ValidatePopupAnchor(Transform root, UIAction action,
            Dictionary<string, int> nodeCounts, List<UIAuthoringIssue> issues)
        {
            if (!RequireNode(root, action.AnchorNodeName, nodeCounts,
                    UIAuthoringRuleIds.PopupAnchorMissing, "ShowPopup 锚点", issues))
                return;

            Transform node = FindNode(root, action.AnchorNodeName);
            if (node != null && !(node is RectTransform))
            {
                Add(issues, UIAuthoringRuleIds.PopupAnchorMissing, UIAuthoringIssueSeverity.Error,
                    "ShowPopup 锚点必须是 RectTransform。", action.AnchorNodeName);
            }
        }

        private static void ValidateSelectTab(Transform root, UIPanelDefinition definition,
            UIAction action, Dictionary<string, int> nodeCounts, List<UIAuthoringIssue> issues)
        {
            UITabGroup group = FindTabGroup(definition, action.GroupName);
            if (group == null)
            {
                Add(issues, UIAuthoringRuleIds.TabInvalid, UIAuthoringIssueSeverity.Error,
                    "SelectTab 的页签组不存在：" + action.GroupName + "。");
                return;
            }

            if (group.NodeNames == null || action.Index < 0 || action.Index >= group.NodeNames.Count)
            {
                Add(issues, UIAuthoringRuleIds.TabInvalid, UIAuthoringIssueSeverity.Error,
                    "SelectTab 的索引越界：" + action.Index + "。");
                return;
            }

            for (int i = 0; i < group.NodeNames.Count; i++)
            {
                RequireNode(root, group.NodeNames[i], nodeCounts,
                    UIAuthoringRuleIds.NodeMissing, "页签", issues);
            }
        }

        private static void ValidateTabGroups(Transform root, UIPanelDefinition definition,
            Dictionary<string, int> nodeCounts, List<UIAuthoringIssue> issues)
        {
            if (definition.TabGroups == null)
                return;

            var groupNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < definition.TabGroups.Count; i++)
            {
                UITabGroup group = definition.TabGroups[i];
                if (group == null)
                    continue;

                if (string.IsNullOrEmpty(group.GroupName))
                {
                    Add(issues, UIAuthoringRuleIds.TabInvalid, UIAuthoringIssueSeverity.Error,
                        "页签组的 GroupName 不能为空。");
                }
                else if (!groupNames.Add(group.GroupName))
                {
                    Add(issues, UIAuthoringRuleIds.TabInvalid, UIAuthoringIssueSeverity.Error,
                        "页签组名重复：" + group.GroupName + "。");
                }

                if (group.NodeNames == null || group.NodeNames.Count == 0)
                {
                    Add(issues, UIAuthoringRuleIds.TabInvalid, UIAuthoringIssueSeverity.Error,
                        "页签组没有节点：" + group.GroupName + "。");
                    continue;
                }

                for (int j = 0; j < group.NodeNames.Count; j++)
                {
                    RequireNode(root, group.NodeNames[j], nodeCounts,
                        UIAuthoringRuleIds.NodeMissing, "页签", issues);
                }
            }
        }

        private static void ValidateTarget(string assetPath, string ruleId, Func<GameObject, bool> isValid,
            string message, UIAuthoringAddressCatalog addresses, bool addressCheckPerformed,
            Func<string, GameObject> loadPrefab, List<UIAuthoringIssue> issues)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Add(issues, ruleId, UIAuthoringIssueSeverity.Error, "动作的目标地址为空。");
                return;
            }

            if (!addressCheckPerformed || addresses == null || !addresses.IsAvailable)
            {
                Add(issues, UIAuthoringRuleIds.AddressUnresolved, UIAuthoringIssueSeverity.Warning,
                    "未执行地址检查，无法校验目标根类型：" + assetPath + "。");
                return;
            }

            if (!addresses.TryGetAssetPath(assetPath, out string path) || string.IsNullOrEmpty(path))
            {
                Add(issues, UIAuthoringRuleIds.AddressUnresolved, UIAuthoringIssueSeverity.Warning,
                    "地址未在 YooAsset 主资源采集目录中找到：" + assetPath + "。");
                return;
            }

            GameObject prefab = loadPrefab?.Invoke(path);
            if (prefab == null)
            {
                Add(issues, UIAuthoringRuleIds.AddressUnresolved, UIAuthoringIssueSeverity.Warning,
                    "地址解析到预制体失败：" + path + "。");
                return;
            }

            if (!isValid(prefab))
            {
                Add(issues, ruleId, UIAuthoringIssueSeverity.Error, message);
            }
        }

        private static bool IsDialogPanelType(Type type)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(UIDialogPanel<>))
                    return true;
            }
            return false;
        }

        private static UITabGroup FindTabGroup(UIPanelDefinition definition, string groupName)
        {
            if (definition.TabGroups == null || string.IsNullOrEmpty(groupName))
                return null;

            for (int i = 0; i < definition.TabGroups.Count; i++)
            {
                UITabGroup group = definition.TabGroups[i];
                if (group != null && string.Equals(group.GroupName, groupName, StringComparison.Ordinal))
                    return group;
            }
            return null;
        }

        private static Transform FindNode(Transform root, string nodeName)
        {
            if (root == null || string.IsNullOrEmpty(nodeName))
                return null;
            return FindNodeRecursive(root, nodeName, 0);
        }

        /// <summary>
        /// 不限约定深度地找节点，只为报出「第几层超限」；搜索本身仍设硬上限防止异常层级。
        /// </summary>
        private static Transform FindNodeAnyDepth(Transform node, string nodeName, int depth,
            out int foundDepth)
        {
            foundDepth = -1;
            if (node == null || depth > MaxHudSearchDepth)
                return null;

            if (string.Equals(node.name, nodeName, StringComparison.Ordinal))
            {
                foundDepth = depth;
                return node;
            }

            for (int i = 0; i < node.childCount; i++)
            {
                Transform found = FindNodeAnyDepth(node.GetChild(i), nodeName, depth + 1,
                    out foundDepth);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Transform FindNodeRecursive(Transform node, string nodeName, int depth)
        {
            if (node == null || depth > MaxNodeDepth)
                return null;
            if (string.Equals(node.name, nodeName, StringComparison.Ordinal))
                return node;
            for (int i = 0; i < node.childCount; i++)
            {
                Transform found = FindNodeRecursive(node.GetChild(i), nodeName, depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Dictionary<string, int> CountNodes(Transform root)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            CountNodesRecursive(root, 0, counts);
            return counts;
        }

        private static void CountNodesRecursive(Transform node, int depth, Dictionary<string, int> counts)
        {
            if (node == null || depth > MaxNodeDepth)
                return;
            counts.TryGetValue(node.name, out int count);
            counts[node.name] = count + 1;
            for (int i = 0; i < node.childCount; i++)
                CountNodesRecursive(node.GetChild(i), depth + 1, counts);
        }

        private static bool RequireNode(Transform root, string nodeName, Dictionary<string, int> nodeCounts,
            string missingRule, string context, List<UIAuthoringIssue> issues)
        {
            if (string.IsNullOrEmpty(nodeName))
            {
                Add(issues, missingRule, UIAuthoringIssueSeverity.Error, context + "的节点名为空。");
                return false;
            }

            if (!nodeCounts.TryGetValue(nodeName, out int count))
            {
                Add(issues, missingRule, UIAuthoringIssueSeverity.Error,
                    context + "节点不存在（或深度超过 " + MaxNodeDepth + " 层）：" + nodeName + "。",
                    nodeName);
                return false;
            }

            if (count > 1)
            {
                Add(issues, UIAuthoringRuleIds.NodeDuplicated, UIAuthoringIssueSeverity.Warning,
                    context + "节点名重复，运行时只取第一个：" + nodeName + "。", nodeName);
            }

            return true;
        }

        private static void Add(List<UIAuthoringIssue> issues, string ruleId,
            UIAuthoringIssueSeverity severity, string message, string nodePath = "")
        {
            issues.Add(new UIAuthoringIssue(ruleId, severity, message, nodePath));
        }
    }
}
