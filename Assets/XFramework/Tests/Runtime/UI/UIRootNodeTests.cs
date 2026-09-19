using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// <see cref="UIRootNode"/> 的生命周期归属测试。
    /// <para>它是场景里唯一会自动碰全局管理器的组件，而判据此前是「全局是否已初始化」而不是
    /// 「是不是我」——于是叠加场景下卸载<b>任意</b>一个节点，都会把另一个场景仍在用的管理器
    /// 一起拆掉，此后所有门面调用抛「尚未初始化」。</para>
    /// <para>注意本 fixture 不注入面板工厂，也不开面板：它只关心节点的接管与拆台。</para>
    /// </summary>
    [TestFixture]
    public class UIRootNodeTests
    {
        private GameObject _owner;
        private GameObject _intruder;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            // AddComponent 会立刻跑 Awake；RequireComponent(Canvas) 自动补齐 Canvas，
            // 于是这个节点直接接管全局管理器
            _owner = new GameObject("UIRoot_Owner");
            _owner.AddComponent<UIRootNode>();
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();

            if (_owner != null)
                UnityEngine.Object.DestroyImmediate(_owner);

            if (_intruder != null)
                UnityEngine.Object.DestroyImmediate(_intruder);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        [Test]
        public void Awake_TakesOverAsRoot()
        {
            Assert.IsTrue(UIManager.IsInitialized, "场景里的根节点应当自动完成初始化");
            Assert.AreSame(_owner.transform, UIManager.UIRoot);
        }

        /// <summary>
        /// 卸载别的节点不该拆掉正在使用的管理器。
        /// <para>第二个节点会被忽略（单根是既有设计），但它的 <c>OnDestroy</c> 此前同样会调
        /// <c>UIManager.Destroy()</c>——卸载一个从没生效过的节点，却把在用的管理器拆了。</para>
        /// </summary>
        [Test]
        public void DestroyingNonOwnerNode_KeepsManagerAlive()
        {
            _intruder = new GameObject("UIRoot_Intruder");
            _intruder.AddComponent<UIRootNode>();

            Assert.AreSame(_owner.transform, UIManager.UIRoot,
                "前置：第二个节点被忽略，根仍是第一个");

            UnityEngine.Object.DestroyImmediate(_intruder);
            _intruder = null;

            Assert.IsTrue(UIManager.IsInitialized, "卸载别人的节点不该把管理器拆掉");
            Assert.AreSame(_owner.transform, UIManager.UIRoot, "根不该变");
        }

        /// <summary>
        /// 反过来：根节点自己被卸载时，管理器要随之拆掉（层容器与面板都挂在它的子节点下）。
        /// </summary>
        [Test]
        public void DestroyingOwnerNode_TearsManagerDown()
        {
            UnityEngine.Object.DestroyImmediate(_owner);
            _owner = null;

            Assert.IsFalse(UIManager.IsInitialized, "根没了，管理器也该收摊");
        }
    }
}
