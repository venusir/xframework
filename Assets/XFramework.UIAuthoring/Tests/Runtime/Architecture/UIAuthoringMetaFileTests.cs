using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 包内 <c>.meta</c> 完整性守卫：Unity 忽略 <c>~</c> 目录与点文件，其余资源与目录
    /// 都必须有同名 <c>.meta</c>；仓内 Assets 布局下包根目录自身也要有 <c>.meta</c>
    /// （它由 <c>Assets/</c> 的导入生成，不在包内递归射程里）。
    /// </summary>
    [TestFixture]
    public sealed class UIAuthoringMetaFileTests
    {
        [Test]
        public void EveryPackageAsset_HasMeta()
        {
            var missing = CollectMissingMeta(UIAuthoringTestPaths.PackageRoot);

            Assert.That(missing, Is.Empty,
                "包内缺少 .meta：\n  " + string.Join("\n  ", missing));
        }

        [Test]
        public void PackageRoot_HasMeta_InRepoLayout()
        {
            if (!UIAuthoringTestPaths.IsRepoLayout)
                Assert.Ignore("消费方布局（Packages/ 或 PackageCache）不要求包根目录自身有 .meta。");

            string meta = UIAuthoringTestPaths.PackageRoot + ".meta";
            Assert.That(File.Exists(meta), Is.True,
                "仓内 Assets 布局下包根目录缺少 .meta：" + meta);
        }

        [Test]
        public void CollectMissingMeta_DetectsMissingAndSkipsTilde()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "xfw-uiauthoring-meta-" + Guid.NewGuid().ToString("N"));
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
