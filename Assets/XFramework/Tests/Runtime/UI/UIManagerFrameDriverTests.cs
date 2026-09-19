using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// UI 每帧通路的调度归属测试。
    /// <para>面板 / HUD 的每帧更新原由场景里的 <c>UIRootNode.Update</c> 驱动：那条通路既不在档位
    /// 调度里、也不受 <see cref="UpdateManager.Pause"/> 约束，与其它模块的暂停语义是两套。
    /// 现改为注册进 <see cref="UpdateManager"/> 统一调度。</para>
    /// <para>观察点是 HUD 提供者（<see cref="UIManager.Update"/> 会驱动它）——避免依赖 AssetManager
    /// 打开真实面板。</para>
    /// </summary>
    public class UIManagerFrameDriverTests
    {
        private GameObject _root;
        private CountingHudProvider _hud;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRootNodeTest");
            UIManager.Initialize(_root.transform);

            _hud = new CountingHudProvider();
            UIManager.SetHudProvider(_hud);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        [Test]
        public void Initialize_RegistersSingleFrameDriver()
        {
            Assert.AreEqual(1, UpdateManager.TotalCount, "初始化后应注册一个每帧驱动器");
            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0), "驱动器在每帧档");
        }

        [Test]
        public void Destroy_UnregistersFrameDriver()
        {
            UIManager.Destroy();

            Assert.AreEqual(0, UpdateManager.TotalCount, "销毁后不应留下悬挂回调");
        }

        /// <summary>
        /// 注入路径同样接上每帧驱动。
        /// <para>此前只有 <see cref="UIManager.Initialize"/> 注册驱动器，<see cref="UIManager.SetInstance"/>
        /// 不注册，而驱动类是 private sealed、外部无从补注册——于是注入自定义实例之后，面板、HUD、Tip
        /// 全都没有人来驱动，<c>UIManager.Update</c> 的文档却写着「不需要自行调用」。</para>
        /// </summary>
        [Test]
        public void SetInstance_RegistersFrameDriver_AndDrivesInjectedInstance()
        {
            UIManager.Destroy();   // 回到「什么都没注册」，本用例只走注入这条路径

            var impl = new UIManagerImpl();
            impl.Initialize(_root.transform, null);
            impl.SetHudProvider(_hud);   // 观察点：UIManager.Update 会驱动它
            UIManager.SetInstance(impl);

            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0),
                "注入实例后同样要有每帧驱动器，否则面板/HUD/Tip 全部静止");

            UpdateManager.Tick(time: 1.0f);

            Assert.AreEqual(1, _hud.UpdateCount, "驱动器应把每帧派发转发给注入的实例");
        }

        /// <summary>
        /// 换实例不该多出一个驱动器：驱动器调的是门面的 <c>Update</c>（每次都读当前实例），
        /// 注册两个就会每帧驱动两遍。
        /// </summary>
        [Test]
        public void InitializeThenSetInstance_KeepsSingleFrameDriver()
        {
            var impl = new UIManagerImpl();
            impl.Initialize(_root.transform, null);
            impl.SetHudProvider(_hud);

            UIManager.SetInstance(impl);   // SetUp 已 Initialize 过，驱动器早就在了

            Assert.AreEqual(1, UpdateManager.GetCount(UpdateTier.Tier0),
                "再次注册会让每帧被驱动两遍");

            UpdateManager.Tick(time: 1.0f);

            Assert.AreEqual(1, _hud.UpdateCount, "驱动应转发给替换后的新实例");
        }

        /// <summary>
        /// 注入路径退化为「只有每帧档」：分档需求由 <see cref="UIManagerImpl"/> 读面板档位后上报，
        /// 而那条回调只在 <see cref="UIManager.Initialize"/> 里装配。
        /// </summary>
        [Test]
        public void SetInstance_HasNoTierDrivers()
        {
            UIManager.Destroy();

            var impl = new UIManagerImpl();
            impl.Initialize(_root.transform, null);
            UIManager.SetInstance(impl);

            Assert.AreEqual(0, UIManager.TierDriverCount,
                "注入路径没有分档回调，故不应出现分档驱动器");
        }

        [Test]
        public void Update_IsDrivenByUpdateManager_AndStoppedByPause()
        {
            UpdateManager.Tick(time: 1.0f);
            Assert.AreEqual(1, _hud.UpdateCount,
                "每帧更新应经由 UpdateManager 派发，不再依赖场景里有个 MonoBehaviour 每帧调它");

            UpdateManager.Pause();
            UpdateManager.Tick(time: 2.0f);
            Assert.AreEqual(1, _hud.UpdateCount, "暂停后 UI 每帧更新与其它模块一并停掉");

            UpdateManager.Resume();
            UpdateManager.Tick(time: 3.0f);
            Assert.AreEqual(2, _hud.UpdateCount, "恢复后继续派发");
        }

        /// <summary>
        /// 计数用 HUD 提供者：只关心 <see cref="IUiHudProvider.Update"/> 被调了多少次。
        /// </summary>
        private sealed class CountingHudProvider : IUiHudProvider
        {
            public int UpdateCount { get; private set; }

            public bool HasActive => false;

            public void SetUIRoot(Transform uiRoot) { }

            public UniTask<T> AttachAsync<T>(Transform target, string assetPath, Vector2? offset = null,
                CancellationToken cancellationToken = default) where T : UIHudItem
            {
                return default;
            }

            public void Detach(Transform target) { }

            public void DetachAll() { }

            public void Update(float deltaTime, float time) => UpdateCount++;
        }
    }
}
