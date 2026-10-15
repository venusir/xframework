using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAsset;

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// <see cref="AssetManager"/> 门面入口的主线程断言测试——补上这份守卫此前**零覆盖**的缺口。
    /// <para><b>为什么落在 <c>Tests/Editor/</c></b>：断言整段包在 <c>#if UNITY_EDITOR</c> 里（Release 零开销），
    /// PlayMode fixture 观测不到它——同 <c>MessageMainThreadGuardTests</c> 与 Pipeline 的同类用例。
    /// 跑本 fixture 必须显式给 <c>-Platform EditMode</c>，否则 0 用例是假绿。</para>
    /// <para><b>两条分工</b>：① 守卫本体（<c>private static AssertMainThread</c>）经反射驱动；
    /// 其中主线程那条是<b>负向控制</b>——缺了它，一个「恒打日志」的守卫也能让越线程用例通过。
    /// ② 接线用例（子线程调真实入口）不可省：守卫写好了却没接上，只有它能发现。</para>
    /// <para><b>闩锁是静态字段</b>（与 Message 的 <c>ref</c> 形参不同），故用门面自己的
    /// <see cref="AssetManager.ResetForNewSession"/> 复位——顺带让「会话复位让提示重新具备」这条
    /// 也进入覆盖（那一行赋值此前没有任何用例钉住）。</para>
    /// <para>子线程一律 <c>Join</c> 阻塞主线程后才继续，故与主线程无并发访问——本 fixture 验的是线程归属，
    /// 不是并发安全。</para>
    /// </summary>
    [TestFixture]
    public class AssetMainThreadGuardTests
    {
        #region Private Fields

        /// <summary>门面的主线程断言本体（private static，经反射取一次）。</summary>
        private static readonly MethodInfo AssertMainThreadMethod = typeof(AssetManager).GetMethod(
            "AssertMainThread", BindingFlags.Static | BindingFlags.NonPublic);

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp()
        {
            AssetManager.Destroy();
            AssetManager.ImplFactory = null;

            // 闩锁归零：每个用例零顺序依赖（这同时钉住「复位真的清了闩锁」的一半）
            AssetManager.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            AssetManager.Destroy();
            AssetManager.ImplFactory = null;
        }

        /// <summary>在子线程里执行一次反射调用，并等它结束。</summary>
        private static void InvokeOnWorkerThread()
        {
            var thread = new Thread(() => AssertMainThreadMethod.Invoke(null, new object[] { "Destroy" }));
            thread.Start();
            thread.Join();
        }

        #endregion

        #region 守卫本体

        [Test]
        public void AssertMainThread_OnMainThread_DoesNotLog()
        {
            AssertMainThreadMethod.Invoke(null, new object[] { "InitializeAsync" });

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AssertMainThread_OffMainThread_LogsOncePerLatch()
        {
            // 两次调用只声明一条:第二次必须静默,多打一条会以 UnhandledLogMessageException 暴露
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AssetManager] Destroy 必须在 Unity 主线程调用")));

            var thread = new Thread(() =>
            {
                AssertMainThreadMethod.Invoke(null, new object[] { "Destroy" });
                AssertMainThreadMethod.Invoke(null, new object[] { "Destroy" });
            });
            thread.Start();
            thread.Join();
        }

        #endregion

        #region 接线(入口真的调了守卫吗)

        [Test]
        public void Destroy_OffMainThread_LogsMainThreadError()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AssetManager] Destroy 必须在 Unity 主线程调用")));

            var thread = new Thread(() => AssetManager.Destroy());
            thread.Start();
            thread.Join();
        }

        #endregion

        #region 会话复位让提示重新具备

        [Test]
        public void ResetForNewSession_ReArmsTheLatch()
        {
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AssetManager] Destroy 必须在 Unity 主线程调用")));
            InvokeOnWorkerThread();

            AssetManager.ResetForNewSession();

            // 复位之后同一违规必须**再报一次**——否则提示在长会话里永久失效
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("[AssetManager] Destroy 必须在 Unity 主线程调用")));
            InvokeOnWorkerThread();
        }

        #endregion
    }
}
