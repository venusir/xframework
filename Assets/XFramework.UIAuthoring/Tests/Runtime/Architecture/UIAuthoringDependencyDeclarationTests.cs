using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 依赖声明守卫：本包 <c>package.json</c> 的 <c>dependencies</c> 必须为空
    /// （UPM 不支持 Git URL 传递依赖）；Runtime asmdef 只允许引用
    /// 核心包、UniTask 与 TextMeshPro（UGUI 是预编译程序集，不写进 references）；
    /// Editor asmdef 允许的额外引用（TMP / YooAsset）同样逐字锁定。
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

        private static readonly string[] AllowedEditorReferences =
        {
            "Venusy609.Xframework",
            "Venusy609.Xframework.UI.Authoring",
            "Unity.TextMeshPro",
            "YooAsset",
            "YooAsset.Editor",
        };

        private static string[] ReadReferences(string path)
        {
            string json = File.ReadAllText(path);

            Match block = Regex.Match(json, "\"references\"\\s*:\\s*\\[(?<refs>[^\\]]*)\\]");
            Assert.That(block.Success, Is.True, $"{path} 缺少 references：");

            MatchCollection names = Regex.Matches(block.Groups["refs"].Value, "\"([^\"]+)\"");
            var references = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
                references[i] = names[i].Groups[1].Value;
            return references;
        }

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

            Assert.That(ReadReferences(path), Is.EquivalentTo(AllowedReferences),
                "Runtime asmdef 只允许引用核心包 + UniTask + TextMeshPro；新增依赖需同时更新包 README 的安装说明。");
        }

        [Test]
        public void EditorAsmdef_ReferencesOnlyDeclaredAssemblies()
        {
            string path = Path.Combine(
                UIAuthoringTestPaths.PackageRoot, "Editor",
                "Venusy609.Xframework.UI.Authoring.Editor.asmdef");

            Assert.That(ReadReferences(path), Is.EquivalentTo(AllowedEditorReferences),
                "Editor asmdef 的引用集合变了：新增依赖需同步包 README 的依赖说明与核心依赖声明守卫的射程。");
        }
    }
}
