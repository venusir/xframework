using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using XFramework.XAsset;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 门面完备性测试：<see cref="IAssetManager"/> 的每个声明成员，都必须能在 <see cref="AssetManager"/>
    /// 上找到**同名**的 public static 转发。
    /// <para>体例照 <c>UIFacadeCompletenessTests</c> / <c>SettingsFacadeCompletenessTests</c> /
    /// <c>LocalizationFacadeCompletenessTests</c> / <c>InputFacadeCompletenessTests</c> /
    /// <c>FileFacadeCompletenessTests</c> / <c>DataFacadeCompletenessTests</c> /
    /// <c>SaveFacadeCompletenessTests</c> / <c>ConfigFacadeCompletenessTests</c>，本条是第九个采用者。</para>
    /// <para><b>只查名字，不查签名</b>：签名由编译器兜底——转发体是经 <see cref="IAssetManager"/> 的调用，
    /// 签名不符根本编译不过。于是唯一会漏的就是「压根没写转发」，而名字正是它的判据。
    /// 本模块的 32 个接口成员今天**全部有**同名转发——它是守卫，不是回归。</para>
    /// <para><b>它是单向的</b>：只断言「接口有、门面缺」。本门面另有 5 个自有成员
    /// （<c>IsInitialized</c>、<c>SetInstance</c>、<c>Destroy</c>，以及能力接口的 <c>ClearPool</c>/<c>ClearAllPools</c>），
    /// 反向断言会把它们判成缺陷，故由另外两条断言单独钉住。</para>
    /// <para><b>IDisposable 不会误报</b>：<see cref="IAssetManager"/> 继承 <c>IDisposable</c>，而门面用
    /// <c>Destroy()</c> 表达注销（名字不同）。实测确认 <c>typeof(I).GetMethods()</c> **只返回接口自己声明**的
    /// 成员（用 BCL 的 <c>IList&lt;int&gt;</c> 验证：它不返回 <c>ICollection&lt;T&gt;</c>/<c>IEnumerable&lt;T&gt;</c>
    /// 的任何方法），故 <c>Dispose</c> 不会进入这个集合。</para>
    /// <para><b>拦不住什么</b>：转发接错线与假转发（空实现、抛异常）反射都看不出来，那两条只能靠行为测试
    /// （见 <c>AssetManagerFacadeTests</c>）。别高估这一条。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>：本 fixture 是纯反射，不 Initialize、不注入实例、
    /// 不写任何静态字段，故没有需要复位的门面。</para>
    /// </summary>
    [TestFixture]
    public class AssetFacadeCompletenessTests
    {
        #region Private Fields

        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// 门面的 public static 成员名集合。域内只构建一次（反射结果必须缓存）。
        /// </summary>
        private static readonly HashSet<string> FacadeNames = BuildFacadeNames(typeof(AssetManager));

        #endregion

        #region Tests

        [Test]
        public void Facade_ForwardsEveryInterfaceMember()
        {
            var missing = new List<string>();
            foreach (var name in InterfaceMemberNames(typeof(IAssetManager)))
            {
                if (!FacadeNames.Contains(name))
                    missing.Add(name);
            }

            Assert.IsEmpty(missing,
                "以下 IAssetManager 成员在 AssetManager 上找不到同名转发。新增接口成员时必须同步在门面加" +
                "静态转发，否则它在第三方眼里根本不存在：\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// 可选能力接口的成员同样要转发。
        /// <para>比八个先例多出的一条：能力接口（<see cref="IAssetPoolController"/>）也是**公开面**——
        /// 门面用 <c>is</c> 探测后转发，漏一条同样是「第三方够不到」，而上面那条断言只管
        /// <see cref="IAssetManager"/>，管不到这里。</para>
        /// </summary>
        [Test]
        public void Facade_ForwardsEveryPoolCapabilityMember()
        {
            var missing = new List<string>();
            foreach (var name in InterfaceMemberNames(typeof(IAssetPoolController)))
            {
                if (!FacadeNames.Contains(name))
                    missing.Add(name);
            }

            Assert.IsEmpty(missing,
                "以下 IAssetPoolController 成员在 AssetManager 上找不到同名转发：" +
                "\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// 生命周期与实例管理留在门面外层。
        /// <para>它们不对应任何 <see cref="IAssetManager"/> 成员，故上面那条覆盖断言管不到，需要单独钉住。
        /// <c>InitializeAsync</c> 不在此列——它是接口成员，已被第一条覆盖。</para>
        /// </summary>
        [Test]
        public void Facade_KeepsLifecycleMembers()
        {
            foreach (var name in new[] { "IsInitialized", "SetInstance", "Destroy" })
            {
                Assert.IsTrue(FacadeNames.Contains(name),
                    $"门面缺少生命周期成员 {name}——它不对应 IAssetManager 的成员，这条断言是它唯一的守卫。");
            }
        }

        /// <summary>
        /// 门面必须保持扁平。
        /// <para>分组会把「门面名 == 接口名」这条唯一的人工核对手段换掉（转发时必然改名），
        /// 而它换来的 IntelliSense 分组在本仓没有任何门面认为值得付这个价。</para>
        /// </summary>
        [Test]
        public void Facade_StaysFlat()
        {
            var nested = typeof(AssetManager).GetNestedTypes(BindingFlags.Public);

            Assert.IsEmpty(nested,
                "门面不应有 public 嵌套类型——分组会让门面成员名与 IAssetManager 脱钩，上面那条同名断言随即失效。");
        }

        /// <summary>
        /// 门面不得暴露实例属性。
        /// <para>一旦暴露，<see cref="IAssetManager"/> 成员的增加会自动成为公开 API，跳过评审。
        /// 本门面的 <c>_instance</c> 刻意是 private。</para>
        /// </summary>
        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            Assert.IsNull(typeof(AssetManager).GetProperty("Instance", PublicStatic));
            Assert.IsNull(typeof(AssetManager).GetProperty("Impl", PublicStatic));
        }

        #endregion

        #region Reflection

        /// <summary>
        /// 接口自己声明的成员名（方法与属性）。
        /// <para>用 <c>GetMethods</c> + <c>GetProperties</c> 而不是 <c>GetMembers</c>：接口的**继承**成员
        /// 不会被返回（<see cref="IAssetManager"/> 继承 <c>IDisposable</c>，而门面用 <c>Destroy()</c>
        /// 表达注销，名字不同，只查同名转发本就不会把它算进来）。</para>
        /// </summary>
        private static IEnumerable<string> InterfaceMemberNames(Type interfaceType)
        {
            foreach (var property in interfaceType.GetProperties())
                yield return property.Name;

            foreach (var method in interfaceType.GetMethods())
            {
                // 属性访问器是 SpecialName，不过滤会被当成 get_Xxx 方法重复计入
                if (!method.IsSpecialName)
                    yield return method.Name;
            }
        }

        private static HashSet<string> BuildFacadeNames(Type facadeType)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in facadeType.GetProperties(PublicStatic))
                names.Add(property.Name);

            foreach (var method in facadeType.GetMethods(PublicStatic))
            {
                if (!method.IsSpecialName)
                    names.Add(method.Name);
            }

            return names;
        }

        #endregion
    }
}
