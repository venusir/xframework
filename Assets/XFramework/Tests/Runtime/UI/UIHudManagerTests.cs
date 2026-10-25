using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
        private const string UpdateThrowingPath = "ui/hud/update-throw";
        private const string CountingPath = "ui/hud/counting";
        private const string ReentrantPath = "ui/hud/reentrant";
        private const string DoubleThrowPath = "ui/hud/double-throw";

        #endregion

        #region Fields

        private GameObject _root;
        private GameObject _target;
        private FakeAssetManager _assets;

        #endregion

        [SetUp]
        public void SetUp()
        {
            // 归零：不依赖上一个 fixture 的 TearDown（SetUp 中途失败时它不会执行）
            UIManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _assets = new FakeAssetManager();
            _assets.RegisterPrefab<HudStubItem>(StubPath);
            _assets.RegisterPrefab<ThrowingHudItem>(ThrowingPath);
            _assets.RegisterPrefab<AsyncCloseHudItem>(AsyncClosePath);
            _assets.RegisterPrefab<UpdateThrowingHudItem>(UpdateThrowingPath);
            _assets.RegisterPrefab<CountingHudItem>(CountingPath);
            _assets.RegisterPrefab<ReentrantHudItem>(ReentrantPath);
            _assets.RegisterPrefab<ThrowingOpenAndRecycleHudItem>(DoubleThrowPath);
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

        [Test]
        public void ShowHudAsync_OnOpenAndPoolRecycleThrow_PreservesOnOpenException()
        {
            // R4-UIA-002：DiscardHud → RecycleHud 的回池钩子异常此前会替换掉 Attach 的原始 OnOpen 异常。
            LogAssert.Expect(LogType.Error, new Regex("HUD 回池钩子抛异常"));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await UIManager.ShowHudAsync<ThrowingOpenAndRecycleHudItem>(
                    _target.transform, DoubleThrowPath));

            StringAssert.Contains("OnOpen", ex.Message, "原始 OnOpen 异常不得被回池钩子异常替换");
            Assert.AreEqual(1, _assets.DestroyCount, "实例仍应还回资源层");
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

        #region 逐 HUD 异常隔离（UI-B-001）

        /// <summary>
        /// 一个 HUD 的 <c>OnUpdate</c> 抛异常不得中断本轮 HUD 遍历（UI-B-001）。
        /// <para>改前：<c>UIHudManagerImpl.Update</c> 的循环直接调 <c>hud.OnUpdate</c>，异常逃到外层
        /// provider 隔离，同帧其余 HUD 都不会被驱动；症状是「其他 HUD 静默停更」。
        /// 修法：逐 HUD try/catch + 停更标记（回池复位），照面板侧 <c>DriveTier</c> 的先例。</para>
        /// </summary>
        [Test]
        public async Task HudOnUpdateThrows_OtherHudsAreStillDriven()
        {
            var secondTarget = new GameObject("Target2");
            try
            {
                // 先挂计数 HUD、后挂抛异常 HUD：派发是倒序遍历，抛异常者排在前
                var counting = await UIManager.ShowHudAsync<CountingHudItem>(secondTarget.transform, CountingPath);
                await UIManager.ShowHudAsync<UpdateThrowingHudItem>(_target.transform, UpdateThrowingPath);

                LogAssert.Expect(LogType.Error, new Regex("OnUpdate threw exception"));
                Assert.DoesNotThrow(() => UIManager.Update(0.016f, 0f),
                    "单个 HUD 的异常必须被就地隔离，不得逃到 provider 层");

                Assert.AreEqual(1, counting.UpdateCount, "抛异常者之后的 HUD 本帧仍须被驱动");

                // 第二帧：抛过的 HUD 停更（不重复刷日志），另一个继续
                Assert.DoesNotThrow(() => UIManager.Update(0.016f, 0f));
                Assert.AreEqual(2, counting.UpdateCount, "停更只作用于抛异常的那一个");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondTarget);
            }
        }

        /// <summary>
        /// 停更的 HUD 仍要处理目标丢失（R3-UIB-003）：停更只该停「更新」，不该停「回收」——
        /// 否则目标销毁后 HUD 常驻屏幕、映射永远清不掉。
        /// </summary>
        [Test]
        public async Task FaultedHud_TargetDestroyed_IsStillDetached()
        {
            var target = new GameObject("FaultedTarget");
            try
            {
                var hud = await UIManager.ShowHudAsync<UpdateThrowingHudItem>(
                    target.transform, UpdateThrowingPath);

                LogAssert.Expect(LogType.Error, new Regex("OnUpdate threw exception"));
                UIManager.Update(0.016f, 0f); // 首次驱动抛异常 → UpdateFaulted

                UnityEngine.Object.DestroyImmediate(target);
                UIManager.Update(0.016f, 0f); // 目标丢失应回收，而不是被停更分支跳过
                await UniTask.Yield();

                Assert.IsFalse(hud.IsOpen, "停更 HUD 的目标销毁后仍应被回收（R3-UIB-003）");
            }
            finally
            {
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// OnUpdate 里 HideHud 其它 HUD 不得让同一实例同帧被驱动两次（R3-UIB-002）。
        /// 倒序遍历 + 删除下方条目会把当前实例移位到当前下标；没有 lastProcessed 守卫会重复驱动。
        /// </summary>
        [Test]
        public async Task HudOnUpdate_HidesOtherHud_NoDoubleDriveOrCrash()
        {
            var otherTarget = new GameObject("OtherTarget");
            try
            {
                await UIManager.ShowHudAsync<CountingHudItem>(
                    otherTarget.transform, CountingPath);
                var reentrant = await UIManager.ShowHudAsync<ReentrantHudItem>(
                    _target.transform, ReentrantPath);

                ReentrantHudItem.OnUpdateAction = () => UIManager.HideHud(otherTarget.transform);

                Assert.DoesNotThrow(() => UIManager.Update(0.016f, 0f));
                Assert.AreEqual(1, reentrant.UpdateCount,
                    "同一 HUD 不得在同一帧被驱动两次（R3-UIB-002）");
            }
            finally
            {
                ReentrantHudItem.OnUpdateAction = null;
                UnityEngine.Object.DestroyImmediate(otherTarget);
            }
        }

        /// <summary>
        /// 无主相机时 `Camera.main` 不得每帧重查（R3-UIB-011）：降到 1 秒一次，且只告警一次。
        /// </summary>
        [Test]
        public async Task Hud_NoMainCamera_RetriesLookupAtMostOncePerSecond()
        {
            Assert.IsNull(Camera.main, "前提：测试场景没有主相机");

            var hud = await UIManager.ShowHudAsync<HudStubItem>(_target.transform, StubPath);

            LogAssert.Expect(LogType.Warning, new Regex("找不到主相机"));
            UIManager.Update(0f, 0f);
            Assert.AreEqual(1, hud.CameraLookupCount);

            UIManager.Update(0f, 0.5f);
            Assert.AreEqual(1, hud.CameraLookupCount, "1 秒内不应每帧重查 Camera.main");

            UIManager.Update(0f, 1.1f);
            Assert.AreEqual(2, hud.CameraLookupCount, "超过 1 秒后应重试一次");
        }

        /// <summary>
        /// 同 target 完全相同的并发 Attach 必须合并为一次实例化（R4-UIB-002）。
        /// </summary>
        [Test]
        public async Task ShowHudAsync_SameTargetConcurrent_InstantiatesOnce()
        {
            _assets.Gate = new UniTaskCompletionSource();
            try
            {
                var first = UIManager.ShowHudAsync<HudStubItem>(_target.transform, StubPath);
                var second = UIManager.ShowHudAsync<HudStubItem>(_target.transform, StubPath);

                await UniTask.Yield();
                _assets.Gate.TrySetResult();
                var firstHud = await first;
                var secondHud = await second;

                Assert.AreEqual(1, _assets.InstantiateCount, "同 target 同请求应只实例化一次");
                Assert.IsNotNull(firstHud);
                Assert.AreSame(firstHud, secondHud, "并发调用方应拿到同一个 HUD 实例");
                Assert.AreEqual(1, CountActiveHudChildren(), "Layer_HUD 下应只有一个活跃 HUD");
            }
            finally
            {
                _assets.Gate = null;
            }
        }

        /// <summary>
        /// 并发 Attach 后 CloseAllAsync 必须把全部 HUD（含不在映射里的孤儿）清掉（R4-UIB-002）。
        /// </summary>
        [Test]
        public async Task CloseAllAsync_AfterConcurrentAttach_LeavesNoActiveHud()
        {
            _assets.Gate = new UniTaskCompletionSource();
            try
            {
                var first = UIManager.ShowHudAsync<HudStubItem>(_target.transform, StubPath);
                var second = UIManager.ShowHudAsync<HudStubItem>(_target.transform, StubPath);

                await UniTask.Yield();
                _assets.Gate.TrySetResult();
                await first;
                await second;

                await UIManager.CloseAllAsync();

                Assert.AreEqual(0, CountActiveHudChildren(), "CloseAllAsync 后 Layer_HUD 不应残留活跃实例");
            }
            finally
            {
                _assets.Gate = null;
            }
        }

        private int CountActiveHudChildren()
        {
            var container = _root.transform.Find("Layer_HUD");
            if (container == null)
                return 0;

            int count = 0;
            for (int i = 0; i < container.childCount; i++)
            {
                if (container.GetChild(i).gameObject.activeSelf)
                    count++;
            }
            return count;
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
    /// OnOpen 与 OnPoolRecycle 都抛异常的 HUD：验证回池钩子异常不覆盖原始 OnOpen 异常（R4-UIA-002）。
    /// </summary>
    public class ThrowingOpenAndRecycleHudItem : UIHudItem
    {
        protected override UniTask OnOpenImpl(object userData)
        {
            throw new InvalidOperationException("[ThrowingOpenAndRecycleHudItem] 故意在 OnOpen 里抛异常");
        }

        protected internal override void OnPoolRecycle()
        {
            base.OnPoolRecycle();
            throw new InvalidOperationException("[ThrowingOpenAndRecycleHudItem] 故意在 OnPoolRecycle 里抛异常");
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

    /// <summary>每帧更新即抛异常的 HUD，用于验证逐 HUD 异常隔离。</summary>
    public class UpdateThrowingHudItem : UIHudItem
    {
        protected internal override void OnUpdate(float deltaTime, float time)
            => throw new InvalidOperationException("[UpdateThrowingHudItem] 故意在 OnUpdate 里抛异常");
    }

    /// <summary>记录被驱动次数的 HUD。</summary>
    public class CountingHudItem : UIHudItem
    {
        public int UpdateCount;

        protected internal override void OnUpdate(float deltaTime, float time) => UpdateCount++;
    }

    /// <summary>
    /// 在 OnUpdate 里执行一次外部动作的 HUD（用于重入：HideHud 其它 HUD / CloseAllAsync）。
    /// <para>动作只执行一次，避免后续帧重复触发。</para>
    /// </summary>
    public class ReentrantHudItem : UIHudItem
    {
        public static Action OnUpdateAction;

        public int UpdateCount { get; private set; }

        protected internal override void OnUpdate(float deltaTime, float time)
        {
            UpdateCount++;
            base.OnUpdate(deltaTime, time);

            var action = OnUpdateAction;
            OnUpdateAction = null;
            action?.Invoke();
        }
    }

    #endregion
}
