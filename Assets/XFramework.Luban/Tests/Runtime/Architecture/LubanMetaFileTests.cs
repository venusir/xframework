using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 包内 <c>.meta</c> 完整性守卫：Unity 忽略 <c>~</c> 目录与点文件，其余资源与目录
    /// 都必须有同名 <c>.meta</c>。
    /// </summary>
    [TestFixture]
    public sealed class LubanMetaFileTests
    {
        [Test]
        public void EveryPackageAsset_HasMeta()
        {
            var missing = CollectMissingMeta(LubanRuntimeTestPaths.PackageRoot);

            Assert.That(missing, Is.Empty,
                "包内缺少 .meta：\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void CollectMissingMeta_DetectsMissingAndSkipsTilde()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "xfw-luban-meta-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string file = Path.Combine(root, "A.txt");
                File.WriteAllText(file, "a");
                string ignored = Path.Combine(root, "Ignored~");
                Directory.CreateDirectory(ignored);
                File.WriteAllText(Path.Combine(ignored, "B.txt"), "b");

                var missing = CollectMissingMeta(root);
                Assert.That(missing.Count, Is.EqualTo(1));
                Assert.That(missing[0], Does.EndWith("A.txt"));

                File.WriteAllText(file + ".meta", "meta");
                Assert.That(CollectMissingMeta(root), Is.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        internal static List<string> CollectMissingMeta(string root)
        {
            var missing = new List<string>();
            Walk(root, missing);
            return missing;
        }

        private static void Walk(string directory, List<string> missing)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                string name = Path.GetFileName(entry);
                if (name.EndsWith("~", StringComparison.Ordinal) ||
                    name.EndsWith(".meta", StringComparison.Ordinal) ||
                    name.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!File.Exists(entry + ".meta"))
                {
                    missing.Add(entry);
                }

                if (Directory.Exists(entry))
                {
                    Walk(entry, missing);
                }
            }
        }
    }
}
