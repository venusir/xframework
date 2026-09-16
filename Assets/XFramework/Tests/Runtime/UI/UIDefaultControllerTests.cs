using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.TestTools;
using XFramework.XUI.Controller;
using XFramework.XUI.View;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 默认控制器的日志开关测试。
    /// <para>它每次拦截都打日志，意味着每开一个面板就是五条带字符串插值的日志——生产环境纯属噪音。
    /// 现默认静默，需要观察生命周期流程时用 <c>new UIDefaultController(verbose: true)</c>。</para>
    /// </summary>
    [TestFixture]
    public class UIDefaultControllerTests
    {
        [Test]
        public void Default_AllowsEverything()
        {
            var controller = new UIDefaultController();

            Assert.IsTrue(controller.OnBeforeOpenAsync(typeof(FakePanel), "ui/a", 100, null)
                .GetAwaiter().GetResult(), "默认放行打开");
            Assert.IsTrue(controller.OnBeforeCloseAsync(typeof(FakePanel), null, false)
                .GetAwaiter().GetResult(), "默认放行关闭");
        }

        [Test]
        public void Verbose_StillAllowsEverything()
        {
            var controller = new UIDefaultController(verbose: true);

            Assert.IsTrue(controller.OnBeforeOpenAsync(typeof(FakePanel), "ui/a", 100, null)
                .GetAwaiter().GetResult(), "verbose 只影响日志，不改变返回值");
            Assert.IsTrue(controller.OnBeforeCloseAsync(typeof(FakePanel), null, true)
                .GetAwaiter().GetResult());
        }

        [Test]
        public void Verbose_LogsEachStage()
        {
            LogAssert.Expect(UnityEngine.LogType.Log, new Regex("允许打开面板"));
            LogAssert.Expect(UnityEngine.LogType.Log, new Regex("允许关闭面板"));
            LogAssert.Expect(UnityEngine.LogType.Log, new Regex("所有面板已关闭"));

            var controller = new UIDefaultController(verbose: true);
            controller.OnBeforeOpenAsync(typeof(FakePanel), "ui/a", 100, null).GetAwaiter().GetResult();
            controller.OnBeforeCloseAsync(typeof(FakePanel), null, false).GetAwaiter().GetResult();
            controller.OnAllPanelsClosedAsync().GetAwaiter().GetResult();
        }
    }
}
