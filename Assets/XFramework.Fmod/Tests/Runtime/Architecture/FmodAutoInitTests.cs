using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// Phase 1 的 provider 程序集不应有 AutoInit；自动安装属于 Phase 2 条件集成程序集。
    /// </summary>
    [TestFixture]
    public class FmodAutoInitTests
    {
        private static readonly Regex RuntimeEntryPattern = new Regex(
            @"^[ \t]*\[\s*(?:UnityEngine\.)?RuntimeInitializeOnLoadMethod(?:Attribute)?" +
            @"\s*\(\s*(?:UnityEngine\.)?RuntimeInitializeLoadType\.BeforeSceneLoad\s*\)\s*\]",
            RegexOptions.Multiline);

        private static readonly Regex EditorEntryPattern = new Regex(
            @"^[ \t]*\[\s*(?:UnityEditor\.)?InitializeOnLoadMethod(?:Attribute)?" +
            @"\s*(?:\(\s*\))?\s*\]",
            RegexOptions.Multiline);

        [Test]
        public void IntegrationAutoInstall_HasRuntimeEntryAndEscapeHatch()
        {
            string source = FmodTestPaths.ReadText("Runtime/Integration/FmodAutoInstall.cs");
            if (source == null)
                Assert.Ignore("找不到 Integration/FmodAutoInstall.cs；跳过 AutoInit 源码守卫。");

            Assert.IsTrue(RuntimeEntryPattern.IsMatch(source),
                "自动安装必须挂 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]。");
            Assert.IsFalse(EditorEntryPattern.IsMatch(source),
                "自动安装不应挂编辑器 InitializeOnLoadMethod；它只在运行时创建 FMOD 实例。");
            StringAssert.Contains("XFW_DISABLE_FMOD_AUTOINSTALL", source);
        }

        [Test]
        public void AutoInitSourceValidator_RequiresBeforeSceneLoadAttribute()
        {
            const string afterOnly =
                "// BeforeSceneLoad\n" +
                "[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]\n" +
                "private static void AutoInstall() {}";
            Assert.IsFalse(RuntimeEntryPattern.IsMatch(afterOnly),
                "注释里的 BeforeSceneLoad 不应让错误档位蒙混过关。");

            const string correct =
                "[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]\n" +
                "private static void AutoInstall() {}";
            Assert.IsTrue(RuntimeEntryPattern.IsMatch(correct));
        }

        [Test]
        public void AutoInitSourceValidator_RejectsEditorAttributeAliases()
        {
            string[] aliases =
            {
                "[InitializeOnLoadMethod]",
                "[InitializeOnLoadMethod()]",
                "[UnityEditor.InitializeOnLoadMethod]",
                "[UnityEditor.InitializeOnLoadMethodAttribute]",
            };

            foreach (string alias in aliases)
                Assert.IsTrue(EditorEntryPattern.IsMatch(alias), "应命中：" + alias);
        }

        [Test]
        public void ProviderAssembly_HasNoAutoInit()
        {
            var offenders = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var type in typeof(FmodAudioSettings).Assembly.GetTypes())
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
                "Phase 1 的 FMOD-free provider 程序集不应有 AutoInit；自动安装应在 Phase 2 条件集成程序集：" +
                "\n  " + string.Join("\n  ", offenders));
        }
    }
}
