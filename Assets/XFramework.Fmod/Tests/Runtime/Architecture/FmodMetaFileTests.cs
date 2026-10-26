using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 包内每个资源/目录都必须有同名 <c>.meta</c>；<c>~</c> 与点开头的条目由 Unity 忽略，跳过。
    /// </summary>
    [TestFixture]
    public class FmodMetaFileTests
    {
        [Test]
        public void EveryPackageAsset_HasMeta()
        {
            string root = FmodTestPaths.PackageRoot;
            if (root == null)
                Assert.Ignore("找不到 XFramework.Fmod 包根；跳过 .meta 守卫。");

            var missing = new List<string>();
            CheckEntry(root, missing);

            foreach (string entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            {
                if (ShouldSkip(entry))
                    continue;

                CheckEntry(entry, missing);
            }

            Assert.IsEmpty(missing,
                "以下包内条目缺少同名 .meta：\n  " + string.Join("\n  ", missing));
        }

        private static void CheckEntry(string path, List<string> missing)
        {
            if (!File.Exists(path + ".meta") && !Directory.Exists(path + ".meta"))
                missing.Add(path);
        }

        private static bool ShouldSkip(string path)
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(".meta", System.StringComparison.Ordinal))
                return true;
            if (name.StartsWith("~", System.StringComparison.Ordinal) ||
                name.StartsWith(".", System.StringComparison.Ordinal))
                return true;

            string directory = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(directory))
            {
                string dirName = Path.GetFileName(directory);
                if (dirName.StartsWith("~", System.StringComparison.Ordinal) ||
                    dirName.StartsWith(".", System.StringComparison.Ordinal))
                {
                    return true;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return false;
        }
    }
}
