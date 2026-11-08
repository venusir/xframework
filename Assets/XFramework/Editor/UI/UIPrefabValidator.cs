using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XUI;
using XFramework.XUI.Data;
using XFramework.XUI.View;

namespace XFramework.Editor.UI
{
    /// <summary>
    /// 纯规则引擎：对内存中的 UI 预制体根节点做只读检查。
    /// <para>不加载资产、不依赖 <c>AssetDatabase</c>，因此 EditMode 测试可以直接对 <see cref="GameObject"/> 跑。</para>
    /// <para>阈值必须与运行时保持一致：5 层约定深度、前缀表、根节点组件约束、池化变换重置。
    /// 规则号与适用面见 <see cref="UIPrefabRuleIds"/>。</para>
    /// </summary>
    internal static class UIPrefabValidator
    {
        #region Constants

        /// <summary>
        /// 与 <c>UIPanelBinding.CacheComponentsRecursive</c> 的 <c>maxDepth</c> 一致：
        /// 根为第 0 层，第 5 层仍会被缓存，第 6 层起不会。
        /// </summary>
        internal const int MaxConventionDepth = 5;

        private static readonly Vector2 DefaultAnchor = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 DefaultSize = new Vector2(100f, 100f);

        #endregion

        #region Public

        /// <summary>
        /// 校验一个 UI 预制体根节点。
        /// </summary>
        /// <param name="root">预制体根节点；为 null 时返回空列表。</param>
        /// <param name="addresses">地址检查命中的采集结果；未启用地址检查或未收录时为空。</param>
        /// <param name="addressCheckPerformed">地址检查是否真的执行过；false 时跳过规则 008。</param>
        /// <param name="isTmpText">TMP 文本探测；为 null 时使用 <see cref="UIPrefabTmpTextProbe"/>，测试可注入假实现。</param>
        /// <returns>发现列表，按规则产生顺序排列。</returns>
        public static List<UIPrefabIssue> Validate(
            GameObject root,
            IReadOnlyList<UIPrefabAddressEntry> addresses,
            bool addressCheckPerformed,
            Func<Component, bool> isTmpText = null)
        {
            var issues = new List<UIPrefabIssue>();
            if (root == null)
                return issues;

            isTmpText ??= UIPrefabTmpTextProbe.IsTmpText;

            var rootView = root.GetComponent<UIViewBase>();
            var rootTip = root.GetComponent<UITipItem>();

            ValidateRootComponent(root, rootView, rootTip, issues);
            ValidatePanel(root, issues, isTmpText);
            ValidateAddress(root, addresses, addressCheckPerformed, issues);
            ValidateTip(root, rootTip, issues, isTmpText);

            return issues;
        }

        #endregion

        #region Rule 001 — Root Component

        private static void ValidateRootComponent(
            GameObject root,
            UIViewBase rootView,
            UITipItem rootTip,
            List<UIPrefabIssue> issues)
        {
            if (rootView != null || rootTip != null)
                return;

            var childView = root.GetComponentInChildren<UIViewBase>(true);
            if (childView != null)
            {
                issues.Add(new UIPrefabIssue(
                    UIPrefabRuleIds.RootComponentMissing,
                    UIPrefabIssueSeverity.Error,
                    "根节点没有 UIViewBase 派生组件（面板 / HUD），但子节点上有。面板工厂与 HUD 管理器只对根节点取组件；请把该组件移到预制体根节点。",
                    BuildPath(root.transform, childView.transform)));
                return;
            }

            var childTip = root.GetComponentInChildren<UITipItem>(true);
            if (childTip != null)
            {
                issues.Add(new UIPrefabIssue(
                    UIPrefabRuleIds.RootComponentMissing,
                    UIPrefabIssueSeverity.Error,
                    "根节点没有 UITipItem，但子节点上有。Tip 管理器只对根节点取组件；请把 UITipItem 移到预制体根节点。",
                    BuildPath(root.transform, childTip.transform)));
            }
        }

        #endregion

        #region Rules 002–007 — Panel

