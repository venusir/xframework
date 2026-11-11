using System;
using NUnit.Framework;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// Integration 源码的结构守卫。真实 <c>FmodEventBackend</c> 只在 <c>XFRAMEWORK_FMOD</c> 下编译，
    /// 干净克隆的默认全量不编译它；这条守卫用源码文本锁定「不能靠 RuntimeManager 初始化状态早退」，
    /// 是在无 SDK 环境里唯一能自动红绿的证据（行为冒烟见 <c>XFW_FMOD_GLOBAL_PARAM</c>）。
    /// </summary>
    [TestFixture]
    public class FmodIntegrationSourceTests
    {
        [Test]
        public void SetGlobalParameter_DoesNotEarlyReturnOnRuntimeManagerInit()
        {
            string source = FmodTestPaths.ReadText("Runtime/Integration/FmodEventBackend.cs");
            if (source == null)
                Assert.Ignore("包根不可定位；跳过 Integration 源码结构守卫。");

            int start = source.IndexOf("public bool SetGlobalParameter", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "找不到 SetGlobalParameter 的方法声明");

            int end = source.IndexOf("public bool LoadBank", start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start), "找不到 SetGlobalParameter 之后的 LoadBank");

            string body = source.Substring(start, end - start);
            Assert.That(body, Does.Not.Contain("!RuntimeManager.IsInitialized"),
                "SetGlobalParameter 不得在 RuntimeManager 初始化前早退：StudioSystem 会懒初始化，" +
                "反过来会让「先设全局参数再起播」的第一个调用静默失败（R5-FMD-001）");
        }
    }
}
