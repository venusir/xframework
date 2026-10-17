using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// 门面完备性测试：<see cref="ICameraManager"/> 的每个声明成员，都必须能在
    /// <see cref="CameraManager"/> 上找到**同名**的 public static 转发。
    /// <para>签名不必测——转发体是经 <see cref="ICameraManager"/> 的调用，签名不符编译不过；
    /// 唯一会漏的是「压根没写转发」。与 <c>UIFacadeCompletenessTests</c> 同构。</para>
    /// <para>本 fixture 是纯反射：不初始化、不注入、不写静态字段，故没有需要复位的状态。</para>
    /// </summary>
    [TestFixture]
    public class CameraFacadeCompletenessTests
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        private static readonly HashSet<string> FacadeNames = BuildFacadeNames();

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
                "以下 ICameraManager 成员在 CameraManager 上找不到同名转发。新增接口成员时必须同步在门面加静态转发，" +
                "否则它在第三方眼里根本不存在：\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void Facade_StaysFlat()
        {
            var nested = typeof(CameraManager).GetNestedTypes(BindingFlags.Public);

            Assert.IsEmpty(nested,
                "门面不应有 public 嵌套类型——分组会让门面成员名与 ICameraManager 脱钩，上面那条同名断言随即失效。");
        }

        [Test]
        public void Facade_KeepsLifecycleMembers()
        {
            Assert.IsNotNull(typeof(CameraManager).GetMethod("Initialize", PublicStatic));
            Assert.IsNotNull(typeof(CameraManager).GetMethod("Destroy", PublicStatic));
            Assert.IsNotNull(typeof(CameraManager).GetProperty("IsInitialized", PublicStatic));
        }

        [Test]
        public void Facade_DoesNotExposeInstanceProperty()
        {
            Assert.IsNull(typeof(CameraManager).GetProperty("Instance", PublicStatic));
        }

        private static IEnumerable<string> InterfaceMemberNames()
        {
            foreach (var property in typeof(ICameraManager).GetProperties())
                yield return property.Name;

            foreach (var method in typeof(ICameraManager).GetMethods())
            {
                if (!method.IsSpecialName)
                    yield return method.Name;
            }
        }

        private static HashSet<string> BuildFacadeNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in typeof(CameraManager).GetProperties(PublicStatic))
                names.Add(property.Name);
            foreach (var method in typeof(CameraManager).GetMethods(PublicStatic))
                names.Add(method.Name);

            return names;
        }
    }
}