        private static void ValidatePanel(GameObject root, List<UIPrefabIssue> issues, Func<Component, bool> isTmpText)
        {
            var panel = root.GetComponent<UIPanelBase>();
            if (panel == null)
                return;

            var rootBindings = root.GetComponents<UIPanelBinding>();

            if (rootBindings.Length == 0)
            {
                var childBinding = root.GetComponentInChildren<UIPanelBinding>(true);
                if (childBinding != null)
                {
                    issues.Add(new UIPrefabIssue(
                        UIPrefabRuleIds.BindingNotOnRoot,
                        UIPrefabIssueSeverity.Error,
                        "UIPanelBinding 不在面板根节点。UIPanelBase.Binding 只查自己所在的 GameObject；子节点上的绑定不会生效。",
                        BuildPath(root.transform, childBinding.transform)));
                }
            }
            else
            {
                if (rootBindings.Length > 1)
                {
                    issues.Add(new UIPrefabIssue(
                        UIPrefabRuleIds.BindingNotOnRoot,
                        UIPrefabIssueSeverity.Warning,
                        "面板根节点上有多个 UIPanelBinding，只有第一个会生效。",
                        root.name));
                }

                ValidateConventionNodes(root.transform, issues, isTmpText);
            }

            ValidateRootRectTransform(root, issues);
            ValidateRootTransform(root, issues);
        }

        private static void ValidateConventionNodes(
            Transform root,
            List<UIPrefabIssue> issues,
            Func<Component, bool> isTmpText)
        {
            // 键为节点名；运行时缓存使用 OrdinalIgnoreCase，同名只保留第一个。
            var cached = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Visit(root, 0);

            void Visit(Transform node, int depth)
            {
                if (node == null)
                    return;

                if (TryGetConventionExpectation(node.name, out var expectedType, out var expectedName, out var isTextPrefix))
                {
                    if (depth > MaxConventionDepth)
                    {
                        issues.Add(new UIPrefabIssue(
                            UIPrefabRuleIds.ConventionDepthExceeded,
                            UIPrefabIssueSeverity.Warning,
                            $"约定节点位于第 {depth} 层，超过约定绑定的 {MaxConventionDepth} 层上限（根为第 0 层），不会被缓存。",
                            BuildPath(root, node)));
                    }
                    else
                    {
                        var component = node.GetComponent(expectedType);
                        if (component == null)
                        {
                            var message = isTextPrefix && HasTmpText(node, isTmpText)
                                ? "约定节点上没有 UGUI Text 组件。约定式绑定只认 UnityEngine.UI.Text，不认 TMP_Text；请改用 UIBinder.BindToText 或换成 Text 组件。"
                                : $"约定节点上没有 {expectedName} 组件，约定绑定会跳过；请检查节点命名与组件类型。";

                            issues.Add(new UIPrefabIssue(
                                UIPrefabRuleIds.ConventionTypeMismatch,
                                UIPrefabIssueSeverity.Warning,
                                message,
                                BuildPath(root, node)));
                        }
                        else if (cached.TryGetValue(node.name, out var firstPath))
                        {
                            issues.Add(new UIPrefabIssue(
                                UIPrefabRuleIds.ConventionNameDuplicated,
                                UIPrefabIssueSeverity.Warning,
                                $"约定节点名重复（大小写不敏感），只绑定第一个；首次出现于 '{firstPath}'，本节点不会被绑定。",
                                BuildPath(root, node)));
                        }
                        else
                        {
                            cached[node.name] = BuildPath(root, node);
                        }
                    }
                }

                for (int i = 0; i < node.childCount; i++)
                    Visit(node.GetChild(i), depth + 1);
            }
        }

        private static void ValidateRootRectTransform(GameObject root, List<UIPrefabIssue> issues)
        {
            if (!(root.transform is RectTransform rect))
                return;

            if (!Approximately(rect.anchorMin, DefaultAnchor)
                || !Approximately(rect.anchorMax, DefaultAnchor)
                || !Approximately(rect.sizeDelta, DefaultSize))
            {
                return;
            }

            issues.Add(new UIPrefabIssue(
                UIPrefabRuleIds.DefaultRootRectTransform,
                UIPrefabIssueSeverity.Info,
                "面板根 RectTransform 仍是 Unity 默认值（居中 100×100）。框架不会调整它；全屏面板通常应四向拉伸，对话框/弹窗可保留居中。",
                root.name));
        }

