using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 门面完备性测试：<see cref="ISettingsManager{T}"/> 的每个声明成员，都必须能在
    /// <see cref="SettingsManager"/> 上找到**同名**（或映射表指定的）public static 转发。
    /// <para><b>为什么必须有这一条</b>：转发不变量——「接口新增成员必须同步在门面加转发，否则它在
    /// 第三方眼里根本不存在」——此前只靠评审，于是 <c>Migrator</c> 长期处于「<see cref="ISettingsManager{T}"/>
    /// 上有、文档也提、门面上却查无此成员」的状态：第三方只能从 <c>Initialize</c> 的返回值上挂迁移器，
    /// 初始化一旦发生在别处就够不着。同 <c>UIManager.SetLayerVisibility</c> 是同一类缺陷。</para>
    /// <para><b>为什么只查名字，不查签名</b>：签名由编译器兜底——转发体是经 <see cref="ISettingsManager{T}"/>
    /// 的调用，签名不符根本编译不过。于是唯一会漏的就是「压根没写转发」，而名字正是它的判据。</para>
    /// <para><b>拦不住什么</b>：转发接错线（<c>SetMigrator</c> 转发到了 <c>SetStore</c>）与假转发
    /// （空实现、抛异常）反射都看不出来。别高估这一条——门面那侧因此另有一条行为用例
    /// （<c>SettingsManagerFacadeTests.SetMigrator_WiresThroughToManager</c>）补这个缺口。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>：本 fixture 是纯反射，不 Initialize、不 Destroy、
    /// 不写任何静态字段，故没有需要复位的东西。</para>
    /// </summary>
    [TestFixture]
    public class SettingsFacadeCompletenessTests
    {
        #region Test Doubles

        /// <summary>
        /// 仅用于把开放泛型 <c>ISettingsManager&lt;&gt;</c> 闭合出来。
        /// <para>取 <c>typeof(ISettingsManager&lt;ProbeSettings&gt;)</c> 而不是 <c>typeof(ISettingsManager&lt;&gt;)</c>：
        /// 成员名与类型参数无关，闭合之后不必去赌开放泛型上的反射边界行为。</para>
        /// </summary>
        private sealed class ProbeSettings
        {
        }

        #endregion

        #region Private Fields

        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// 接口成员名 → 门面成员名集合。只有「一对 Get/Set 访问器」形状的成员需要映射，
        /// 其余接口名与门面名一一对应。
        /// <para><b>这是本文件唯一允许的第二份真相</b>，故刻意保持最小：只列改名的那几个，
        /// 且要求列出的名字<b>全部</b>存在——「成对齐全」正是本模块对 <c>Store</c> 的既有约定
        /// （只有 setter 会让读取侧静默缺失，而那条缺陷不会以任何方式报警）。</para>
        /// </summary>
        private static readonly Dictionary<string, string[]> RenamedMembers = new(StringComparer.Ordinal)
        {
            ["Store"] = new[] { "GetStore", "SetStore" },
            ["Migrator"] = new[] { "GetMigrator", "SetMigrator" },
        };

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
                var expected = RenamedMembers.TryGetValue(name, out var mapped) ? mapped : new[] { name };

                for (var i = 0; i < expected.Length; i++)
                {
                    if (!FacadeNames.Contains(expected[i]))
                        missing.Add($"ISettingsManager<>.{name} → SettingsManager.{expected[i]}");
                }
            }

            Assert.IsEmpty(missing,
                "以下接口成员在门面上找不到对应转发。新增接口成员时必须同步在门面加静态转发，" +
                "否则它在第三方眼里根本不存在：\n  " + string.Join("\n  ", missing));
        }

        /// <summary>
        /// 门面必须保持扁平。
        /// <para>分组会把「门面名 == 接口名」这条唯一的人工核对手段换掉（转发时必然改名），
        /// 而它换来的 IntelliSense 分组在本仓其它门面（<c>MessageManager</c> 48 个成员、
        /// <c>InputManager</c> 43 个）上都没被采用。<c>UIManager</c> 曾短暂分组过，未发布即撤销。</para>
        /// <para>在 Settings 这边比 UI 更值：<c>Settings&lt;T&gt;()</c> 正是有人会想归到
        /// 「数据访问」静态类里去的成员。</para>
        /// </summary>
        [Test]
        public void Facade_StaysFlat()
        {
            var nested = typeof(SettingsManager).GetNestedTypes(BindingFlags.Public);

            Assert.IsEmpty(nested,
                "门面不应有 public 嵌套类型——分组会让门面成员名与 ISettingsManager<> 脱钩，" +
                "上面那条同名断言随即失效。");
        }

        /// <summary>
        /// 生命周期成员留在门面外层。
        /// <para>它们不对应任何 <see cref="ISettingsManager{T}"/> 成员，故上面那条覆盖断言管不到，
        /// 需要单独钉住。</para>
        /// <para>不用 <c>GetMethod</c> 查名：<c>Initialize</c> 有三个重载，<c>GetMethod</c> 会抛
        /// <see cref="AmbiguousMatchException"/>。</para>
        /// </summary>
        [Test]
        public void Facade_KeepsLifecycleMembers()
        {
            Assert.IsTrue(FacadeNames.Contains("Initialize"), "门面必须保留 Initialize");
            Assert.IsTrue(FacadeNames.Contains("Destroy"), "门面必须保留 Destroy");
        }

        /// <summary>
        /// 门面不得暴露实例属性。
        /// <para>一旦暴露，<see cref="ISettingsManager{T}"/> 成员的增加会自动成为公开 API，跳过评审。</para>
        /// </summary>
        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            Assert.IsNull(typeof(SettingsManager).GetProperty("Instance", PublicStatic));
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
            var contract = typeof(ISettingsManager<ProbeSettings>);

            foreach (var property in contract.GetProperties())
                yield return property.Name;

            foreach (var method in contract.GetMethods())
            {
                // 属性访问器是 SpecialName，不过滤会被当成 get_Xxx 方法重复计入
                if (!method.IsSpecialName)
                    yield return method.Name;
            }
        }

        private static HashSet<string> BuildFacadeNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in typeof(SettingsManager).GetProperties(PublicStatic))
                names.Add(property.Name);

            foreach (var method in typeof(SettingsManager).GetMethods(PublicStatic))
            {
                if (!method.IsSpecialName)
                    names.Add(method.Name);
            }

            return names;
        }

        #endregion
    }
}
