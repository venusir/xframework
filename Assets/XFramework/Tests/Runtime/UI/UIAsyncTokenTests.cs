using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// Tip / HUD 两条子系统通路的取消令牌传递测试。
    /// <para>这两条路都要经 <see cref="XAsset.AssetManager"/> 才能真正跑起来，故此处用假 provider
    /// 只验证「门面把令牌原样转交给了 provider」——这是本提交新增的公开契约。</para>
    /// </summary>
    [TestFixture]
    public class UIAsyncTokenTests
    {
        private GameObject _root;
        private RecordingTipProvider _tip;
        private RecordingHudProvider _hud;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_TokenTest", typeof(RectTransform));
            UIManager.Initialize(_root.transform);

            _tip = new RecordingTipProvider();
            _hud = new RecordingHudProvider();
            UIManager.SetTipProvider(_tip);
            UIManager.SetHudProvider(_hud);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        [Test]
        public async Task ShowTipAsync_ForwardsTextConfigAndToken()
        {
            using var cts = new CancellationTokenSource();
            var config = new TipConfig { Duration = 1.5f };

            await UIManager.ShowTipAsync("+10", config, cts.Token);

            Assert.AreEqual(1, _tip.CallCount);
            Assert.AreEqual("+10", _tip.LastText);
            Assert.AreEqual(1.5f, _tip.LastConfig.Duration);
            Assert.AreEqual(cts.Token, _tip.LastToken, "令牌应原样转交，供提前终止播放");
        }

        [Test]
        public async Task ShowHud_ForwardsTargetPathOffsetAndToken()
        {
            var target = new GameObject("Target").transform;

            try
            {
                using var cts = new CancellationTokenSource();

                await UIManager.ShowHudAsync<StubHudItem>(target, "ui/hud/hp", new Vector2(0f, 80f), cts.Token);

                Assert.AreEqual(1, _hud.CallCount);
                Assert.AreSame(target, _hud.LastTarget);
                Assert.AreEqual("ui/hud/hp", _hud.LastAssetPath);
                Assert.AreEqual(new Vector2(0f, 80f), _hud.LastOffset.Value);
                Assert.AreEqual(cts.Token, _hud.LastToken);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target.gameObject);
            }
        }

        /// <summary>
        /// 全部关闭应一并回收 HUD 与在播 Tip。
        /// <para>它们不是面板，但与面板共享同一个 UIRoot 与生命周期。此前实现里没有这一步，而
        /// README 与实现内注释都写着「已经做了」——注释说交给门面处理，门面只转发一行。</para>
        /// </summary>
        [Test]
        public async Task CloseAllAsync_DetachesHudAndTips()
        {
            await UIManager.CloseAllAsync();

            Assert.AreEqual(1, _hud.DetachAllCount, "全部关闭应回收世界空间 HUD");
            Assert.AreEqual(1, _tip.DetachAllCount, "全部关闭应回收在播 Tip");
        }

        /// <summary>
        /// 但遮罩不在其列：它是引用计数句柄，在这里强制清掉会让别的系统手里的句柄凭空失效。
        /// <para>钉住这条是必要的——「全部关闭」很容易被顺手改成「什么都清」，而那会破坏句柄契约。</para>
        /// </summary>
        [Test]
        public async Task CloseAllAsync_LeavesMaskAlone()
        {
            var mask = UIManager.ShowMask();
            Assert.IsTrue(UIManager.IsMaskShowing);

            await UIManager.CloseAllAsync();

            Assert.IsTrue(UIManager.IsMaskShowing,
                "要收遮罩得显式 HideMask()，而不是让「全部关闭」替持有者释放");

            mask.Dispose();
        }

        #region Test Doubles

        /// <summary>记录调用参数的 Tip provider，不触碰资源系统。</summary>
        private sealed class RecordingTipProvider : IUITipProvider
        {
            public int CallCount { get; private set; }
            public int DetachAllCount { get; private set; }
            public string LastText { get; private set; }
            public TipConfig LastConfig { get; private set; }
            public CancellationToken LastToken { get; private set; }

            public void SetUIRoot(Transform uiRoot) { }

            public UniTask ShowTipAsync(string text, TipConfig config = default,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                LastText = text;
                LastConfig = config;
                LastToken = cancellationToken;
                return UniTask.CompletedTask;
            }

            public void Update(float deltaTime, float time) { }

            public void DetachAll() => DetachAllCount++;
        }

        /// <summary>记录调用参数的 HUD provider，不触碰资源系统。</summary>
        private sealed class RecordingHudProvider : IUiHudProvider
        {
            public int CallCount { get; private set; }
            public int DetachAllCount { get; private set; }
            public Transform LastTarget { get; private set; }
            public string LastAssetPath { get; private set; }
            public Vector2? LastOffset { get; private set; }
            public CancellationToken LastToken { get; private set; }

            public bool HasActive => false;

            public void SetUIRoot(Transform uiRoot) { }

            public UniTask<T> AttachAsync<T>(Transform target, string assetPath, Vector2? offset = null,
                CancellationToken cancellationToken = default) where T : UIHudItem
            {
                CallCount++;
                LastTarget = target;
                LastAssetPath = assetPath;
                LastOffset = offset;
                LastToken = cancellationToken;
                return UniTask.FromResult<T>(null);
            }

            public void Detach(Transform target) { }

            public void DetachAll() => DetachAllCount++;

            public void Update(float deltaTime, float time) { }
        }

        /// <summary>仅用于满足泛型约束的 HUD 类型。</summary>
        public class StubHudItem : UIHudItem
        {
        }

        #endregion
    }
}
