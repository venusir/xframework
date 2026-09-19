using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// HUD 通路的异常与回收时序测试。
    /// <para><b>为什么此前没有这类用例</b>：<c>UIHudManagerImpl</c> 硬编码了
    /// <see cref="AssetManager"/>，Runtime 测试里造不出 HUD——<c>UIPanelPoolLifecycleTests</c> 的
    /// 「HUD 回池」区为此只覆盖了两半。现在用 <see cref="FakeAssetManager"/> 注入假的资源层，
    /// 整条通路都能跑起来了。</para>
    /// </summary>
    [TestFixture]
    public class UIHudManagerTests
    {
        #region Constants

        private const string StubPath = "ui/hud/ok";
        private const string ThrowingPath = "ui/hud/throw";
        private const string AsyncClosePath = "ui/hud/async";

        #endregion

        #region Fields

        private GameObject _root;
        private GameObject _target;
        private FakeAssetManager _assets;

        #endregion

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _assets = new FakeAssetManager();
            _assets.RegisterPrefab<HudStubItem>(StubPath);
            _assets.RegisterPrefab<ThrowingHudItem>(ThrowingPath);
            _assets.RegisterPrefab<AsyncCloseHudItem>(AsyncClosePath);
            AssetManager.SetInstance(_assets);

            _root = new GameObject("UIRoot_HudTest", typeof(RectTransform));
            _target = new GameObject("Target");

            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _assets.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            if (_target != null)
                UnityEngine.Object.DestroyImmediate(_target);

            // AssetManager 也必须复位：本 fixture 注入过假实例，留着会污染后续 fixture
            AssetManager.Destroy();

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #region 打开失败

        /// <summary>
        /// HUD 打开失败时，实例必须还回资源层。
        /// <para>失败点发生在注册映射之前，于是 <c>DetachAll</c> 也找不到它——不自己收干净的话，
        /// 它就是一个常驻 Layer_HUD、还持着资源引用、谁也碰不到的孤儿。面板侧有回滚路径兜住，
        /// HUD 侧此前什么都没有。</para>
        /// </summary>
        [Test]
        public void ShowHudAsync_OnOpenThrows_RecyclesInstance()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await UIManager.ShowHudAsync<ThrowingHudItem>(_target.transform, ThrowingPath));

            Assert.AreEqual(1, _assets.DestroyCount,
                "打开失败也必须把实例还回资源层——否则它既不在映射里也不在池里");
        }

        #endregion

        #region 清空之后仍可用

        /// <summary>
        /// 全部关闭之后，HUD 仍要能附加。
        /// <para><c>CloseAllAsync</c> 会用 <c>DetachAll</c> 收掉在播 HUD，而 <c>DetachAll</c> 同时
        /// 服务于「管理器要退役了」（Dispose / 换根）。若把后者实现成粘性标志，前者就会把管理器
        /// 永久判为已退役——此后每次 Attach 都实例化完立刻回收。（Tip 侧踩过同一个坑，见
        /// <c>UITipProviderTests</c>。）</para>
        /// </summary>
        [Test]
        public async Task ShowHudAsync_AfterCloseAll_StillWorks()
        {
            await UIManager.ShowHudAsync<HudStubItem>(_target.transform, StubPath);

            await UIManager.CloseAllAsync();
            Assert.AreEqual(1, _assets.DestroyCount, "全部关闭应回收 HUD");

            var hud = await UIManager.ShowHudAsync<HudStubItem>(_target.transform, StubPath);

            Assert.IsNotNull(hud, "CloseAllAsync 之后新附加的 HUD 不该被丢掉");
            Assert.AreEqual(1, _assets.DestroyCount,
                "新 HUD 不该被立刻回收——那等于 HUD 从此再也用不了");
        }

        #endregion

        #region 回收时序

        /// <summary>
        /// 回收必须发生在关闭<b>完成之后</b>。
        /// <para><c>Detach</c> 是同步 API，此前先 <c>DoCloseAsync(...).Forget()</c> 再立刻回收 GameObject：
        /// 关闭的续体（含 <c>DoCloseAsync</c> 收尾里的失活与状态落定）会跑在实例已回池、甚至已被另一个
        /// 目标取走复用之后——把新持有者的 HUD 关掉并失活。</para>
        /// </summary>
        [Test]
        public async Task HideHud_RecyclesOnlyAfterCloseCompletes()
        {
            var hud = await UIManager.ShowHudAsync<AsyncCloseHudItem>(_target.transform, AsyncClosePath);

            UIManager.HideHud(_target.transform);   // 同步 Detach，关闭跨帧

            await UniTask.Yield();
            await UniTask.Yield();

            Assert.IsTrue(hud.CloseCompleted, "前置：关闭应当已经跑完");
            Assert.IsTrue(hud.RecycledAfterClose,
                "回收抢在关闭前面了——那会让关闭的收尾落在已回池的实例上");
            Assert.AreEqual(1, _assets.DestroyCount);
        }

        #endregion

        #region 映射不留死条目

        /// <summary>
        /// 目标丢失触发的回收（此时 <c>FollowTarget</c> 已是 null）不能留下以旧目标为键的死条目，
        /// 否则下一次 <c>DetachAll</c> 会对着一个已回池的实例再回收一遍。
        /// </summary>
        [Test]
        public async Task TargetLost_ThenDetachAll_DoesNotRecycleTwice()
        {
            var hud = await UIManager.ShowHudAsync<HudStubItem>(_target.transform, StubPath);

            // 目标丢失：FollowTarget 被清空，下一帧 OnUpdate 检出并触发回收
            hud.FollowTarget = null;
            UIManager.Update(0.016f, 0f);
            await UniTask.Yield();

            Assert.AreEqual(1, _assets.DestroyCount, "目标丢失应回收一次");

            // 再清一次：映射里若还留着旧条目，这里就会二次回收
            UIManager.SetHudProvider(null);
            await UniTask.Yield();

            Assert.AreEqual(1, _assets.DestroyCount,
                "映射里残留的死条目会让 DetachAll 二次回收同一个实例");
        }

        #endregion
    }

    #region Test Doubles

    /// <summary>什么都不做的 HUD，用于走通正常路径。</summary>
    public class HudStubItem : UIHudItem
    {
    }

    /// <summary>打开即抛异常的 HUD，用于验证附加失败时实例被回池、不留孤儿。</summary>
    public class ThrowingHudItem : UIHudItem
    {
        protected override UniTask OnOpenImpl(object userData)
        {
            throw new InvalidOperationException("[ThrowingHudItem] 故意在 OnOpen 里抛异常");
        }
    }

    /// <summary>
    /// 关闭要跨一帧的 HUD，用于验证「回收一定发生在关闭之后」。
    /// <para>观察点放在 <see cref="UIViewBase.OnPoolRecycle"/> 上：它由回收路径调用，此刻关闭若尚未
    /// 跑完，就说明回收抢跑了。</para>
    /// </summary>
    public class AsyncCloseHudItem : UIHudItem
    {
        /// <summary>关闭是否已经跑完。</summary>
        public bool CloseCompleted { get; private set; }

        /// <summary>回池那一刻关闭是否已完成。</summary>
        public bool RecycledAfterClose { get; private set; }

        protected override async UniTask OnCloseImpl(bool immediate)
        {
            await UniTask.Yield();   // 制造「关闭尚未完成」的窗口
            CloseCompleted = true;
        }

        protected internal override void OnPoolRecycle()
        {
            RecycledAfterClose = CloseCompleted;
            base.OnPoolRecycle();
        }
    }

    #endregion
}
