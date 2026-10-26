using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 依赖声明守卫：FMOD 不写进 package.json；Runtime 不引用 FMODUnity；README 声明消费方前置。
    /// </summary>
    [TestFixture]
    public class FmodDependencyDeclarationTests
    {
        private const string ForbiddenTypePrefix = "FMODUnity";

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
            foreach (string file in Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", System.StringComparison.Ordinal) &&
                    !file.EndsWith(".asmdef", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains(ForbiddenTypePrefix))
                    violations.Add(file);
            }

            Assert.IsEmpty(violations,
                "FMOD-free 层不得引用 FMODUnity：\n  " + string.Join("\n  ", violations));
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
        }
    }
}