        private static void ValidateRootTransform(GameObject root, List<UIPrefabIssue> issues)
        {
            var transform = root.transform;
            if (transform.localPosition == Vector3.zero
                && transform.localRotation == Quaternion.identity
                && transform.localScale == Vector3.one)
            {
                return;
            }

            issues.Add(new UIPrefabIssue(
                UIPrefabRuleIds.NonIdentityRootTransform,
                UIPrefabIssueSeverity.Warning,
                "面板根节点使用了非单位变换。对象池复用取回实例时会把 localPosition / localRotation / localScale 重置为 0 / identity / 1，首次打开与复用后的外观会不一致；请把偏移与缩放放到子节点。",
                root.name));
        }

        #endregion

        #region Rule 008 — YooAsset Collection

        private static void ValidateAddress(
            GameObject root,
            IReadOnlyList<UIPrefabAddressEntry> addresses,
            bool addressCheckPerformed,
            List<UIPrefabIssue> issues)
        {
            if (!addressCheckPerformed)
                return;

            if (addresses != null && addresses.Count > 0)
                return;

            issues.Add(new UIPrefabIssue(
                UIPrefabRuleIds.NotCollectedByYooAsset,
                UIPrefabIssueSeverity.Warning,
                "该预制体没有被任何 YooAsset 主资源采集器收录，运行时无法按地址加载。只被静态/依赖采集器收录时同样无法按地址加载；若项目另有加载方式可忽略本条。",
                root.name));
        }

        #endregion

        #region Rule 009 — Tip Text

        private static void ValidateTip(
            GameObject root,
            UITipItem rootTip,
            List<UIPrefabIssue> issues,
            Func<Component, bool> isTmpText)
        {
            if (rootTip == null)
                return;

            if (HasAnyTmpText(root.transform, isTmpText))
                return;

            issues.Add(new UIPrefabIssue(
                UIPrefabRuleIds.TipWithoutTmpText,
                UIPrefabIssueSeverity.Warning,
                "Tip 预制体的根及后代没有 TMP_Text；UITipItem.Begin 会报错并直接返回，Tip 显示不出内容。",
                root.name));
        }

        #endregion

        #region Helpers

        private static bool TryGetConventionExpectation(
            string name,
            out Type expectedType,
            out string expectedName,
            out bool isTextPrefix)
        {
            // 顺序必须与 UIPanelBinding.CacheComponentsRecursive 的 else-if 链一致：
            // 同一个节点名命中多个前缀时，只有最靠前的那个生效。
            if (name.StartsWith("txt_", StringComparison.OrdinalIgnoreCase))
            {
                expectedType = typeof(Text);
                expectedName = "Text";
                isTextPrefix = true;
                return true;
            }

            if (name.StartsWith("img_", StringComparison.OrdinalIgnoreCase))
            {
                expectedType = typeof(Image);
                expectedName = "Image";
                isTextPrefix = false;
                return true;
            }

            if (name.StartsWith("btn_", StringComparison.OrdinalIgnoreCase))
            {
                expectedType = typeof(Button);
                expectedName = "Button";
                isTextPrefix = false;
                return true;
            }

            if (name.StartsWith("sld_", StringComparison.OrdinalIgnoreCase))
            {
                expectedType = typeof(Slider);
                expectedName = "Slider";
                isTextPrefix = false;
                return true;
            }

            if (name.StartsWith("tgl_", StringComparison.OrdinalIgnoreCase))
            {
                expectedType = typeof(Toggle);
                expectedName = "Toggle";
                isTextPrefix = false;
                return true;
            }

            expectedType = null;
            expectedName = null;
            isTextPrefix = false;
            return false;
        }

        private static bool HasTmpText(Transform node, Func<Component, bool> isTmpText)
        {
            var components = node.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && isTmpText(components[i]))
                    return true;
            }

            return false;
        }

        private static bool HasAnyTmpText(Transform root, Func<Component, bool> isTmpText)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (HasTmpText(transforms[i], isTmpText))
                    return true;
            }

            return false;
        }

        private static string BuildPath(Transform root, Transform node)
        {
            if (root == null)
                return node != null ? node.name : string.Empty;

            if (node == null || node == root)
                return root.name;

            var names = new List<string>(8);
            var current = node;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Add(root.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Vector2.Distance(left, right) < 0.001f;
        }

        #endregion
    }
}
