using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 依赖声明守卫：FMOD 不写进 package.json；Runtime 不引用 FMODUnity；README 声明消费方前置。
    /// </summary>
    [TestFixture]
    public class FmodDependencyDeclarationTests
    {
        private const string ForbiddenTypePrefix = "FMODUnity";

        /// <summary>
        /// 公开类型声明的源码扫描：覆盖修饰符、属性前缀与 <c>record</c>/<c>delegate</c>，
        /// 避免「Integration 全 internal」被新形状绕过。
        /// </summary>
        private static readonly Regex PublicTypePattern = new Regex(
            @"^[ \t]*(?:\[[^\]]*\][ \t]*)*public[ \t]+" +
            @"(?:(?:static|sealed|abstract|partial|readonly|unsafe|new|ref)[ \t]+)*" +
            @"(?:class|struct|interface|enum|record|delegate)\b",
            RegexOptions.Multiline);

#pragma warning disable CS0649 // JsonUtility 通过反射赋值
        [Serializable]
        private sealed class AsmdefGate
        {
            public string[] references;
            public string[] defineConstraints;
            public VersionDefine[] versionDefines;
        }

        [Serializable]
        private sealed class VersionDefine
        {
            public string name;
            public string expression;
            public string define;
        }
#pragma warning restore CS0649

        [Test]
        public void PackageJson_DependenciesStayEmpty()
        {
            string json = FmodTestPaths.ReadText("package.json");
            if (json == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过依赖声明守卫。");

            StringAssert.Contains("\"dependencies\": {}", json,
                "UPM 不支持 Git URL 依赖；FMOD 也不在公开 UPM 注册表，dependencies 必须为空。");
        }

        [Test]
        public void RuntimeSources_DoNotReferenceFmodUnity()
        {
            string root = FmodTestPaths.PackageRoot;
            if (root == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过依赖声明守卫。");

            var violations = new List<string>();
            string runtime = Path.Combine(root, "Runtime");
            string integration = Path.Combine(runtime, "Integration") + Path.DirectorySeparatorChar;
            foreach (string file in Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", StringComparison.Ordinal) &&
                    !file.EndsWith(".asmdef", StringComparison.Ordinal))
                {
                    continue;
                }

                // Integration/ 是条件集成程序集，允许引用 FMODUnity；由 IntegrationAsmdef 契约守卫。
                if (file.StartsWith(integration, StringComparison.Ordinal))
                    continue;

                if (File.ReadAllText(file).Contains(ForbiddenTypePrefix))
                    violations.Add(file);
            }

            Assert.IsEmpty(violations,
                "FMOD-free 层不得引用 FMODUnity：\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void IntegrationAsmdefs_BindDefineToConstraints()
        {
            AssertIntegrationAsmdefIsGated(
                "Runtime/Integration/Venusy609.Xframework.Fmod.Unity.asmdef");
            AssertIntegrationAsmdefIsGated(
                "Tests/Runtime/Integration/Venusy609.Xframework.Fmod.Unity.Tests.asmdef");
        }

        private static void AssertIntegrationAsmdefIsGated(string relativePath)
        {
            string json = FmodTestPaths.ReadText(relativePath);
            if (json == null)
                Assert.Ignore("找不到 " + relativePath + "；跳过条件门控守卫。");

            AsmdefGate gate;
            try
            {
                gate = JsonUtility.FromJson<AsmdefGate>(json);
            }
            catch (Exception)
            {
                gate = null;
            }

            Assert.IsNotNull(gate, "无法解析 asmdef JSON：" + relativePath);
            Assert.IsNotNull(gate.references, relativePath + " 缺少 references");
            Assert.GreaterOrEqual(Array.IndexOf(gate.references, "FMODUnity"), 0,
                relativePath + " 必须引用 FMODUnity");
            Assert.IsNotNull(gate.defineConstraints, relativePath + " 缺少 defineConstraints");

            VersionDefine matched = null;
            if (gate.versionDefines != null)
            {
                for (int i = 0; i < gate.versionDefines.Length; i++)
                {
                    VersionDefine candidate = gate.versionDefines[i];
                    if (candidate != null && string.Equals(candidate.name,
                            "com.fmod.fmod-for-unity.v204", StringComparison.Ordinal))
                    {
                        matched = candidate;
                        break;
                    }
                }
            }

            Assert.IsNotNull(matched,
                relativePath + " 缺少包名 com.fmod.fmod-for-unity.v204 的 versionDefines 条目");
            Assert.AreEqual("2.4.0", matched.expression,
                relativePath + " 的版本表达式必须是 2.4.0");
            Assert.IsFalse(string.IsNullOrWhiteSpace(matched.define),
                relativePath + " 的 versionDefines.define 不能为空");
            Assert.GreaterOrEqual(Array.IndexOf(gate.defineConstraints, matched.define), 0,
                relativePath + " 的 defineConstraints 必须包含 versionDefines.define（" +
                matched.define + "）");
        }

        [Test]
        public void PublicTypeScanner_DetectsAllModifierForms()
        {
            string[] positives =
            {
                "public class A {}",
                "public static class B {}",
                "public abstract class C {}",
                "public sealed class D {}",
                "public partial class E {}",
                "public readonly struct F {}",
                "public ref struct G {}",
                "public record H {}",
                "public delegate void I();",
                "  [Serializable] public sealed partial class J {}",
            };

            foreach (string positive in positives)
                Assert.IsTrue(PublicTypePattern.IsMatch(positive), "应命中：" + positive);

            string[] negatives =
            {
                "internal class A {}",
                "// public class B {}",
                "public void Method() {}",
                "public int Field;",
            };

            foreach (string negative in negatives)
                Assert.IsFalse(PublicTypePattern.IsMatch(negative), "不应命中：" + negative);
        }

        [Test]
        public void IntegrationSources_AreInternalOnly()
        {
            string root = FmodTestPaths.PackageRoot;
            if (root == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过 Integration 可见性守卫。");

            string integration = Path.Combine(root, "Runtime", "Integration");
            if (!Directory.Exists(integration))
                Assert.Ignore("找不到 Runtime/Integration；跳过 Integration 可见性守卫。");

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(
                         integration, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (PublicTypePattern.IsMatch(text))
                    violations.Add(file);
            }

            Assert.IsEmpty(violations,
                "Integration 程序集不得暴露公开类型（应全部 internal）：\n  " +
                string.Join("\n  ", violations));
        }

        [Test]
        public void Readme_DeclaresFmodPrerequisite()
        {
            string readme = FmodTestPaths.ReadText("README.md");
            if (readme == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过依赖声明守卫。");

            StringAssert.Contains("FMOD for Unity", readme);
            StringAssert.Contains("2.04", readme);
            StringAssert.Contains("Asset Store", readme);
            StringAssert.Contains("com.fmod.fmod-for-unity.v204", readme);
            StringAssert.Contains("2.4.0", readme);
        }
    }
}
