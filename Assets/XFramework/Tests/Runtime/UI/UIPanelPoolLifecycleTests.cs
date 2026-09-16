using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.Data;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 回池生命周期测试。
    /// <para>面板与 HUD 都是<strong>回池而非销毁</strong>：<c>AssetManager.DestroyInstance</c> 只是失活并
    /// 脱离父节点。因此任何挂在 <c>OnDestroy</c> 上的释放都不会触发——ViewModel 绑定、语言订阅、
    /// Canvas 排序全都属于这一类。回池钩子 <c>OnPoolRecycle</c> 才是正确的位置。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelPoolLifecycleTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_PoolTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();

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

        #region ViewModel 解绑

        [Test]
        public async Task CloseAsync_UnbindsViewModel()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/first");

            // 刻意绕过 panel.Binding 属性直接取组件——这同时覆盖 OnPoolRecycle 里的
            // 「GetComponent 兜底」分支（_binding 缓存仍为 null）
            var binding = panel.gameObject.AddComponent<UIPanelBinding>();
            var viewModel = new FakeViewModel();
            binding.Bind(viewModel);

            Assert.IsTrue(viewModel.Bound, "绑定后应调用 OnBound");
            Assert.IsTrue(binding.IsBound);

            await UIManager.Panel.CloseAsync<FakePanel>();

            Assert.IsTrue(viewModel.Disposed,
                "面板回池应解绑并释放 ViewModel——修复前挂在 OnDestroy 上，回池不销毁故永不触发");
            Assert.IsFalse(binding.IsBound);
        }

        [Test]
        public async Task CloseThenReopen_BindsFreshViewModel()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/first");
            panel.gameObject.AddComponent<UIPanelBinding>();

            var first = new FakeViewModel();
            panel.Binding.Bind(first);
            await UIManager.Panel.CloseAsync<FakePanel>();

            // 重新打开：拿到的是同一个池化实例
            var reopened = await UIManager.Panel.OpenAsync<FakePanel>("ui/first");
            Assert.AreSame(panel, reopened, "应复用池中的同一实例");

            var second = new FakeViewModel();
            reopened.Binding.Bind(second);

            Assert.IsFalse(first.Bound, "旧 ViewModel 应已解绑");
            Assert.IsTrue(second.Bound, "新 ViewModel 应正常绑定");
        }

        #endregion

        #region 订阅归口

        [Test]
        public async Task Track_DisposedOnPoolRecycle()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/first");

            var subscription = new CountingDisposable();
            var returned = panel.Track(subscription);

            Assert.AreSame(subscription, returned, "Track 应原样返回句柄，便于链式使用");

            await UIManager.Panel.CloseAsync<FakePanel>();

            Assert.IsTrue(subscription.Disposed, "登记的订阅应在回池时释放");
        }

        [Test]
        public async Task Track_Null_IsIgnored()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/first");

            Assert.IsNull(panel.Track(null), "null 应原样返回且不登记");

            await UIManager.Panel.CloseAsync<FakePanel>();
        }

        [Test]
        public async Task Track_SurvivesCloseBlockedByController()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/first");

            var subscription = new CountingDisposable();
            panel.Track(subscription);

            UIManager.Panel.SetController(new BlockingController { BlockClose = true });
            await UIManager.Panel.CloseAsync<FakePanel>();

            Assert.IsTrue(UIManager.Panel.IsOpen<FakePanel>(), "关闭被拦下，面板仍开着");
            Assert.IsFalse(subscription.Disposed,
                "关闭被拦下时不该释放订阅——解绑挂在回池点而非关闭点，正是为此");
        }

        #endregion

        #region HUD 回池

        /// <remarks>
        /// 未覆盖：<c>UIHudManagerImpl.DetachInternal</c> 中「调用 <c>OnPoolRecycle</c>」这一步接线。
        /// 走通它需要真实的 HUD 管理器，而它硬编码了 <see cref="XAsset.AssetManager"/>。
        /// 此处覆盖两半：回池钩子本身在 HUD 实例上的行为，以及触发回收的「目标丢失」信号。
        /// </remarks>
        [Test]
        public void HudPoolRecycle_ResetsCanvasSorting()
        {
            var hud = MakeHud(_root.transform);

            try
            {
                hud.Canvas.overrideSorting = true;
                hud.Canvas.sortingOrder = 990_000;

                hud.DrivePoolRecycle();

                Assert.AreEqual(0, hud.Canvas.sortingOrder,
                    "HUD 回池应复位 Canvas 排序——漏调 OnPoolRecycle 的话排序值会一直留在实例上");
                Assert.IsFalse(hud.Canvas.overrideSorting);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hud.gameObject);
            }
        }

        [Test]
        public void HudWithLostTarget_RaisesOnTargetLost()
        {
            var hud = MakeHud(_root.transform);

            try
            {
                UIHudItem reported = null;
                hud.OnTargetLost += h => reported = h;

                hud.FollowTarget = null;
                hud.DriveUpdate();

                Assert.AreSame(hud, reported, "目标丢失应触发回收信号，HUD 管理器据此 Detach");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hud.gameObject);
            }
        }

        /// <summary>造一个挂在 <paramref name="parent"/> 下的 HUD 实例。</summary>
        private static StubHud MakeHud(Transform parent)
        {
            var go = new GameObject("Hud", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var hud = go.AddComponent<StubHud>();
            hud.FollowTarget = new GameObject("HudTarget").transform;
            return hud;
        }

        #endregion

        #region Test Doubles

        /// <summary>记录生命周期调用的假 ViewModel。</summary>
        private sealed class FakeViewModel : IViewModel
        {
            public bool Bound { get; private set; }
            public bool Unbound { get; private set; }
            public bool Disposed { get; private set; }

            public void OnBound() => Bound = true;

            public void OnUnbound()
            {
                Unbound = true;
                Bound = false;
            }

            public void Dispose() => Disposed = true;
        }

        /// <summary>记录是否被释放的句柄。</summary>
        private sealed class CountingDisposable : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        /// <summary>
        /// 可供测试直接驱动的 HUD 实例。
        /// <para><c>OnUpdate</c> / <c>OnPoolRecycle</c> 是 <c>protected internal</c>，跨程序集只能经
        /// <c>protected</c> 语义访问，故由子类暴露驱动入口。</para>
        /// </summary>
        public class StubHud : UIHudItem
        {
            public void DriveUpdate(float deltaTime = 0.016f, float time = 0f) => OnUpdate(deltaTime, time);

            public void DrivePoolRecycle() => OnPoolRecycle();
        }

        #endregion
    }
}
