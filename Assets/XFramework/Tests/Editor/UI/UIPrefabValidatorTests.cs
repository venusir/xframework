using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XUI;
using XFramework.XUI.Data;
using XFramework.XUI.View;

namespace XFramework.Editor.UI.Tests
{
    /// <summary>
    /// <see cref="UIPrefabValidator"/> 的规则引擎测试。全部对内存 GameObject 跑，不创建任何资产。
    /// </summary>
    class UIPrefabValidatorTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        #region Rule 001 — Root Component

        [Test]
        public void RootComponent_PanelOnChild_ReportsError()
        {
            var root = CreateRoot();
            CreateChild(root.transform, "Panel").AddComponent<TestPanel>();

            var issues = Validate(root);

            Assert.IsTrue(TryFindIssue(issues, UIPrefabRuleIds.RootComponentMissing, out var issue));
            Assert.AreEqual(UIPrefabIssueSeverity.Error, issue.Severity);
        }

        [Test]
        public void RootComponent_PanelOnRoot_NoError()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();

            var issues = Validate(root);

            Assert.IsFalse(HasIssue(issues, UIPrefabRuleIds.RootComponentMissing));
        }

        [Test]
        public void RootComponent_HudOnChild_ReportsError()
        {
            var root = CreateRoot();
            CreateChild(root.transform, "Hud").AddComponent<TestHudItem>();

            Assert.IsTrue(HasIssue(Validate(root), UIPrefabRuleIds.RootComponentMissing));
        }

        [Test]
        public void RootComponent_TipOnChild_ReportsError()
        {
            var root = CreateRoot();
            CreateChild(root.transform, "Tip").AddComponent<UITipItem>();

            Assert.IsTrue(HasIssue(Validate(root), UIPrefabRuleIds.RootComponentMissing));
        }

        #endregion

        #region Rule 002 — Binding Placement

        [Test]
        public void Binding_OnChild_ReportsError()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            CreateChild(root.transform, "Binding").AddComponent<UIPanelBinding>();

            Assert.IsTrue(TryFindIssue(Validate(root), UIPrefabRuleIds.BindingNotOnRoot, out var issue));
            Assert.AreEqual(UIPrefabIssueSeverity.Error, issue.Severity);
        }

        [Test]
        public void Binding_OnRoot_NoError()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();

            Assert.IsFalse(HasIssue(Validate(root), UIPrefabRuleIds.BindingNotOnRoot));
        }

        [Test]
        public void Binding_MultipleOnRoot_ReportsWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();
            root.AddComponent<UIPanelBinding>();

            Assert.IsTrue(TryFindIssue(Validate(root), UIPrefabRuleIds.BindingNotOnRoot, out var issue));
            Assert.AreEqual(UIPrefabIssueSeverity.Warning, issue.Severity);
        }

        #endregion

        #region Rules 003–005 — Convention Nodes

        [Test]
        public void Depth_At5_NoWarning()
        {
            var root = CreateConventionChain(5, "txt_Value", addText: true);

            Assert.IsFalse(HasIssue(Validate(root), UIPrefabRuleIds.ConventionDepthExceeded));
        }

        [Test]
        public void Depth_At6_ReportsWarning()
        {
            var root = CreateConventionChain(6, "txt_Value", addText: true);

            Assert.IsTrue(HasIssue(Validate(root), UIPrefabRuleIds.ConventionDepthExceeded));
        }

        [Test]
        public void Depth_At6_DoesNotReportTypeMismatch()
        {
            var root = CreateConventionChain(6, "img_Value", addText: false);

            var issues = Validate(root);

            Assert.IsTrue(HasIssue(issues, UIPrefabRuleIds.ConventionDepthExceeded));
            Assert.IsFalse(HasIssue(issues, UIPrefabRuleIds.ConventionTypeMismatch));
        }

        [Test]
        public void Convention_ImgWithoutImage_ReportsMismatch()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();
            CreateChild(root.transform, "img_Icon");

            Assert.IsTrue(HasIssue(Validate(root), UIPrefabRuleIds.ConventionTypeMismatch));
        }

        [Test]
        public void Convention_TxtWithTmp_ReportsActionableTmpHint()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();
            CreateChild(root.transform, "txt_Title").AddComponent<FakeTmpText>();

            var issues = Validate(root, isTmpText: component => component is FakeTmpText);

            Assert.IsTrue(TryFindIssue(issues, UIPrefabRuleIds.ConventionTypeMismatch, out var issue));
            StringAssert.Contains("TMP", issue.Message);
            StringAssert.Contains("UIBinder.BindToText", issue.Message);
        }

        [Test]
        public void Convention_ExpectedComponent_NoMismatch()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();
            CreateChild(root.transform, "txt_Title").AddComponent<Text>();

            Assert.IsFalse(HasIssue(Validate(root), UIPrefabRuleIds.ConventionTypeMismatch));
        }

        [Test]
        public void Convention_DuplicateName_ReportsWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();
            CreateChild(root.transform, "txt_Value").AddComponent<Text>();
            CreateChild(root.transform, "txt_Value").AddComponent<Text>();

            Assert.IsTrue(HasIssue(Validate(root), UIPrefabRuleIds.ConventionNameDuplicated));
        }

        [Test]
        public void Convention_DuplicateNameDifferentCase_ReportsWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();
            CreateChild(root.transform, "txt_Value").AddComponent<Text>();
            CreateChild(root.transform, "TXT_VALUE").AddComponent<Text>();

            Assert.IsTrue(HasIssue(Validate(root), UIPrefabRuleIds.ConventionNameDuplicated));
        }

        [Test]
        public void Convention_MismatchedDuplicate_NoDuplicateWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();
            CreateChild(root.transform, "txt_Value").AddComponent<Text>();
            CreateChild(root.transform, "txt_Value");

            var issues = Validate(root);

            Assert.IsTrue(HasIssue(issues, UIPrefabRuleIds.ConventionTypeMismatch));
            Assert.IsFalse(HasIssue(issues, UIPrefabRuleIds.ConventionNameDuplicated));
        }

        #endregion

        #region Rules 006–007 — Panel Transform

        [Test]
        public void RectTransform_Default_ReportsInfo()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100f, 100f);

            Assert.IsTrue(TryFindIssue(Validate(root), UIPrefabRuleIds.DefaultRootRectTransform, out var issue));
            Assert.AreEqual(UIPrefabIssueSeverity.Info, issue.Severity);
        }

        [Test]
        public void RectTransform_Stretched_NoInfo()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;

            Assert.IsFalse(HasIssue(Validate(root), UIPrefabRuleIds.DefaultRootRectTransform));
        }

        [Test]
        public void RootTransform_NonIdentity_ReportsWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.transform.localScale = new Vector3(2f, 2f, 2f);

            Assert.IsTrue(TryFindIssue(Validate(root), UIPrefabRuleIds.NonIdentityRootTransform, out var issue));
            Assert.AreEqual(UIPrefabIssueSeverity.Warning, issue.Severity);
        }

        [Test]
        public void RootTransform_Identity_NoWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();

            Assert.IsFalse(HasIssue(Validate(root), UIPrefabRuleIds.NonIdentityRootTransform));
        }

        #endregion

        #region Rule 008 — YooAsset Collection

        [Test]
        public void Address_Disabled_NoCoverageWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();

            Assert.IsFalse(HasIssue(Validate(root, addressCheckPerformed: false), UIPrefabRuleIds.NotCollectedByYooAsset));
        }

        [Test]
        public void Address_EnabledUncollected_ReportsWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();

            var issues = Validate(root, addressCheckPerformed: true, addresses: Array.Empty<UIPrefabAddressEntry>());

            Assert.IsTrue(HasIssue(issues, UIPrefabRuleIds.NotCollectedByYooAsset));
        }

        [Test]
        public void Address_EnabledCollected_NoWarning()
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();

            var addresses = new[]
            {
                new UIPrefabAddressEntry("DefaultPackage", "ui/test", "Assets/Test.prefab", "guid"),
            };

            Assert.IsFalse(HasIssue(
                Validate(root, addressCheckPerformed: true, addresses: addresses),
                UIPrefabRuleIds.NotCollectedByYooAsset));
        }

        #endregion

        #region Rule 009 — Tip Text

        [Test]
        public void Tip_WithoutTmp_ReportsWarning()
        {
            var root = CreateRoot();
            root.AddComponent<UITipItem>();

            Assert.IsTrue(HasIssue(
                Validate(root, isTmpText: component => component is FakeTmpText),
                UIPrefabRuleIds.TipWithoutTmpText));
        }

        [Test]
        public void Tip_WithTmp_NoWarning()
        {
            var root = CreateRoot();
            root.AddComponent<UITipItem>();
            CreateChild(root.transform, "Label").AddComponent<FakeTmpText>();

            Assert.IsFalse(HasIssue(
                Validate(root, isTmpText: component => component is FakeTmpText),
                UIPrefabRuleIds.TipWithoutTmpText));
        }

        #endregion

        #region Candidate & Probe

        [Test]
        public void Candidate_Detection()
        {
            Assert.IsFalse(UIPrefabCandidateDetector.IsCandidate(CreateRoot("Plain")));

            var panelRoot = CreateRoot("PanelRoot");
            panelRoot.AddComponent<TestPanel>();
            Assert.IsTrue(UIPrefabCandidateDetector.IsCandidate(panelRoot));

            var hudRoot = CreateRoot("HudRoot");
            hudRoot.AddComponent<TestHudItem>();
            Assert.IsTrue(UIPrefabCandidateDetector.IsCandidate(hudRoot));

            var tipRoot = CreateRoot("TipRoot");
            tipRoot.AddComponent<UITipItem>();
            Assert.IsTrue(UIPrefabCandidateDetector.IsCandidate(tipRoot));

            var childPanelRoot = CreateRoot("ChildPanelRoot");
            CreateChild(childPanelRoot.transform, "Child").AddComponent<TestPanel>();
            Assert.IsTrue(UIPrefabCandidateDetector.IsCandidate(childPanelRoot));

            var bindingOnly = CreateRoot("BindingOnly");
            bindingOnly.AddComponent<UIPanelBinding>();
            Assert.IsFalse(UIPrefabCandidateDetector.IsCandidate(bindingOnly));
        }

        [Test]
        public void TmpProbe_RecognizesRealTmpTextType()
        {
            var tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
            if (tmpType == null)
                Assert.Ignore("未找到 TMPro.TMP_Text（TMP 程序集未加载）。");

            Assert.IsTrue(UIPrefabTmpTextProbe.IsTmpTextType(tmpType));
            Assert.IsFalse(UIPrefabTmpTextProbe.IsTmpTextType(typeof(Text)));
        }

        #endregion

        #region Rule Ids & Report

        [Test]
        public void RuleIds_AreUniqueAndWellFormed()
        {
            var fields = typeof(UIPrefabRuleIds).GetFields(BindingFlags.Public | BindingFlags.Static);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            Assert.AreEqual(9, fields.Length);

            for (int i = 0; i < fields.Length; i++)
            {
                var value = (string)fields[i].GetValue(null);
                Assert.IsTrue(Regex.IsMatch(value, @"^UI-PREFAB-\d{3}$"), $"规则号格式不正确：{value}");
                Assert.IsTrue(seen.Add(value), $"规则号重复：{value}");
            }
        }

        [Test]
        public void Report_CountsAndSorting()
        {
            var report = new UIPrefabValidationReport();

            var errorEntry = new UIPrefabValidationEntry("Assets/B.prefab", "b");
            errorEntry.Issues.Add(new UIPrefabIssue(
                UIPrefabRuleIds.RootComponentMissing, UIPrefabIssueSeverity.Error, "error"));

            var warningEntry = new UIPrefabValidationEntry("Assets/A.prefab", "a");
            warningEntry.Issues.Add(new UIPrefabIssue(
                UIPrefabRuleIds.ConventionTypeMismatch, UIPrefabIssueSeverity.Warning, "warning"));

            var cleanEntry = new UIPrefabValidationEntry("Assets/C.prefab", "c");

            report.AddEntry(warningEntry);
            report.AddEntry(cleanEntry);
            report.AddEntry(errorEntry);
            report.SortEntries();

            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(1, report.WarningCount);
            Assert.AreEqual(1, report.CleanCount);
            Assert.AreEqual("Assets/B.prefab", report.Entries[0].AssetPath);
            Assert.AreEqual("Assets/A.prefab", report.Entries[1].AssetPath);
            Assert.AreEqual("Assets/C.prefab", report.Entries[2].AssetPath);
            StringAssert.Contains("候选 3 个", report.BuildSummary());
        }

        #endregion

        #region Helpers

        private GameObject CreateRoot(string name = "Root")
        {
            var root = new GameObject(name, typeof(RectTransform));
            _created.Add(root);
            return root;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }

        private GameObject CreateConventionChain(int depth, string leafName, bool addText)
        {
            var root = CreateRoot();
            root.AddComponent<TestPanel>();
            root.AddComponent<UIPanelBinding>();

            var current = root.transform;
            for (int i = 1; i <= depth; i++)
            {
                var nodeName = i == depth ? leafName : $"Node{i}";
                current = CreateChild(current, nodeName).transform;
            }

            if (addText)
                current.gameObject.AddComponent<Text>();

            return root;
        }

        private static List<UIPrefabIssue> Validate(
            GameObject root,
            bool addressCheckPerformed = false,
            IReadOnlyList<UIPrefabAddressEntry> addresses = null,
            Func<Component, bool> isTmpText = null)
        {
            return UIPrefabValidator.Validate(root, addresses, addressCheckPerformed, isTmpText);
        }

        private static bool HasIssue(IReadOnlyList<UIPrefabIssue> issues, string ruleId)
        {
            return TryFindIssue(issues, ruleId, out _);
        }

        private static bool TryFindIssue(
            IReadOnlyList<UIPrefabIssue> issues,
            string ruleId,
            out UIPrefabIssue issue)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].RuleId == ruleId)
                {
                    issue = issues[i];
                    return true;
                }
            }

            issue = default;
            return false;
        }

        #endregion

        #region Test Doubles

        private sealed class TestPanel : UIPanelBase
        {
            protected override UniTask OnOpen(object userData) => UniTask.CompletedTask;

            protected override UniTask OnClose() => UniTask.CompletedTask;
        }

        private sealed class TestHudItem : UIHudItem
        {
        }

        private sealed class FakeTmpText : MonoBehaviour
        {
        }

        #endregion
    }
}
