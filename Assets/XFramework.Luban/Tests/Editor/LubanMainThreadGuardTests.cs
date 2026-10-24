using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XConfig.Luban;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// <see cref="LubanConfig"/> 门面入口的主线程断言测试。
    /// <para>断言本体包在 <c>#if UNITY_EDITOR</c> 里，故与核心 Asset/Message/Pipeline
    /// 的同类用例一样落在 EditMode 测试程序集。</para>
    /// <para>闩锁是静态字段，用门面自己的 <see cref="LubanConfig.ResetForNewSession"/>
    /// 复位；子线程一律 Join，验的是线程归属而非并发安全。</para>
    /// </summary>
    [TestFixture]
    public sealed class LubanMainThreadGuardTests
    {
        private static readonly MethodInfo AssertMainThreadMethod = typeof(LubanConfig).GetMethod(
            "AssertMainThread", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            LubanConfig.DataProviderFactory = null;
            LubanConfig.Destroy();
            LubanConfig.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            LubanConfig.DataProviderFactory = null;
            LubanConfig.Destroy();
        }

        [Test]
        public void AssertMainThread_OnMainThread_DoesNotLog()
        {
            AssertMainThreadMethod.Invoke(null, new object[] { "InitializeAsync" });

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AssertMainThread_OffMainThread_LogsOncePerLatch()
        {
            LogAssert.Expect(LogType.Error,
                new Regex(Regex.Escape("[LubanConfig] Destroy 必须在 Unity 主线程调用")));

            var thread = new Thread(() =>
            {
                AssertMainThreadMethod.Invoke(null, new object[] { "Destroy" });
                AssertMainThreadMethod.Invoke(null, new object[] { "Destroy" });
            });
            thread.Start();
            thread.Join();
        }

        [Test]
        public void Destroy_OffMainThread_LogsMainThreadError()
        {
            LogAssert.Expect(LogType.Error,
                new Regex(Regex.Escape("[LubanConfig] Destroy 必须在 Unity 主线程调用")));

            var thread = new Thread(() => LubanConfig.Destroy());
            thread.Start();
            thread.Join();
        }

        [Test]
        public void ResetForNewSession_ReArmsTheLatch()
        {
            LogAssert.Expect(LogType.Error,
                new Regex(Regex.Escape("[LubanConfig] Destroy 必须在 Unity 主线程调用")));

            var thread = new Thread(() =>
                AssertMainThreadMethod.Invoke(null, new object[] { "Destroy" }));
            thread.Start();
            thread.Join();

            LubanConfig.ResetForNewSession();

            LogAssert.Expect(LogType.Error,
                new Regex(Regex.Escape("[LubanConfig] Destroy 必须在 Unity 主线程调用")));

            thread = new Thread(() =>
                AssertMainThreadMethod.Invoke(null, new object[] { "Destroy" }));
            thread.Start();
            thread.Join();
        }
    }
}
