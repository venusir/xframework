using System;
using System.Reflection;
using NUnit.Framework;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 门面分组形态的测试。
    /// <para>用反射锁住「哪些成员在哪一层」——纯改名重构最容易在后续新增成员时被稀释：
    /// 顺手往扁平层加一个方法不会有任何提示，直到分组的可读性重新退化回一团。</para>
    /// </summary>
    [TestFixture]
    public class UIFacadeGroupingTests
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        [Test]
        public void Facade_ExposesSubsystemGroups()
        {
            string[] groups = { "Panel", "Stack", "Mask", "Tip", "Hud", "Layer", "Diagnostic", "Events" };

            foreach (var name in groups)
            {
                var nested = typeof(UIManager).GetNestedType(name, BindingFlags.Public);

                Assert.IsNotNull(nested, $"门面应暴露 {name} 分组");
                Assert.IsTrue(nested.IsAbstract && nested.IsSealed,
                    $"{name} 应是静态分组类（零状态、零分配）");
            }
        }

        [Test]
        public void Facade_DoesNotExposeFlatPanelMembers()
        {
            // 这些成员必须留在分组里；一旦有人把它们加回扁平层，IntelliSense 会同时出现
            // 两种风格，读者得猜哪些分过组
            string[] mustBeGrouped = { "OpenAsync", "CloseAsync", "PushAsync", "PopAsync", "ShowMask",
                "ShowTipAsync", "ShowHudAsync", "SetLayerInteractive", "GetState", "Subscribe" };

            foreach (var name in mustBeGrouped)
            {
                Assert.IsNull(typeof(UIManager).GetMethod(name, PublicStatic),
                    $"{name} 不该出现在门面扁平层——它属于某个子系统分组");
            }
        }

        [Test]
        public void Facade_LifecycleMembersStayOnOuterClass()
        {
            // 生命周期与实例管理留在外层：它们是「门面」本身的职责，不属于任何子系统
            Assert.IsNotNull(typeof(UIManager).GetMethod("Initialize", PublicStatic));
            Assert.IsNotNull(typeof(UIManager).GetMethod("Destroy", PublicStatic));
            Assert.IsNotNull(typeof(UIManager).GetMethod("Update", PublicStatic));
            Assert.IsNotNull(typeof(UIManager).GetProperty("IsInitialized", PublicStatic));
            Assert.IsNotNull(typeof(UIManager).GetProperty("UIRoot", PublicStatic));
        }

        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            // 与 UILayerApiTests 里的同名断言互补：那条锁的是「不加 public Instance」，
            // 这条确保分组重构也没有顺手带出一个
            Assert.IsNull(typeof(UIManager).GetProperty("Instance", PublicStatic));
        }
    }
}
