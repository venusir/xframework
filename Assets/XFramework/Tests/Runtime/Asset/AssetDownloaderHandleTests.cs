using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAsset;

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// <see cref="AssetDownloaderHandle"/> 的逐订阅者隔离（R3-AST-006）。
    /// <para>下载器的三个事件由 YooAsset 的 <c>OperationSystem</c> 在更新循环里同步调用，任一订阅者
    /// 抛异常都会打断整轮更新。多播直接 <c>Invoke</c> 还会在抛出者处中断、后续订阅者收不到，因此
    /// 实现按调用列表逐个 try/catch；这里直接测那段隔离逻辑（真 <c>DownloaderOperation</c> 需要
    /// 真实 YooAsset 包，构造不出来）。</para>
    /// </summary>
    [TestFixture]
    public class AssetDownloaderHandleTests
    {
        [Test]
        public void InvokeIsolated_OneSubscriberThrows_OthersStillInvoked()
        {
            int second = 0;
            Action<float> handlers = _ => throw new InvalidOperationException("boom");
            handlers += _ => second++;

            LogAssert.Expect(LogType.Error, new Regex("下载器事件 ProgressChanged 的订阅者抛出异常"));

            AssetDownloaderHandle.InvokeIsolated(handlers, 0.5f, "ProgressChanged");

            Assert.AreEqual(1, second, "一个订阅者抛异常不得阻断后续订阅者");
        }

        [Test]
        public void InvokeIsolated_TwoArgOverload_OneSubscriberThrows_OthersStillInvoked()
        {
            int second = 0;
            Action<string, string> handlers = (_, _) => throw new InvalidOperationException("boom");
            handlers += (_, _) => second++;

            LogAssert.Expect(LogType.Error, new Regex("下载器事件 DownloadError 的订阅者抛出异常"));

            AssetDownloaderHandle.InvokeIsolated(handlers, "a", "b", "DownloadError");

            Assert.AreEqual(1, second, "一个订阅者抛异常不得阻断后续订阅者");
        }
    }
}
