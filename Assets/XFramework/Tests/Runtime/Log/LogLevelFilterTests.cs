using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XLog.Tests
{
    /// <summary>
    /// 分级过滤：全局档、分类覆盖、以及 <c>IsEnabled</c> 与实际产出的一致性。
    /// <para><b>为什么「静默」要用 <c>NoUnexpectedReceived</c> 断言</b>：低于 Warning 的日志走
    /// <c>LogType.Log</c>，Unity 测试框架不会把意外 Log 判失败（只有 Warning/Error 会），
    /// 不显式查一次就等于没断言。</para>
    /// </summary>
    [TestFixture]
    public class LogLevelFilterTests
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

        #region 全局档

        [Test]
        public void GlobalMinimum_DropsLowerLevels()
        {
            LogManager.MinimumLevel = LogLevel.Warning;

            LogManager.Verbose(LogCategories.Save, "verbose 应被丢弃");
            LogManager.Debug(LogCategories.Save, "debug 应被丢弃");
            LogManager.Info(LogCategories.Save, "info 应被丢弃");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void GlobalMinimum_StillEmitsWarningAndAbove()
        {
            LogManager.MinimumLevel = LogLevel.Warning;

            LogAssert.Expect(LogType.Warning, "[Save] 警告");
            LogAssert.Expect(LogType.Error, "[Save] 错误");
            LogAssert.Expect(LogType.Error, "[Save] 致命");

            LogManager.Warning(LogCategories.Save, "警告");
            LogManager.Error(LogCategories.Save, "错误");
            LogManager.Fatal(LogCategories.Save, "致命");
        }

        #endregion

        #region 分类覆盖

        [Test]
        public void CategoryLevel_SilencesOnlyThatCategory()
        {
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            LogAssert.Expect(LogType.Warning, "[Data] 别的分类照常");
            LogManager.Warning(LogCategories.Save, "被静默的分类");
            LogManager.Warning(LogCategories.Data, "别的分类照常");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CategoryLevel_OverridesGlobalInBothDirections()
        {
            // 放宽：全局 Error，某分类单独开到 Info
            LogManager.MinimumLevel = LogLevel.Error;
            LogManager.SetCategoryLevel(LogCategories.Data, LogLevel.Info);

            LogAssert.Expect(LogType.Log, "[Data] 放宽后可见");
            LogManager.Info(LogCategories.Data, "放宽后可见");

            // 收紧：全局 Verbose，某分类单独收到 Error
            LogManager.MinimumLevel = LogLevel.Verbose;
            LogManager.SetCategoryLevel(LogCategories.Data, LogLevel.Error);

            LogManager.Info(LogCategories.Data, "被收紧的信息不该出现");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ResetCategoryLevel_FollowsGlobalAgain()
        {
            LogManager.MinimumLevel = LogLevel.Warning;
            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);

            LogManager.ResetCategoryLevel(LogCategories.Save);

            LogAssert.Expect(LogType.Warning, "[Save] 回来了");
            LogManager.Warning(LogCategories.Save, "回来了");
        }

        #endregion

        #region IsEnabled 一致性

        [Test]
        public void IsEnabled_AgreesWithGlobalAndCategoryLevels()
        {
            LogManager.MinimumLevel = LogLevel.Warning;

            Assert.IsFalse(LogManager.IsEnabled(LogLevel.Info, LogCategories.Save));
            Assert.IsTrue(LogManager.IsEnabled(LogLevel.Warning, LogCategories.Save));

            LogManager.SetCategoryLevel(LogCategories.Save, LogLevel.Off);
            Assert.IsFalse(LogManager.IsEnabled(LogLevel.Error, LogCategories.Save));

            LogManager.SetCategoryLevel(LogCategories.Data, LogLevel.Verbose);
            Assert.IsTrue(LogManager.IsEnabled(LogLevel.Verbose, LogCategories.Data));
        }

        [Test]
        public void OffAsWriteLevel_ProducesNothing()
        {
            LogManager.Log(LogLevel.Off, LogCategories.Save, "Off 不是可写入的级别");

            LogAssert.NoUnexpectedReceived();
        }

        #endregion

        #region 未注册分类

        [Test]
        public void UnregisteredCategory_LogsWithVisibleMarker()
        {
            LogAssert.Expect(LogType.Warning, "[Unregistered] 未注册也能写");
            LogManager.Warning(default(LogCategory), "未注册也能写");
        }

        [Test]
        public void SetCategoryLevel_OnUnregisteredCategory_Throws()
        {
            Assert.Throws<ArgumentException>(() => LogManager.SetCategoryLevel(default(LogCategory), LogLevel.Off));
            Assert.Throws<ArgumentException>(() => LogManager.ResetCategoryLevel(default(LogCategory)));
        }

        #endregion
    }
}
