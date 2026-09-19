using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XMessage;
using XFramework.XUI.Data;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// <see cref="UIManager.Subscribe{T}"/> 的生命周期绑定测试。
    /// <para><b>为什么要有这一条</b>：这三个重载此前各自手写了一遍「订阅 + 绑定销毁令牌」，
    /// 而底层 <c>MessageManager.TryBindToDestroy</c> 早就把同一件事做全了。手写版比它弱三处：
    /// 每次订阅分配一个闭包；<c>context</c> 限定 <c>MonoBehaviour</c>，于是实现了框架自己的
    /// <see cref="IDestroyCancellationToken"/> 约定的非 MonoBehaviour 对象（ViewModel / Model）
    /// 根本用不了这个归口入口；令牌已取消时仍去注册。现改为复用同一份实现。</para>
    /// <para>第一条用例只防回归（旧写法也能过）；后两条才是这次修复的判别点。</para>
    /// </summary>
    [TestFixture]
    public class UISubscribeBindingTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_SubscribeTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        /// <summary>
        /// MonoBehaviour 订阅者：它被销毁后不应再收到回调。
        /// <para>这条在改动前后都成立（旧写法也走 <c>destroyCancellationToken</c>），属回归护栏——
        /// 真正区分新旧的是下面两条。</para>
        /// </summary>
        [Test]
        public async Task Subscribe_WithMonoBehaviour_StopsAfterDestroy()
        {
            int count = 0;
            var listener = new GameObject("Listener").AddComponent<SubscribeListener>();
            UIManager.Subscribe((PanelOpenedMessage _) => count++, listener);

            await UIManager.OpenAsync<FakePanel>("ui/a");
            Assert.AreEqual(1, count, "订阅有效时应收到面板打开消息");

            UnityEngine.Object.DestroyImmediate(listener.gameObject);

            await UIManager.CloseAsync<FakePanel>();
            await UIManager.OpenAsync<FakePanel>("ui/a");

            Assert.AreEqual(1, count, "订阅者销毁后不应再收到回调");
        }

        /// <summary>
        /// 非 MonoBehaviour 订阅者：实现 <see cref="IDestroyCancellationToken"/> 即可自动退订。
        /// <para>这正是改动前无法表达的用法——参数类型卡在 <c>MonoBehaviour</c>，ViewModel / Model
        /// 这类普通 C# 对象只能自己持有句柄。</para>
        /// </summary>
        [Test]
        public async Task Subscribe_WithDestroyTokenObject_StopsAfterCancel()
        {
            int count = 0;
            var listener = new TokenListener();
            UIManager.Subscribe((PanelOpenedMessage _) => count++, listener);

            await UIManager.OpenAsync<FakePanel>("ui/a");
            Assert.AreEqual(1, count, "非 MonoBehaviour 订阅者同样应当收到消息");

            listener.Dispose();   // 令牌取消 == 对象销毁

            await UIManager.CloseAsync<FakePanel>();
            await UIManager.OpenAsync<FakePanel>("ui/a");

            Assert.AreEqual(1, count, "令牌取消后不应再收到回调");
        }

        /// <summary>
        /// 既不是 MonoBehaviour 也不实现 <see cref="IDestroyCancellationToken"/>：订得上，但没人会
        /// 自动退订。此时必须留痕——静默的话，故障表现只是「对象没了回调还在跑」，很难追回这里。
        /// </summary>
        [Test]
        public void Subscribe_WithUnbindableContext_WarnsButStillSubscribes()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex("neither a MonoBehaviour nor an IDestroyCancellationToken"));

            var subscription = UIManager.Subscribe((PanelOpenedMessage _) => { }, "not a lifecycle object");

            Assert.IsNotNull(subscription, "绑定不上不代表订阅失败——句柄仍要交给调用方自行管理");
            subscription.Dispose();
        }
    }

    /// <summary>用作订阅上下文的 MonoBehaviour 替身：只需要一个能被销毁的身份。</summary>
    internal sealed class SubscribeListener : MonoBehaviour
    {
    }

    /// <summary>
    /// 非 MonoBehaviour 的生命周期对象替身：照 <see cref="IDestroyCancellationToken"/> 的文档示例实现。
    /// </summary>
    internal sealed class TokenListener : IDestroyCancellationToken, IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public CancellationToken DestroyCancellationToken => _cts.Token;

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
