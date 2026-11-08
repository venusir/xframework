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
            UIDeclarativeDialogPanel dialog = root.GetComponent<UIDeclarativeDialogPanel>();
            if (panel == null && dialog == null)
            {
                Add(issues, UIAuthoringRuleIds.RootComponentMissing, UIAuthoringIssueSeverity.Error,
                    "根节点没有 UIDeclarativePanel / UIDeclarativeDialogPanel。");
                return issues;
            }

            if (definition == null)
            {
                Add(issues, UIAuthoringRuleIds.DefinitionMissing, UIAuthoringIssueSeverity.Warning,
                    "面板没有绑定 UIPanelDefinition；若是静态预制体可忽略，否则请检查引用。");
                return issues;
            }

            if (definition.Kind == UIPanelDefinitionKind.Hud)
            {
                Add(issues, UIAuthoringRuleIds.HudNotSupported, UIAuthoringIssueSeverity.Error,
                    "HUD 定义在阶段 2a 尚未实现。");
                return issues;
            }

            UIPanelDefinitionKind rootKind = panel != null
                ? UIPanelDefinitionKind.Panel
                : UIPanelDefinitionKind.Dialog;

            if (definition.Kind != rootKind)
            {
                Add(issues, UIAuthoringRuleIds.KindMismatch, UIAuthoringIssueSeverity.Error,
                    "定义的 Kind 与根组件不匹配：定义=" + definition.Kind + "，根组件=" + rootKind + "。");
                return issues;
            }

            Dictionary<string, int> nodeCounts = CountNodes(root.transform);
            var resultIds = new HashSet<string>(StringComparer.Ordinal);

            ValidateTextBindings(root.transform, definition, nodeCounts, issues);
            ValidateActionBindings(root.transform, definition, rootKind, nodeCounts, resultIds,
                addresses, addressCheckPerformed, loadPrefab, issues);
            ValidateTabGroups(root.transform, definition, nodeCounts, issues);

            return issues;
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

            if (kind == UIPanelDefinitionKind.Panel && action.Kind == UIActionKind.CloseWithResult)
            {
                Add(issues, UIAuthoringRuleIds.ActionNotAllowed, UIAuthoringIssueSeverity.Error,
                    "CloseWithResult 只能用于 Dialog。");
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
                            return !IsDialogPanelType(panel.GetType());
                        },
                        action.Kind + " 目标根必须是具体 UIPanelBase，且不能是声明式面板/任何 UIDialogPanel<>。",
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
