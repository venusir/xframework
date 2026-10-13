using NUnit.Framework;
using UnityEngine.SceneManagement;
using XFramework.XScene;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="SceneLoadOptions"/> 与 <see cref="SceneLoadProgress"/> 的值语义。
    /// <para><b>为什么钉「默认值」</b>：选项是 <c>readonly struct</c>，使用方最自然的写法是
    /// <c>default</c> 或 <c>new SceneLoadOptions()</c>——两者必须都是「单场景 / 不激活 / 不回收」。
    /// 全零在这里是有意义的默认，不是陷阱；这条一旦失守，静默行为会变。</para>
    /// <para><b>无 SetUp/TearDown 是有意的</b>：本 fixture 是纯结构体，不碰任何门面与静态状态。</para>
    /// </summary>
    [TestFixture]
    public class SceneLoadOptionsTests
    {
        #region Tests

        [Test]
        public void Default_IsSingleWithoutExtras()
        {
            SceneLoadOptions options = default;

            Assert.AreEqual(LoadSceneMode.Single, options.Mode);
            Assert.IsFalse(options.ActivateAfterLoad);
            Assert.IsFalse(options.UnloadUnusedAfterLoad);
        }

        [Test]
        public void NewInstance_IsSingleWithoutExtras()
        {
            var options = new SceneLoadOptions();

            Assert.AreEqual(LoadSceneMode.Single, options.Mode, "无参构造与 default 必须一致");
            Assert.IsFalse(options.ActivateAfterLoad);
            Assert.IsFalse(options.UnloadUnusedAfterLoad);
        }

        [Test]
        public void Constructor_RoundTripsAllFields()
        {
            var options = new SceneLoadOptions(LoadSceneMode.Additive, activateAfterLoad: true, unloadUnusedAfterLoad: true);

            Assert.AreEqual(LoadSceneMode.Additive, options.Mode);
            Assert.IsTrue(options.ActivateAfterLoad);
            Assert.IsTrue(options.UnloadUnusedAfterLoad);
        }

        [Test]
        public void Statics_AreTheTwoCommonShapes()
        {
            Assert.AreEqual(LoadSceneMode.Single, SceneLoadOptions.Single.Mode);
            Assert.AreEqual(LoadSceneMode.Additive, SceneLoadOptions.Additive.Mode);
            Assert.IsFalse(SceneLoadOptions.Additive.ActivateAfterLoad, "叠加加载默认不激活——要激活请显式构造");
        }

        /// <summary>
        /// 结构断言而不是分配断言（本仓两把分配尺子在本环境恒返回 0，见 <c>Workflow.md</c> §六）：
        /// 进度载荷在每帧轮询路径上被构造，它必须是值类型，否则每轮一次堆分配。
        /// </summary>
        [Test]
        public void BothStructs_AreValueTypes()
        {
            Assert.IsTrue(typeof(SceneLoadOptions).IsValueType);
            Assert.IsTrue(typeof(SceneLoadProgress).IsValueType,
                "进度载荷每帧构造一次——引用类型会让轮询路径每帧分配");
        }

        [Test]
        public void Progress_CarriesValueAndLocation()
        {
            var progress = new SceneLoadProgress(0.5f, "scenes/main");

            Assert.AreEqual(0.5f, progress.Progress);
            Assert.AreEqual("scenes/main", progress.Location);
        }

        #endregion
    }
}
