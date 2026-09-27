using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XMessage;
using XFramework.XMessage.Internal;

namespace XFramework.XMessage.Tests
{
    /// <summary>
    /// 发布/订阅入口的主线程断言测试。守卫仅在 <c>#if UNITY_EDITOR</c> 下编译,故本 fixture 落在
    /// <c>Tests/Editor/</c>(对照 Pipeline 的同类用例 <c>PipelineTests.WorkerThreadWrite_LogsMainThreadError</c>)。
    /// <para>
    /// 两条断言的分工:① 守卫本体用局部变量驱动,零全局状态、零顺序依赖;其中主线程那条是<b>负向控制</b>
    /// ——缺了它,一个「恒打日志」的守卫也能让越线程用例通过。② 接线用例(子线程调用真实入口)不可省:
    /// 守卫写好了却没接上,只有它能发现。
    /// </para>
    /// <para>
    /// 子线程一律 <c>Join</c> 阻塞主线程后才继续,故与主线程无并发访问——本 fixture 验的是线程归属,
    /// 不是并发安全。
    /// </para>
    /// </summary>
    [TestFixture]
    public class MessageMainThreadGuardTests
    {
        #region Test Doubles

        /// <summary>接线用例专用消息类型:私有嵌套,不会与其它 fixture 的通道互相干扰。</summary>
        private sealed class ProbeMessage
        {
        }

        #endregion

        [SetUp]
        public void SetUp()
        {
            MessageManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            MessageManager.Clear();
        }

        #region 守卫本体

        [Test]
        public void AssertEntry_OnMainThread_DoesNotLog()
        {
            var logged = false;

            MainThreadGuard.AssertEntry(ref logged, "Publish");

            Assert.IsFalse(logged, "主线程调用不得置位首错闩锁");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AssertEntry_OffMainThread_LogsOncePerLatch()
        {
            var logged = false;
            var thread = new Thread(() =>
            {
                MainThreadGuard.AssertEntry(ref logged, "Publish");
                // 第二次必须静默:Expect 只声明一条,多打一条会以 UnhandledLogMessageException 暴露
                MainThreadGuard.AssertEntry(ref logged, "Publish");
            });

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message]")));
            thread.Start();
            thread.Join();

            Assert.IsTrue(logged, "越线程调用应置位首错闩锁");
        }

        #endregion

        #region 接线(入口真的调了守卫吗)

        [Test]
        public void Publish_OffMainThread_LogsMainThreadError()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[Message] Publish 必须在 Unity 主线程调用")));

            var thread = new Thread(() => MessageManager.Publish(new ProbeMessage()));
            thread.Start();
            thread.Join();
        }

        [Test]
        public void Subscribe_OffMainThread_LogsMainThreadError()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("必须在 Unity 主线程调用")));

            var thread = new Thread(() => MessageManager.Subscribe<ProbeMessage>(_ => { }));
            thread.Start();
            thread.Join();
        }

        [Test]
        public void SubscribeAsync_CancelledTokenEarlyReturn_DoesNotLog()
        {
            // 刻意留白的边界:令牌已取消时这四条重载不触碰共享状态,故不受守卫管辖。
            // 若哪天把守卫挪到它们之前,本用例会因多出一条错误日志而红——那时应同步更新
            // MainThreadGuard 的覆盖边界说明,而不是删掉本用例。
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var thread = new Thread(() =>
                MessageManager.SubscribeAsync<ProbeMessage>((msg, ct) => Cysharp.Threading.Tasks.UniTask.CompletedTask, cts.Token));
            thread.Start();
            thread.Join();

            LogAssert.NoUnexpectedReceived();
        }

        #endregion
    }
}
