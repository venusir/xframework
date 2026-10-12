using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XFramework.XConfig;
using XFramework.XFileManager;
using XFramework.XLocalization;
using XFramework.XUI;
using XFramework.XUpdate;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 会话级复位的回归用例（**改前必红**）：门面在「会话 1 → 退出 → 会话 2」之后必须能重新初始化。
    /// <para><b>它锁的缺陷</b>：关闭域重载（Project Settings → Editor → Enter Play Mode Options →
    /// 取消 Reload Domain）时，上一播放会话的静态字段存活到第二会话，而门面的 <c>Initialize</c> 会因
    /// 门锁仍为真而**告警后早退**——<c>Destroy</c> 能复位一切，但正常游戏流程不会调它
    /// （「谁初始化谁清理」只在退出路径成立，而使用方在启动时调一次 <c>Initialize</c> 之后就不再碰了）。</para>
    /// <para><b>为什么能在单个播放会话里验</b>：缺陷需要「静态状态跨会话存活」与「<c>Initialize</c> 早退」
    /// 同时成立——后者在单会话里手动造得出来：<see cref="SimulateNewSession"/> 做两件事，
    /// 拆掉旧调度器（<c>UpdateManager.OnQuitting</c>，<c>Application.quitting</c> 上跑的正是它）
    /// 与调用各门面的 <c>ResetForNewSession</c>（那正是 <c>SubsystemRegistration</c> 上会跑的东西）。
    /// <b>拿掉任一复位即红</b>——这就是「改前必红」的证明路径。</para>
    /// </summary>
    [TestFixture]
    public class SessionResetTests
    {
        #region Fixture

        private GameObject _uiRoot;

        [SetUp]
        public void SetUp()
        {
            // 归零：不依赖上一个 fixture 的 TearDown（SetUp 中途失败时它不会执行）
            UIManager.Destroy();
            ConfigManager.Destroy();
            LocalizationManager.Destroy();
            FileManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _uiRoot = new GameObject("UIRoot_SessionResetTest", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            ConfigManager.Destroy();
            LocalizationManager.Destroy();
            FileManager.Destroy();

            if (_uiRoot != null)
                Object.DestroyImmediate(_uiRoot);
        }

        /// <summary>
        /// 模拟「退出播放 → 第二个会话开始」：<c>SubsystemRegistration</c> 上真正会跑的就是这些复位钩子。
        /// </summary>
        private static void SimulateNewSession()
        {
            UpdateManager.OnQuitting();
            UpdateManager.AutoInit();

            UIManager.ResetForNewSession();
            ConfigManager.ResetForNewSession();
            LocalizationManager.ResetForNewSession();
            FileManager.ResetForNewSession();
        }

        #endregion

        #region 门面逐个复位

        [Test]
        public void Ui_ReinitializesAfterSessionReset()
        {
            UIManager.Initialize(_uiRoot.transform);
            Assert.IsTrue(UIManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(UIManager.IsInitialized, "复位后门面应回到未初始化态");

            UIManager.Initialize(_uiRoot.transform);

            Assert.IsTrue(UIManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警后早退");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0), "帧驱动器应重新进调度器");
        }

        [Test]
        public void Config_ReinitializesAfterSessionReset()
        {
            ConfigManager.Initialize();
            Assert.IsTrue(ConfigManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(ConfigManager.IsInitialized, "复位后门面应回到未初始化态");

            ConfigManager.Initialize();

            Assert.IsTrue(ConfigManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警后早退");
        }

        [Test]
        public void Localization_ReinitializesAfterSessionReset()
        {
            LocalizationManager.Initialize("zh_Hans", new Dictionary<string, string>());
            Assert.IsTrue(LocalizationManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(LocalizationManager.IsInitialized, "复位后门面应回到未初始化态");

            LocalizationManager.Initialize("zh_Hans", new Dictionary<string, string>());

            Assert.IsTrue(LocalizationManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警后早退");
        }

        [Test]
        public void File_ReinitializesAfterSessionReset()
        {
            FileManager.Initialize();
            Assert.IsTrue(FileManager.IsInitialized, "前提校验：会话 1 应当初始化成功");

            SimulateNewSession();
            Assert.IsFalse(FileManager.IsInitialized, "复位后门面应回到未初始化态");

            FileManager.Initialize();

            Assert.IsTrue(FileManager.IsInitialized, "第二个会话必须能重新初始化，而不是告警后早退");
        }

        #endregion
    }
}
