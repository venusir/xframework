using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.Editor.BootstrapValidation;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 编辑器校验器的规则判定：多启动器、Profile 的 Host 提醒、Duplicate 内置阶段、
    /// Asset 阶段缺 RemoteServices。只读校验，不修改场景 / 资产。
    /// <para>Build Settings 的检查依赖工程设置，测试只断言「不抛异常」，不断言具体结果。</para>
    /// </summary>
    class BootstrapValidatorTests
    {
        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
        }

        #endregion

        [Test]
        public void MultipleLaunchers_ReportsError()
        {
            var first = new GameObject("launcher-a").AddComponent<DefaultGameLauncher>();
            var second = new GameObject("launcher-b").AddComponent<DefaultGameLauncher>();
            try
            {
                var issues = BootstrapValidator.Validate();

                var issue = Find(issues, BootstrapRuleIds.MultipleLaunchers);
                Assert.IsNotNull(issue, "同场景多个启动器应被报出");
                Assert.AreEqual(BootstrapIssueSeverity.Error, issue.Value.Severity);
            }
            finally
            {
                Object.DestroyImmediate(first.gameObject);
                Object.DestroyImmediate(second.gameObject);
            }
        }

        [Test]
        public void LauncherWithoutProfileAndUIRoot_ReportsInfos()
        {
            var launcher = new GameObject("launcher-zero").AddComponent<DefaultGameLauncher>();
            try
            {
                var issues = BootstrapValidator.Validate();

                Assert.IsNotNull(Find(issues, BootstrapRuleIds.LauncherWithoutProfile),
                    "无 Profile 应给出零配置提示");
                Assert.IsNotNull(Find(issues, BootstrapRuleIds.LauncherWithoutUIRoot),
                    "无 UI 根应给出提示");
            }
            finally
            {
                Object.DestroyImmediate(launcher.gameObject);
            }
        }

        [Test]
        public void ProfileWithHost_WithoutRegisteredAssetStage_ReportsWarning()
        {
            var profile = ScriptableObject.CreateInstance<BootstrapProfile>();
            var launcher = new GameObject("launcher-host").AddComponent<DefaultGameLauncher>();
            try
            {
                SetField(profile, "_assetPlayMode", AssetPlayMode.Host);
                SetField(launcher, "_profile", profile);

                var issues = BootstrapValidator.Validate();

                var issue = Find(issues, BootstrapRuleIds.HostWithoutRemoteServices);
                Assert.IsNotNull(issue, "Profile 配 Host 且登记表无 Asset 阶段时应提醒注入 RemoteServices");
                Assert.AreEqual(BootstrapIssueSeverity.Warning, issue.Value.Severity);
            }
            finally
            {
                Object.DestroyImmediate(launcher.gameObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AssetStageHostWithoutRemoteServices_ReportsError()
        {
            Bootstrap.Register(new AssetBootstrapStage(new AssetInitOptions
            {
                PlayMode = AssetPlayMode.Host,
            }));

            var issues = BootstrapValidator.Validate();

            var issue = Find(issues, BootstrapRuleIds.HostWithoutRemoteServices);
            Assert.IsNotNull(issue, "Host 模式缺 RemoteServices 应在启动前被报出");
            Assert.AreEqual(BootstrapIssueSeverity.Error, issue.Value.Severity);
        }

        [Test]
        public void DuplicateBuiltInStage_ReportsError()
        {
            Bootstrap.Register(new AssetBootstrapStage());
            Bootstrap.Register(new AssetBootstrapStage());

            var issues = BootstrapValidator.Validate();

            var issue = Find(issues, BootstrapRuleIds.DuplicateBuiltInStages);
            Assert.IsNotNull(issue, "同一内置阶段类型登记两次应被报出");
            Assert.AreEqual(BootstrapIssueSeverity.Error, issue.Value.Severity);
        }

        [Test]
        public void Validate_AlwaysReturnsReportWithoutThrowing()
        {
            Assert.DoesNotThrow(() =>
            {
                var issues = BootstrapValidator.Validate();
                Assert.IsNotNull(issues);
            });
        }

        #region Helpers

        private static BootstrapIssue? Find(List<BootstrapIssue> issues, string ruleId)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].RuleId == ruleId)
                    return issues[i];
            }

            return null;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"找不到字段 {fieldName}——它被改名了吗？改名请同步本夹具");
            field.SetValue(target, value);
        }

        #endregion
    }
}
