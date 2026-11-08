using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 依赖声明守卫：本包 <c>package.json</c> 的 <c>dependencies</c> 必须为空
    /// （UPM 不支持 Git URL 传递依赖）；Runtime asmdef 只允许引用
    /// 核心包、UniTask 与 TextMeshPro（UGUI 是预编译程序集，不写进 references）。
    /// </summary>
    [TestFixture]
    public sealed class UIAuthoringDependencyDeclarationTests
    {
        private static readonly string[] AllowedReferences =
        {
            "Venusy609.Xframework",
            "UniTask",
            "Unity.TextMeshPro",
        };

        [Test]
        public void PackageJson_HasNoDependencies()
        {
            string json = File.ReadAllText(
                Path.Combine(UIAuthoringTestPaths.PackageRoot, "package.json"));

            Assert.That(Regex.IsMatch(json, "\"dependencies\"\\s*:\\s*\\{\\s*\\}"), Is.True,
                "package.json 的 dependencies 必须为空（UPM 不支持 Git URL 传递依赖）。");
        }

        [Test]
        public void RuntimeAsmdef_ReferencesOnlyAllowedAssemblies()
        {
            string path = Path.Combine(
                UIAuthoringTestPaths.PackageRoot, "Runtime",
                "Venusy609.Xframework.UI.Authoring.asmdef");
            string json = File.ReadAllText(path);

            Match block = Regex.Match(json, "\"references\"\\s*:\\s*\\[(?<refs>[^\\]]*)\\]");
            Assert.That(block.Success, Is.True, "Runtime asmdef 缺少 references：");

            MatchCollection names = Regex.Matches(block.Groups["refs"].Value, "\"([^\"]+)\"");
            var references = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
                references[i] = names[i].Groups[1].Value;

            Assert.That(references, Is.EquivalentTo(AllowedReferences),
                "Runtime asmdef 只允许引用核心包 + UniTask + TextMeshPro；新增依赖需同时更新包 README 的安装说明。");
        }
    }
}
