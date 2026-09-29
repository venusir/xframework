using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XLocalization;
using XFramework.XMessage;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="LocalizationManager.Subscribe"/> 的生命周期绑定测试。
    /// <para>本 fixture 的存在理由：该归口此前手写了 <c>context.destroyCancellationToken.Register(() =&gt; sub.Dispose())</c>，
    /// 而 <c>MessageManager.TryBindToDestroy</c> 的 XML 逐字警告过这种写法的三处弱点（闭包分配、只认
    /// <c>MonoBehaviour</c> 而漏掉 <see cref="IDestroyCancellationToken"/>、令牌已取消仍去注册）——
    /// <c>UIManager</c> 也已经按那句话改过一遍。</para>
    /// <para><b>改前必红</b>：「实现 <see cref="IDestroyCancellationToken"/> 的普通对象也能用这个归口入口」
    /// 在修复前<b>根本编译不过</b>（形参类型是 <c>MonoBehaviour</c>，CS1503）——不是断言失败，是程序集
    /// 编译失败。这是本模块最硬的一条证据：能力缺失到编译器就能判的程度。
    /// 实测：把这个 fixture 对着「形参还原成 <c>MonoBehaviour</c>」的旧签名编译，三处调用点全部 CS1503。</para>
    /// <para><b>本 fixture 不覆盖 MonoBehaviour 分支</b>：实测 EditMode 下 <c>Object.DestroyImmediate</c>
    /// <b>不会</b>触发 <c>MonoBehaviour.destroyCancellationToken</c>，该分支只能在 PlayMode 覆盖，本轮未新增
    /// PlayMode fixture（它由 Message 侧同一份 <c>TryBindToDestroy</c> 承担，仓内目前无直接用例——已记入
    /// README 的审计记录）。</para>
    /// </summary>
    class LocalizationSubscribeTests
    {
        [SetUp]
        public void SetUp()
        {
            LocalizationManager.Destroy();
            LocalizationManager.Initialize("en", new Dictionary<string, string> { { "seed", "seed" } });
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationManager.Destroy();
        }

        /// <summary>把 <paramref name="lang"/> 塞进缓存，好让 <c>SetLanguage</c> 能同步切过去。</summary>
        private static void CacheLanguage(string lang)
        {
            LocalizationManager.SetLanguageData(lang, new Dictionary<string, string> { { "seed", "seed" } });
        }

        #region 基本订阅

        [Test]
        public void Handler_ReceivesLanguageChangedMessage()
        {
            CacheLanguage("ja");

            int calls = 0;
            var sub = LocalizationManager.Subscribe(_ => calls++);
            try
            {
                LocalizationManager.SetLanguage("ja");

                Assert.AreEqual(1, calls);
            }
            finally
            {
                sub.Dispose();
            }
        }

        [Test]
        public void NullContext_SubscribesWithoutBindingAndWithoutWarning()
        {
            CacheLanguage("ja");

            int calls = 0;
            var sub = LocalizationManager.Subscribe(_ => calls++);
            try
            {
                LocalizationManager.SetLanguage("ja");

                Assert.AreEqual(1, calls, "不传 context 只是不绑定，订还是订上了");
            }
            finally
            {
                sub.Dispose();
            }
        }

        #endregion

        #region 生命周期绑定

        [Test]
        public void DestroyableOwner_UnsubscribesWhenTokenCancelled()
        {
            // 修复前这一行编译不过：形参是 MonoBehaviour，普通 C# 对象传不进去
            CacheLanguage("ja");

            int calls = 0;
            var owner = new FakeDestroyableOwner();
            LocalizationManager.Subscribe(_ => calls++, owner);

            LocalizationManager.SetLanguage("ja");
            Assert.AreEqual(1, calls, "令牌取消之前应正常收到");

            owner.Dispose(); // 令牌取消 == 对象销毁

            LocalizationManager.SetLanguage("en");
            Assert.AreEqual(1, calls, "令牌取消后不应再收到");
        }

        [Test]
        public void AlreadyCancelledOwner_SubscribesThenImmediatelyUnsubscribes()
        {
            // 契约钉子（非改前必红）：订阅者已销毁时不得留下长期订阅。
            // 底层 TryBindToDestroy 走的是「令牌已取消 → 立即 Dispose」这条分支，而不是 Register
            CacheLanguage("ja");

            var owner = new FakeDestroyableOwner();
            owner.Dispose(); // 先销毁再订阅

            int calls = 0;
            LocalizationManager.Subscribe(_ => calls++, owner);

            LocalizationManager.SetLanguage("ja");
            Assert.AreEqual(0, calls, "订阅者已销毁时不得建立长期订阅");
        }

        #endregion

        #region 绑不上要留痕

        [Test]
        public void NonBindableContext_WarnsButStillSubscribes()
        {
            CacheLanguage("ja");

            int calls = 0;
            LogAssert.Expect(LogType.Warning, new Regex(
                @"\[LocalizationManager\] Subscribe<LanguageChangedMessage>: context of type 'Object'"));

            var sub = LocalizationManager.Subscribe(_ => calls++, new object());
            try
            {
                LocalizationManager.SetLanguage("ja");

                Assert.AreEqual(1, calls, "绑不上不等于订不上——静默才是问题，所以要告警 + 照样订阅");
            }
            finally
            {
                sub.Dispose();
            }
        }

        #endregion
    }

    /// <summary>
    /// 实现 <see cref="IDestroyCancellationToken"/> 的**普通 C# 对象**（非 MonoBehaviour），
    /// 用令牌表达「对象销毁」。修复前它无法作为 <c>LocalizationManager.Subscribe</c> 的 context 传入。
    /// </summary>
    internal sealed class FakeDestroyableOwner : IDestroyCancellationToken, IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// 令牌在构造时就取出来缓存。<b>不能写成 <c>=&gt; _cts.Token</c></b>：<c>CancellationTokenSource.Token</c>
        /// 在源被 Dispose 之后会抛 <c>ObjectDisposedException</c>，于是「已销毁的对象」反而读不出自己的令牌
        /// （实测：本 fixture 第一版就撞上了）。<c>MonoBehaviour.destroyCancellationToken</c> 销毁后仍可读，
        /// 实现该接口的普通对象应对齐这一行为。
        /// </summary>
        private readonly CancellationToken _token;

        public FakeDestroyableOwner()
        {
            _token = _cts.Token;
        }

        public CancellationToken DestroyCancellationToken => _token;

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
