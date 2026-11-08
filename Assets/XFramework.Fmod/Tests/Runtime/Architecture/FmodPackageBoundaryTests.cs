using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 包内全部文本不得出现包外维护文档路径（<c>Documentation/</c>）；包必须自足。
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
                if (IsSelf(file))
                    continue;

                if (!TryReadText(file, out string text))
                    continue;

                if (text.Contains(Forbidden))
                    violations.Add(file);
            }

            Assert.IsEmpty(violations,
                "以下包内文件引用了包外 Documentation/ 路径：\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TextScan_IncludesAsmdefJsonAndSkipsBinary()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "xfw-fmod-boundary-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string asmdef = Path.Combine(dir, "a.asmdef");
                File.WriteAllText(asmdef, "{\"name\":\"Documentation/x\"}");

                string json = Path.Combine(dir, "b.json");
                File.WriteAllText(json, "Documentation/");

                string binary = Path.Combine(dir, "c.bin");
                File.WriteAllBytes(binary, new byte[] { 0x00, 0x44, 0x6F, 0x63 });

                Assert.IsTrue(TryReadText(asmdef, out string asmdefText));
                Assert.AreEqual("{\"name\":\"Documentation/x\"}", asmdefText);
                Assert.IsTrue(TryReadText(json, out _));
                Assert.IsFalse(TryReadText(binary, out _));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static bool IsSelf(string file)
        {
            return string.Equals(Path.GetFileName(file),
                nameof(FmodPackageBoundaryTests) + ".cs", StringComparison.Ordinal);
        }

        /// <summary>读取文本；前 512 字节含 NUL 视为二进制并跳过。</summary>
        private static bool TryReadText(string file, out string text)
        {
            text = null;

            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                int probe = bytes.Length < 512 ? bytes.Length : 512;
                for (int i = 0; i < probe; i++)
                {
                    if (bytes[i] == 0)
                        return false;
                }

                text = System.Text.Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
