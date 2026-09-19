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
            UIManager.Hud.SetProvider(_hud);
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
