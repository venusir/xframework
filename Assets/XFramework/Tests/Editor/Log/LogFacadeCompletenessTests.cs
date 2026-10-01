using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using XFramework.XLog;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 门面完备性测试：<see cref="ILogManager"/> 的每个声明成员，都必须能在 <see cref="LogManager"/>
    /// 上找到**同名 + 同元数**的 public static 转发。
    /// <para><b>为什么必须有这一条</b>：转发不变量——「接口新增成员必须同步在门面加转发，否则它在
    /// 第三方眼里根本不存在」。本模块的接口有 30+ 成员（6 档 × 4 元数 + 通用入口 + 输出端），
    /// 靠评审记住「新加一个重载要在两处写」不现实，十几行反射就能锁死。</para>
    /// <para><b>为什么只查名字与元数，不查类型</b>：类型由编译器兜底——转发体是经 <see cref="ILogManager"/>
    /// 的调用，签名不符根本编译不过。**但名字不够**：`Exception` 这类有重载的成员，门面上只转发其中
    /// 一个重载时「同名」照样成立（本测试因此从只查名字升级到名字 + 元数，与
    /// <c>UIFacadeCompletenessTests</c> 的形态保持同源）。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>：本 fixture 是纯反射，不 Configure、不 Shutdown、
    /// 不写任何静态字段，故没有需要复位的东西。</para>
    /// </summary>
    [TestFixture]
    public class LogFacadeCompletenessTests
    {
        #region Private Fields

        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        /// <summary>门面 public static 成员的签名集合（方法 = 名字 + 元数；属性 = 名字）。</summary>
        private static readonly HashSet<string> FacadeSignatures = BuildFacadeSignatures();

        #endregion

        #region Tests

        [Test]
        public void Facade_ForwardsEveryInterfaceMember()
        {
            var missing = new List<string>();
            foreach (string signature in InterfaceSignatures())
            {
                if (!FacadeSignatures.Contains(signature))
                    missing.Add(signature);
            }

            Assert.IsEmpty(missing,
                "以下 ILogManager 成员（方法记为「名字/元数」）在 LogManager 上找不到对应转发。" +
                "新增接口成员时必须同步在门面加静态转发，否则它在第三方眼里根本不存在：\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>门面必须保持扁平：分组会让「门面名 == 接口名」这条唯一的人工核对手段失效。</summary>
        [Test]
        public void Facade_StaysFlat()
        {
            Assert.IsEmpty(typeof(LogManager).GetNestedTypes(BindingFlags.Public),
                "门面不应有 public 嵌套类型——分组会让门面成员名与 ILogManager 脱钩。");
        }

        /// <summary>生命周期成员不对应接口成员，上面那条覆盖不到，单独钉住。</summary>
        [Test]
        public void Facade_KeepsLifecycleMembers()
        {
            Assert.IsNotNull(typeof(LogManager).GetMethod("Initialize", PublicStatic));
            Assert.IsNotNull(typeof(LogManager).GetMethod("Configure", PublicStatic));
            Assert.IsNotNull(typeof(LogManager).GetMethod("Shutdown", PublicStatic));
            Assert.IsNotNull(typeof(LogManager).GetMethod("Flush", PublicStatic));
        }

        /// <summary>门面不得暴露实例属性——一旦暴露，接口成员的增加会自动成为公开 API，跳过评审。</summary>
        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            Assert.IsNull(typeof(LogManager).GetProperty("Instance", PublicStatic));
        }

        #endregion

        #region Reflection

        private static IEnumerable<string> InterfaceSignatures()
        {
            foreach (PropertyInfo property in typeof(ILogManager).GetProperties())
                yield return property.Name;

            foreach (MethodInfo method in typeof(ILogManager).GetMethods())
            {
                // 属性访问器是 SpecialName，不过滤会被当成 get_Xxx 方法重复计入
                if (!method.IsSpecialName)
                    yield return Signature(method);
            }
        }

        private static HashSet<string> BuildFacadeSignatures()
        {
            var signatures = new HashSet<string>(StringComparer.Ordinal);

            foreach (PropertyInfo property in typeof(LogManager).GetProperties(PublicStatic))
                signatures.Add(property.Name);

            foreach (MethodInfo method in typeof(LogManager).GetMethods(PublicStatic))
            {
                if (!method.IsSpecialName)
                    signatures.Add(Signature(method));
            }

            return signatures;
        }

        /// <summary>方法签名 = 名字 + 元数（带默认值的参数也计入，与 cref 的写法规则一致）。</summary>
        private static string Signature(MethodInfo method)
        {
            return method.Name + "/" + method.GetParameters().Length;
        }

        #endregion
    }
}
