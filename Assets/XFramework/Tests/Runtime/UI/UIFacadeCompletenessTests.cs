using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 门面完备性测试：<see cref="IUIManager"/> 的每个声明成员，都必须能在 <see cref="UIManager"/>
    /// 上找到**同名**的 public static 转发。
    /// <para><b>为什么必须有这一条</b>：转发不变量——「接口新增成员必须同步在门面加转发，否则它在
    /// 第三方眼里根本不存在」——此前只靠评审。<c>SetLayerVisibility</c> 曾长期处于「实现完整、文档也有，
    /// 但只在内部实现上，门面既无转发也无实例属性」的状态，第三方实际完全调不到。</para>
    /// <para><b>为什么只查名字，不查签名</b>：签名由编译器兜底——转发体是经 <see cref="IUIManager"/>
    /// 的调用，签名不符根本编译不过。于是唯一会漏的就是「压根没写转发」，而名字正是它的判据。
    /// 这也让本测试不需要任何映射表：接口名 == 门面名，没有第二份真相。</para>
    /// <para><b>拦不住什么</b>：转发接错线（<c>SetLayerVisibility</c> 转发到了
    /// <c>SetLayerInteractive</c>）与假转发（空实现、抛异常）反射都看不出来，那两条只能靠行为测试。
    /// 别高估这一条。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>：本 fixture 是纯反射，不 Initialize、不 SetInstance、
    /// 不写任何静态字段，故没有需要复位的东西。</para>
    /// </summary>
    [TestFixture]
    public class UIFacadeCompletenessTests
    {
        #region Private Fields

        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// 门面的 public static 成员名集合。域内只构建一次（反射结果必须缓存）。
        /// <para>刻意不用 <c>CollectionPool</c>：池化服务于每帧路径上反复借还的场景，这里是一次性构建的
        /// 只读索引，池化只会引入归还语义与生命周期耦合。</para>
        /// </summary>
        private static readonly HashSet<string> FacadeNames = BuildFacadeNames();

        #endregion

        #region Tests

        [Test]
        public void Facade_ForwardsEveryInterfaceMember()
        {
            var missing = new List<string>();
            foreach (var name in InterfaceMemberNames())
            {
                if (!FacadeNames.Contains(name))
                    missing.Add(name);
            }

            Assert.IsEmpty(missing,
                "以下 IUIManager 成员在 UIManager 上找不到同名转发。新增接口成员时必须同步在门面加静态转发，" +
                "否则它在第三方眼里根本不存在：\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// 门面必须保持扁平。
        /// <para>分组会把「门面名 == 接口名」这条唯一的人工核对手段换掉（转发时必然改名），
        /// 而它换来的 IntelliSense 分组在本仓其它门面（<c>MessageManager</c> 48 个成员、
        /// <c>InputManager</c> 43 个）上都没被采用。<c>UIManager</c> 曾短暂分组过，未发布即撤销。</para>
        /// </summary>
        [Test]
        public void Facade_StaysFlat()
        {
            var nested = typeof(UIManager).GetNestedTypes(BindingFlags.Public);

            Assert.IsEmpty(nested,
                "门面不应有 public 嵌套类型——分组会让门面成员名与 IUIManager 脱钩，" +
                "上面那条同名断言随即失效。私有嵌套类（FrameDriver / TierDriver）不受影响。");
        }

        /// <summary>
        /// 生命周期与实例管理留在门面外层。
        /// <para>它们不对应任何 <see cref="IUIManager"/> 成员，故上面那条覆盖断言管不到，
        /// 需要单独钉住。</para>
        /// </summary>
        [Test]
        public void Facade_KeepsLifecycleMembers()
        {
            Assert.IsNotNull(typeof(UIManager).GetMethod("Initialize", PublicStatic));
            Assert.IsNotNull(typeof(UIManager).GetMethod("SetInstance", PublicStatic));
            Assert.IsNotNull(typeof(UIManager).GetMethod("Destroy", PublicStatic));
        }

        /// <summary>
        /// 门面不得暴露实例属性。
        /// <para>一旦暴露，<see cref="IUIManager"/> 成员的增加会自动成为公开 API，跳过评审。</para>
        /// </summary>
        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            Assert.IsNull(typeof(UIManager).GetProperty("Instance", PublicStatic));
        }

        #endregion

        #region Reflection

        /// <summary>
        /// 接口自己声明的成员名（方法与属性）。
        /// <para>用 <c>GetProperties</c> + <c>GetMethods</c> 而不是 <c>GetMembers</c>：接口的
        /// <b>继承</b>成员不会被返回，而 <c>Dispose()</c> 正属此类——门面用 <c>Destroy</c> 表达同一职责，
        /// 本来就没有 <c>Dispose</c> 转发，这个语义正是我们要的。</para>
        /// </summary>
        private static IEnumerable<string> InterfaceMemberNames()
        {
            foreach (var property in typeof(IUIManager).GetProperties())
                yield return property.Name;

            foreach (var method in typeof(IUIManager).GetMethods())
            {
                // 属性访问器是 SpecialName，不过滤会被当成 get_Xxx 方法重复计入
                if (!method.IsSpecialName)
                    yield return method.Name;
            }
        }

        private static HashSet<string> BuildFacadeNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in typeof(UIManager).GetProperties(PublicStatic))
                names.Add(property.Name);

            foreach (var method in typeof(UIManager).GetMethods(PublicStatic))
            {
                if (!method.IsSpecialName)
                    names.Add(method.Name);
            }

            return names;
        }

        #endregion
    }
}
