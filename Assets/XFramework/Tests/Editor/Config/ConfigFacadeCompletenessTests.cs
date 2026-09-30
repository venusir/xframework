using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 门面完备性测试：<see cref="IConfigManager"/> 的每个声明成员，都必须能在 <see cref="ConfigManager"/>
    /// 上找到**同名**的 public static 转发。
    /// <para>体例照 <c>UIFacadeCompletenessTests</c> / <c>SettingsFacadeCompletenessTests</c> /
    /// <c>LocalizationFacadeCompletenessTests</c> / <c>InputFacadeCompletenessTests</c> /
    /// <c>FileFacadeCompletenessTests</c> / <c>DataFacadeCompletenessTests</c> /
    /// <c>SaveFacadeCompletenessTests</c>，本条是第八个采用者。</para>
    /// <para><b>只查名字，不查签名</b>：签名由编译器兜底——转发体是经 <see cref="IConfigManager"/> 的调用，
    /// 签名不符根本编译不过。于是唯一会漏的就是「压根没写转发」，而名字正是它的判据。</para>
    /// <para><b>它是单向的</b>：只断言「接口有、门面缺」。本门面另有 5 个自有成员
    /// （<c>ConfigChanged</c> 事件、<c>IsInitialized</c>、<c>Initialize</c>、<c>SetInstance</c>、
    /// <c>Destroy</c>），且 6 个异步成员在门面上各多一个 <c>CancellationToken</c> 形参——
    /// 反向断言与签名断言都会把它们判成缺陷。注意 <c>ConfigChanged</c> 是**字段式事件**，
    /// 反射里以 <c>add_</c>/<c>remove_</c> 访问器出现（SpecialName，已被过滤），不在本断言的范围内。</para>
    /// <para><b>今天是绿的</b>：本 fixture 是**新增守卫**，不是回归——17 个接口成员全部有同名转发，
    /// 所以它在加入的第一天就通过。它买的是「以后改坏即红」。</para>
    /// <para><b>拦不住什么</b>：转发接错线与假转发（空实现、抛异常）反射都看不出来，那两条只能靠行为测试
    /// （见 <c>ConfigManagerFacadeTests</c>）。别高估这一条。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>：本 fixture 是纯反射，不 Initialize、不注入实例、
    /// 不写任何静态字段，故没有需要复位的门面。</para>
    /// </summary>
    [TestFixture]
    public class ConfigFacadeCompletenessTests
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
                "以下 IConfigManager 成员在 ConfigManager 上找不到同名转发。新增接口成员时必须同步在门面加" +
                "静态转发，否则它在第三方眼里根本不存在：\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// 门面必须保持扁平。
        /// <para>分组会把「门面名 == 接口名」这条唯一的人工核对手段换掉（转发时必然改名），
        /// 而它换来的 IntelliSense 分组在本仓没有任何门面认为值得付这个价。</para>
        /// </summary>
        [Test]
        public void Facade_StaysFlat()
        {
            var nested = typeof(ConfigManager).GetNestedTypes(BindingFlags.Public);

            Assert.IsEmpty(nested,
                "门面不应有 public 嵌套类型——分组会让门面成员名与 IConfigManager 脱钩，上面那条同名断言随即失效。");
        }

        /// <summary>
        /// 生命周期与实例管理留在门面外层。
        /// <para>它们不对应任何 <see cref="IConfigManager"/> 成员，故上面那条覆盖断言管不到，需要单独钉住。
        /// <c>ConfigChanged</c> 是事件，不在这条里（见类文档的说明）。</para>
        /// </summary>
        [Test]
        public void Facade_KeepsLifecycleMembers()
        {
            foreach (var name in new[] { "IsInitialized", "Initialize", "SetInstance", "Destroy" })
            {
                Assert.IsTrue(FacadeNames.Contains(name),
                    $"门面缺少生命周期成员 {name}——它不对应 IConfigManager 的成员，这条断言是它唯一的守卫。");
            }
        }

        /// <summary>
        /// 门面不得暴露实例属性。
        /// <para>一旦暴露，<see cref="IConfigManager"/> 成员的增加会自动成为公开 API，跳过评审。
        /// 本门面的 <c>_instance</c> 刻意是 private。</para>
        /// </summary>
        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            Assert.IsNull(typeof(ConfigManager).GetProperty("Instance", PublicStatic));
            Assert.IsNull(typeof(ConfigManager).GetProperty("Impl", PublicStatic));
        }

        #endregion

        #region Reflection

        /// <summary>
        /// 接口自己声明的成员名（方法与属性）。
        /// <para>用 <c>GetProperties</c> + <c>GetMethods</c> 而不是 <c>GetMembers</c>：接口的**继承**成员
        /// 不会被返回。<see cref="IConfigManager"/> 本就不继承任何接口（尤其**不是** <c>IDisposable</c>——
        /// 门面用 <c>Destroy()</c> 表达注销，名字不同，只查同名转发本就不会把它算进来），
        /// 这条写法是为与另外七个采用者保持一致。</para>
        /// </summary>
        private static IEnumerable<string> InterfaceMemberNames()
        {
            foreach (var property in typeof(IConfigManager).GetProperties())
                yield return property.Name;

            foreach (var method in typeof(IConfigManager).GetMethods())
            {
                // 属性访问器是 SpecialName，不过滤会被当成 get_Xxx 方法重复计入
                if (!method.IsSpecialName)
                    yield return method.Name;
            }
        }

        private static HashSet<string> BuildFacadeNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in typeof(ConfigManager).GetProperties(PublicStatic))
                names.Add(property.Name);

            foreach (var method in typeof(ConfigManager).GetMethods(PublicStatic))
            {
                if (!method.IsSpecialName)
                    names.Add(method.Name);
            }

            return names;
        }

        #endregion
    }
}
