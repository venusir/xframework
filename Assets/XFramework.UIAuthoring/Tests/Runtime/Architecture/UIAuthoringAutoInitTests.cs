using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 自动初始化族级守卫（本包版）：扩展包的 Runtime 程序集不应有任何
    /// <c>[RuntimeInitializeOnLoadMethod]</c> / <c>[InitializeOnLoadMethod]</c> 入口；
    /// 出现即红，新增时必须先评估并更新本守卫（Fmod 的「无 AutoInit」形态）。
    /// </summary>
    [TestFixture]
    public sealed class UIAuthoringAutoInitTests
    {
        [Test]
        public void RuntimeAssembly_HasNoAutoInit()
        {
            var offenders = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var type in typeof(UIDeclarativePanel).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(flags))
                {
                    if (method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null)
                        offenders.Add(type.FullName + "." + method.Name + " [RuntimeInitializeOnLoadMethod]");
#if UNITY_EDITOR
                    if (method.GetCustomAttribute<UnityEditor.InitializeOnLoadMethodAttribute>() != null)
                        offenders.Add(type.FullName + "." + method.Name + " [InitializeOnLoadMethod]");
#endif
                }
            }

            Assert.IsEmpty(offenders,
                "UIAuthoring Runtime 程序集不应有自动初始化入口；若确需新增，先更新本守卫并登记：" +
                "\n  " + string.Join("\n  ", offenders));
        }
    }
}
