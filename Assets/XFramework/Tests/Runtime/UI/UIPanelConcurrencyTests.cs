using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.Data;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 并发的打开/失败回滚语义测试。
    /// <para>三处回归：同类型并发打开会各自实例化并互相覆盖；<c>PushAsync</c> 打开失败后
    /// 栈顶永久停在失焦态；打开中途失败会留下已实例化但未注册的孤儿面板。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelConcurrencyTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_ConcurrencyTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();
            _factory.RegisterPanel<ThrowingPanel>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Gate = null;
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #region 并发去重

        [Test]
        public async Task ConcurrentOpen_SameType_SharesInstanceAndPublishesOnce()
        {
            int openedCount = 0;
            var subscription = UIManager.Subscribe((PanelOpenedMessage _) => openedCount++);

            try
            {
                // 闸住工厂，制造「在途打开」窗口
                _factory.Gate = new UniTaskCompletionSource<object>();

                var first = UIManager.OpenAsync<FakePanel>("ui/first").Preserve();
                await UniTask.Yield();
                var second = UIManager.OpenAsync<FakePanel>("ui/first").Preserve();
                await UniTask.Yield();

                Assert.AreEqual(1, _factory.CreateCount, "同类型并发打开只应实例化一次");

                _factory.Gate.TrySetResult(null);

                var p1 = await first;
                var p2 = await second;

                Assert.IsNotNull(p1, "首个调用者应拿到面板");
                Assert.AreSame(p1, p2, "后来者应共享同一次打开的结果，而不是拿到池中失活引用");
                Assert.AreEqual(1, openedCount, "PanelOpenedMessage 只应发一次");
                Assert.IsFalse(UIManager.CanGoBack, "显示栈中不应出现重复条目");
            }
            finally
            {
                _factory.Gate = null;
                subscription.Dispose();
            }
        }

        [Test]
        public async Task ConcurrentOpen_DifferentTypes_BothOpen()
        {
            _factory.Gate = new UniTaskCompletionSource<object>();

            var a = UIManager.OpenAsync<FakePanel>("ui/a").Preserve();
            await UniTask.Yield();
            var b = UIManager.OpenAsync<FakePanelB>("ui/b").Preserve();
            await UniTask.Yield();

            _factory.Gate.TrySetResult(null);

            Assert.IsNotNull(await a);
            Assert.IsNotNull(await b);
            Assert.AreEqual(2, _factory.CreateCount, "去重按类型隔离，不同类型各开各的");
            Assert.IsTrue(UIManager.IsOpen<FakePanel>());
            Assert.IsTrue(UIManager.IsOpen<FakePanelB>());
        }

        #endregion

        #region 失败回滚

        [Test]
        public async Task OpenAsync_BlockedByController_LeavesNothingRegistered()
        {
            UIManager.SetController(new BlockingController { BlockOpen = true });

            var panel = await UIManager.OpenAsync<FakePanel>("ui/blocked");

            Assert.IsNull(panel, "被拦截时应返回 null");
            Assert.IsFalse(UIManager.IsOpen<FakePanel>());
            Assert.AreEqual(0, _factory.CreateCount, "拦截发生在实例化之前，不应产生任何实例");
        }

        [Test]
        public async Task OpenAsync_OnOpenThrows_RollsBackInstance()
        {
            bool threw = false;
            try
            {
                await UIManager.OpenAsync<ThrowingPanel>("ui/boom");
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "OnOpen 的异常应向上传播，而不是被吞掉");
            Assert.IsFalse(UIManager.IsOpen<ThrowingPanel>(), "失败的打开不应留下活动条目");
            Assert.IsFalse(UIManager.CanGoBack, "失败的打开不应留在显示栈里");
            Assert.AreEqual(1, _factory.ReleaseCount, "已实例化的面板应经工厂回收，不留孤儿");
        }

        [Test]
        public async Task PushAsync_BlockedByController_RestoresFocusOnPreviousTop()
        {
            var first = await UIManager.OpenAsync<FakePanel>("ui/first");

            // 注意：首次打开走的是 OnOpenImpl 里直接设状态，并不经过 OnFocus()，
            // 故这里断言状态而非日志（首次打开与 Push/Pop 恢复焦点是两条路径，
            // 该不一致留给 OnFocus/OnBlur 正交化那一提交统一）
            Assert.IsFalse(first.IsPaused, "刚打开的面板应处于可交互状态");
            Assert.IsTrue(first.Raycaster.enabled);

            UIManager.SetController(new BlockingController { BlockOpen = true });

            var pushed = await UIManager.PushAsync<FakePanelB>("ui/second");

            Assert.IsNull(pushed, "被拦截时应返回 null");
            Assert.IsFalse(UIManager.IsOpen<FakePanelB>());
            Assert.IsTrue(first.Logged("OnBlur"), "Push 会先模糊栈顶");
            Assert.IsTrue(first.Logged("OnFocus"),
                "打开失败后应把焦点还给栈顶——修复前它会永久停在失焦态");
            Assert.IsFalse(first.IsPaused, "不应停留在暂停态");
            Assert.IsTrue(first.Raycaster.enabled, "交互应恢复可用");
        }

        #endregion

        #region 已打开目标

        [Test]
        public async Task PushAsync_TargetAlreadyOpen_DoesNotLeaveItBlurred()
        {
            var first = await UIManager.OpenAsync<FakePanel>("ui/first");
            Assert.IsFalse(first.IsPaused, "刚打开的面板不应处于暂停态");

            var pushed = await UIManager.PushAsync<FakePanel>("ui/first");

            Assert.AreSame(first, pushed, "目标是已打开的面板时应复用它");
            Assert.IsFalse(first.IsPaused,
                "目标是已打开的面板时不应先模糊再聚焦——栈顶可能就是它自己，那样会把它留在失焦态");
            Assert.IsTrue(first.Raycaster.enabled, "交互应保持可用");
            Assert.AreEqual(1, _factory.CreateCount, "不应重复实例化");
        }

        #endregion
    }
}
