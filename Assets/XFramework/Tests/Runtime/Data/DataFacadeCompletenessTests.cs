using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using XFramework.XData;

namespace XFramework.XData.Tests
{
    /// <summary>
    /// 门面完备性测试：<see cref="IDataManager"/> 的每个声明成员，都必须能在 <see cref="DataManager"/>
    /// 上找到**同名**的 public static 转发。
    /// <para>体例照 <c>UIFacadeCompletenessTests</c> / <c>SettingsFacadeCompletenessTests</c> /
    /// <c>LocalizationFacadeCompletenessTests</c> / <c>InputFacadeCompletenessTests</c> /
    /// <c>FileFacadeCompletenessTests</c>，本条是第六个采用者。</para>
    /// <para><b>这条守卫在本模块是有活证据的</b>：它加进来之前，<see cref="IDataManager.ForEachBlock"/>
    /// 是全仓唯一一个「接口声明了、实现也有、门面上却没有同名转发」的成员——第三方经
    /// <see cref="DataManager"/> 根本调不到，仓内也零调用、零测试。补转发与加守卫是同一批做的；
    /// 把转发临时撤掉即可看到这条断言变红。</para>
    /// <para><b>只查名字，不查签名</b>：签名由编译器兜底——转发体是经 <see cref="IDataManager"/> 的调用，
    /// 签名不符根本编译不过。于是唯一会漏的就是「压根没写转发」，而名字正是它的判据。</para>
    /// <para><b>它是单向的</b>：只断言「接口有、门面缺」。本门面另有 3 个生命周期成员
    /// （<c>IsInitialized</c> / <c>Initialize</c> / <c>Shutdown</c>）不在接口上，反向断言会把它们判成缺陷。</para>
    /// <para><b>拦不住什么</b>：转发接错线与假转发（空实现、抛异常）反射都看不出来，那两条只能靠行为测试。
    /// 别高估这一条。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>：本 fixture 是纯反射，不 Initialize、不注入实现、
    /// 不写任何静态字段，故没有需要复位的门面。</para>
    /// </summary>
    [TestFixture]
    public class DataFacadeCompletenessTests
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
                "以下 IDataManager 成员在 DataManager 上找不到同名转发。新增接口成员时必须同步在门面加静态" +
                "转发，否则它在第三方眼里根本不存在：\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// 门面必须保持扁平。
        /// <para>分组会把「门面名 == 接口名」这条唯一的人工核对手段换掉（转发时必然改名），
        /// 而它换来的 IntelliSense 分组在本仓没有任何门面认为值得付这个价。</para>
        /// </summary>
        [Test]
        public void Facade_StaysFlat()
        {
            var nested = typeof(DataManager).GetNestedTypes(BindingFlags.Public);

            Assert.IsEmpty(nested,
                "门面不应有 public 嵌套类型——分组会让门面成员名与 IDataManager 脱钩，上面那条同名断言随即失效。");
        }

        /// <summary>
        /// 生命周期与实例管理留在门面外层。
        /// <para>它们不对应任何 <see cref="IDataManager"/> 成员，故上面那条覆盖断言管不到，需要单独钉住。
        /// 注意 <c>Initialize</c> 在本模块是**注入**（参数是 <see cref="IDataManager"/>），
        /// 与 Save 的工厂注入不同形——门面完备性只看名字，故这条只断言存在。</para>
        /// </summary>
        [Test]
        public void Facade_KeepsLifecycleMembers()
        {
            foreach (var name in new[] { "IsInitialized", "Initialize", "Shutdown" })
            {
                Assert.IsTrue(FacadeNames.Contains(name),
                    $"门面缺少生命周期成员 {name}——它不对应 IDataManager 的成员，这条断言是它唯一的守卫。");
            }
        }

        /// <summary>
        /// 门面不得暴露实例属性。
        /// <para>一旦暴露，<see cref="IDataManager"/> 成员的增加会自动成为公开 API，跳过评审。
        /// 本门面的 <c>_impl</c> 刻意是 private。</para>
        /// </summary>
        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            Assert.IsNull(typeof(DataManager).GetProperty("Instance", PublicStatic));
            Assert.IsNull(typeof(DataManager).GetProperty("Impl", PublicStatic));
        }

        #endregion

        #region Reflection

        /// <summary>
        /// 接口自己声明的成员名（方法与属性）。
        /// <para>用 <c>GetProperties</c> + <c>GetMethods</c> 而不是 <c>GetMembers</c>：接口的**继承**成员
        /// 不会被返回。<see cref="IDataManager"/> 本就不继承任何接口，这条写法是为与另外五个采用者
        /// 保持一致。</para>
        /// </summary>
        private static IEnumerable<string> InterfaceMemberNames()
        {
            foreach (var property in typeof(IDataManager).GetProperties())
                yield return property.Name;

            foreach (var method in typeof(IDataManager).GetMethods())
            {
                // 属性访问器是 SpecialName，不过滤会被当成 get_Xxx 方法重复计入
                if (!method.IsSpecialName)
                    yield return method.Name;
            }
        }

        private static HashSet<string> BuildFacadeNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in typeof(DataManager).GetProperties(PublicStatic))
                names.Add(property.Name);

            foreach (var method in typeof(DataManager).GetMethods(PublicStatic))
            {
                if (!method.IsSpecialName)
                    names.Add(method.Name);
            }

            return names;
        }

        #endregion
    }
}
