using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 包边界守卫：包内全部 <c>.cs</c> / <c>.md</c> 不得引用维护向 <c>Documentation/</c>
    /// （包随 UPM 发布，第三方看不到仓库根的维护文档）。
    /// </summary>
    [TestFixture]
    public sealed class LubanPackageBoundaryTests
    {
        private const string Marker = "Documentation/";
        private const string SelfFileName = "LubanPackageBoundaryTests.cs";

        [Test]
        public void PackageSources_DoNotReferenceMaintainerDocs()
        {
            string root = LubanRuntimeTestPaths.PackageRoot;
            var violations = new List<string>();

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                Check(file, violations);
            }

            foreach (string file in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories))
            {
                Check(file, violations);
            }

            Assert.That(violations, Is.Empty,
                "包内不得引用维护向 Documentation/：\n  " + string.Join("\n  ", violations));
        }

        private static void Check(string file, List<string> violations)
        {
            if (string.Equals(Path.GetFileName(file), SelfFileName, StringComparison.Ordinal))
            {
                return;
            }

            if (File.ReadAllText(file).Contains(Marker, StringComparison.Ordinal))
            {
                violations.Add(file);
            }
        }
    }
}
