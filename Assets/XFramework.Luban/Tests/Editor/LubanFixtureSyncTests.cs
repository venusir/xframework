using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// 守卫两份 Bin 生成夹具逐字一致：<c>Tests/Editor/Fixtures~/Bin</c> 与
    /// <c>Tests/Runtime/Fixtures/Luban</c> 收同一批文件，但没有共同来源。
    /// </summary>
    [TestFixture]
    public sealed class LubanFixtureSyncTests
    {
        [Test]
        public void BinFixtures_MatchRuntimeFixtures()
        {
            string editorRoot = Path.Combine(LubanTestPaths.FixturesRoot, "Bin");
            string runtimeRoot = Path.Combine(
                LubanTestPaths.PackageRoot, "Tests", "Runtime", "Fixtures", "Luban");

            Dictionary<string, byte[]> editor = Collect(editorRoot);
            Dictionary<string, byte[]> runtime = Collect(runtimeRoot);

            Assert.That(editor.Count, Is.GreaterThan(0));
            Assert.That(editor.Keys, Is.EquivalentTo(runtime.Keys));

            foreach (var pair in editor)
            {
                Assert.That(runtime[pair.Key], Is.EqualTo(pair.Value), pair.Key);
            }
        }

        private static Dictionary<string, byte[]> Collect(string root)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            string[] paths = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            for (int i = 0; i < paths.Length; i++)
            {
                string relative = paths[i]
                    .Substring(root.Length + 1)
                    .Replace('\\', '/');
                files[relative] = File.ReadAllBytes(paths[i]);
            }

            return files;
        }
    }
}
