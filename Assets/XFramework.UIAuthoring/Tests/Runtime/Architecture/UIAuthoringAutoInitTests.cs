using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 自动初始化族级守卫（本包版）：Runtime 程序集的每个自初始化入口都必须登记在允许清单里。
    /// <para>阶段 3 起唯一的合法入口是 <c>UIDataProviderRegistry.ResetForNewSession</c>（会话级静态复位）；
    /// 新增任何其它入口即红——先评估「这个静态状态是否真的需要跨会话清理」再登记。</para>
    /// </summary>
    [TestFixture]
    public sealed class UIAuthoringAutoInitTests
    {
        /// <summary>允许的自动初始化入口（类型全名 + 方法名）。</summary>
        private static readonly string[] AllowedAutoInit =
        {
            "XFramework.XUI.Authoring.UIDataProviderRegistry.ResetForNewSession",
        };

        [Test]
        public void RuntimeAssembly_AutoInitIsRegistered()
        {
            var offenders = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var type in typeof(UIDeclarativePanel).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(flags))
                {
                    if (method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null)
                        Check(type, method, "[RuntimeInitializeOnLoadMethod]", offenders);
#if UNITY_EDITOR
                    if (method.GetCustomAttribute<UnityEditor.InitializeOnLoadMethodAttribute>() != null)
                        Check(type, method, "[InitializeOnLoadMethod]", offenders);
#endif
                }
            }

            Assert.IsEmpty(offenders,
                "UIAuthoring Runtime 程序集出现未登记的自动初始化入口；若确属新成员，" +
                "先把全名加进 AllowedAutoInit 并说明理由：" +
                "\n  " + string.Join("\n  ", offenders));
        }

        private static void Check(System.Type type, MethodInfo method, string attribute,
            List<string> offenders)
        {
            string name = type.FullName + "." + method.Name;
            for (int i = 0; i < AllowedAutoInit.Length; i++)
            {
                if (AllowedAutoInit[i] == name)
                    return;
            }

            offenders.Add(name + " " + attribute);
        }
    }
}
