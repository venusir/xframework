using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using XFramework.XInput;
using XFramework.XInput.Default;

namespace XFramework.XInput.Tests
{
    /// <summary>
    /// 「自己加载资产」这条入口：<c>InputManager.Initialize(InputSystemOptions)</c>。
    /// <para><b>它解决的问题</b>：无参 <c>Initialize()</c> 只能从 <c>Resources/</c> 按固定名字加载——
    /// 用 YooAsset 之类资源系统的项目无路可走。本重载把加载那一步交还给使用方，接线与无参路径相同。</para>
    /// <para><b>夹具纪律</b>：<c>InputManager</c> 是全局静态门面，PlayMode 下所有用例共享一个 player——
    /// SetUp/TearDown 都 <c>Destroy()</c>，否则「未初始化时的宽容语义」等用例会被本 fixture 污染。</para>
    /// </summary>
    [TestFixture]
    public class InputSystemOptionsTests : InputTestFixture
    {
        #region Fixture

        [SetUp]
        public void SetUp()
        {
            InputManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            InputManager.Destroy();
        }

        #endregion

        /// <summary>构造单 map 单 action 的程序化资产；map 名必须是 Player——默认提供者初始化时会切到它。</summary>
        private static InputActionAsset CreatePlayerAsset(string actionName)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Player");
            map.AddAction(actionName, InputActionType.Button, "<Keyboard>/space");
            asset.AddActionMap(map);
            return asset;
        }

        [Test]
        public void Initialize_NullOptions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => InputManager.Initialize((InputSystemOptions)null));
        }

        [Test]
        public void Initialize_NullAsset_Throws()
        {
            Assert.Throws<ArgumentException>(() => InputManager.Initialize(new InputSystemOptions()));
        }

        /// <summary>核心契约：交给它的资产真的被用上了——资产里的 action 立刻可查询。</summary>
        [Test]
        public void Initialize_WithLoadedAsset_UsesThatAsset()
        {
            var asset = CreatePlayerAsset("Jump");

            InputManager.Initialize(new InputSystemOptions { Asset = asset });

            Assert.IsTrue(InputManager.IsInitialized);
            Assert.IsTrue(InputManager.HasAction("Jump"),
                "自己加载的资产必须成为默认提供者的工作资产——这正是本入口存在的意义");
        }
    }
}
