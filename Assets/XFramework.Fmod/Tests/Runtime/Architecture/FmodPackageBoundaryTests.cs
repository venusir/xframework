using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 包内 <c>.cs</c>/<c>.md</c> 不得出现包外维护文档路径（<c>Documentation/</c>）；包必须自足。
    /// </summary>
    [TestFixture]
    public class FmodPackageBoundaryTests
    {
        private const string Forbidden = "Documentation" + "/";

        [Test]
        public void PackageText_DoesNotReferenceOuterDocumentation()
        {
            string root = FmodTestPaths.PackageRoot;
            if (root == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过包边界守卫。");

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", System.StringComparison.Ordinal) &&
                    !file.EndsWith(".md", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (Path.GetFileName(file) == nameof(FmodPackageBoundaryTests) + ".cs")
                    continue;

                if (File.ReadAllText(file).Contains(Forbidden))
                    violations.Add(file);
            }

            Assert.IsEmpty(violations,
                "以下包内文件引用了包外 Documentation/ 路径：\n  " + string.Join("\n  ", violations));
        }
    }
}
