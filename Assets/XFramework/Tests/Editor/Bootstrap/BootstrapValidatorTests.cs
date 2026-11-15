using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.Editor.BootstrapValidation;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 编辑器校验器的规则判定：多启动器、Duplicate 内置阶段、Asset 阶段缺 RemoteServices。
    /// 只读校验，不修改场景 / 资产。
    /// <para>Build Settings 的检查依赖工程设置，测试只断言「不抛异常」，不断言具体结果。</para>
    /// </summary>
    class BootstrapValidatorTests
    {
        #region Test Doubles

        /// <summary>探针：什么都不登记——校验器只关心「场景里有没有 GameLauncher」。</summary>
        private sealed class ProbeLauncher : GameLauncher
        {
            protected override void ConfigureStages()
            {
            }
        }

        #endregion

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
            var first = new GameObject("launcher-a").AddComponent<ProbeLauncher>();
            var second = new GameObject("launcher-b").AddComponent<ProbeLauncher>();
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

        #endregion
    }
}
