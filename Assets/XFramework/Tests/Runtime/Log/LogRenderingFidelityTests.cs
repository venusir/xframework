using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 渲染逐字保真：控制台文本 <c>[Category] message</c> 与 LogType 映射，必须与迁移前手抄前缀的
    /// <c>Debug.Log/LogWarning/LogError</c> 完全一致。
    /// <para><b>这是「193 处迁移靠测试全绿验证」这条策略的执行者</b>：本 fixture 里的断言形态
    /// （精确字符串 + 正则）与既有 30+ 处 <c>LogAssert</c> 相同，任何渲染差异都会在这里先红，
    /// 而不是等到某个模块的集成测试。</para>
    /// </summary>
    [TestFixture]
    public class LogRenderingFidelityTests
    {
        #region Fixture

        [SetUp]
        public void SetUp()
        {
            LogTestKit.SetUp();
        }

        [TearDown]
        public void TearDown()
        {
            LogTestKit.TearDown();
        }

        #endregion

        #region 前缀与 LogType 映射

        [Test]
        public void Warning_RendersBracketPrefix_AndMapsToLogWarning()
        {
            LogAssert.Expect(LogType.Warning, "[Save] 跳过空存档文件: slot/1.sav");
            LogManager.Warning(LogCategories.Save, "跳过空存档文件: {0}", "slot/1.sav");
        }

        /// <summary>这条直接复刻既有的 <c>PipelineLoggingTests</c> 断言形态：迁移后它必须仍然绿。</summary>
        [Test]
        public void Info_RendersBracketPrefix_AndMapsToLog()
        {
            LogAssert.Expect(LogType.Log, "[Pipeline] Stage 'A' start");
            LogManager.Info(LogCategories.Pipeline, "Stage '{0}' start", "A");
        }

        [Test]
        public void Error_MapsToLogError()
        {
            LogAssert.Expect(LogType.Error, "[Data] 未注册的数据块: demo");
            LogManager.Error(LogCategories.Data, "未注册的数据块: {0}", "demo");
        }

        [Test]
        public void Fatal_AlsoMapsToLogError()
        {
            LogAssert.Expect(LogType.Error, "[GameLauncher] 启动失败");
            LogManager.Fatal(LogCategories.GameLauncher, "启动失败");
        }

        [Test]
        public void VerboseAndDebug_MapToLog()
        {
            LogAssert.Expect(LogType.Log, "[Timer] verbose");
            LogAssert.Expect(LogType.Log, "[Timer] debug");

            LogManager.Verbose(LogCategories.Timer, "verbose");
            LogManager.Debug(LogCategories.Timer, "debug");
        }

        #endregion

        #region 模板形态

        [Test]
        public void TwoAndThreeArgs_FormatInHoleOrder()
        {
            LogAssert.Expect(LogType.Error, "[YooAssetManager] Failed to load asset 'ui/main' : 404");
            LogManager.Error(LogCategories.YooAssetManager, "Failed to load asset '{0}' : {1}", "ui/main", 404);

            LogAssert.Expect(LogType.Warning, "[Input] ActionMap 'Player' 未找到（已加载 3 张）");
            LogManager.Warning(LogCategories.Input, "ActionMap '{0}' 未找到（已加载 {2} 张）", "Player", "多余参数", 3);
        }

        [Test]
        public void FormatSpecifier_KeepsCultureSemantics()
        {
            LogAssert.Expect(LogType.Log, "[SettingsManager] 用时 1.50s");
            LogManager.Info(LogCategories.SettingsManager, "用时 {0:F2}s", 1.5f);
        }

        [Test]
        public void EscapedBraces_RenderAsSingleBraces()
        {
            LogAssert.Expect(LogType.Warning, "[SettingsManager] 版本 3 的结构: {Version, Data}");
            LogManager.Warning(LogCategories.SettingsManager, "版本 {0} 的结构: {{Version, Data}}", 3);
        }

        [Test]
        public void RawBraceMessage_IsNotTreatedAsTemplate()
        {
            LogAssert.Expect(LogType.Warning, "[Data] JSON: {");
            LogManager.Warning(LogCategories.Data, "JSON: {");
        }

        #endregion

        #region 异常条目

        [Test]
        public void Exception_AppendsExceptionText_AndMapsToError()
        {
            var exception = new InvalidOperationException("存档损坏");

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[Save\] 应用存档失败\nSystem\.InvalidOperationException: 存档损坏"));

            LogManager.Exception(LogCategories.Save, exception, "应用存档失败");
        }

        [Test]
        public void Exception_WithoutMessage_UsesExceptionMessage()
        {
            var exception = new InvalidOperationException("默认正文");

            LogAssert.Expect(LogType.Error, new Regex(@"\[Save\] 默认正文\nSystem\.InvalidOperationException: 默认正文"));

            LogManager.Exception(LogCategories.Save, exception);
        }

        [Test]
        public void Exception_NullException_ProducesNothing()
        {
            LogManager.Exception(LogCategories.Save, null, "不该出现");

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>级别可指定的重载：`Warning` 走 `Debug.LogWarning`，渲染约定与 Error 档完全一致。</summary>
        [Test]
        public void Exception_WithWarningLevel_MapsToLogWarning()
        {
            var exception = new InvalidOperationException("元数据损坏");

            LogAssert.Expect(LogType.Warning,
                new Regex(@"\[Save\] 解析存档元数据失败: slot/1\.sav\nSystem\.InvalidOperationException: 元数据损坏"));

            LogManager.Exception(LogLevel.Warning, LogCategories.Save, exception, "解析存档元数据失败: slot/1.sav");
        }

        /// <summary>档位过滤对异常入口同样生效——关档时连正文都不该出现。</summary>
        [Test]
        public void Exception_WithOffLevel_ProducesNothing()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            LogManager.Exception(LogLevel.Warning, LogCategories.Save,
                new InvalidOperationException("被静默的分类"), "不该出现");

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>null 异常什么都不做——两个重载同语义（调用点只有消息文本时用得上）。</summary>
        [Test]
        public void Exception_WithLevel_NullException_ProducesNothing()
        {
            LogManager.Exception(LogLevel.Warning, LogCategories.Save, null, "不该出现");

            LogAssert.NoUnexpectedReceived();
        }

        #endregion
    }
}
