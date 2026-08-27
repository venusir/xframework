using NUnit.Framework;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// <see cref="FilePathUtility"/> 纯函数测试。
    /// <para>覆盖:反斜杠归一、前导斜杠去除、文件名提取（兼容两种分隔符）、绝对路径相对化。
    /// 均为纯字符串操作，不依赖真实文件系统，可安全地在任意平台运行。</para>
    /// </summary>
    [TestFixture]
    public class FilePathUtilityTests
    {
        [Test]
        public void NormalizeRelativePath_BackslashBecomesForwardSlash()
        {
            Assert.AreEqual("a/b/c", FilePathUtility.NormalizeRelativePath(@"a\b\c"));
        }

        [Test]
        public void NormalizeRelativePath_LeadingSlashRemoved()
        {
            Assert.AreEqual("a/b", FilePathUtility.NormalizeRelativePath("/a/b"));
        }

        [Test]
        public void NormalizeRelativePath_AlreadyNormalized_Unchanged()
        {
            Assert.AreEqual("a/b/c", FilePathUtility.NormalizeRelativePath("a/b/c"));
        }

        [Test]
        public void NormalizeRelativePath_Empty_Unchanged()
        {
            Assert.AreEqual("", FilePathUtility.NormalizeRelativePath(""));
        }

        [Test]
        public void NormalizeRelativePath_Null_Unchanged()
        {
            Assert.IsNull(FilePathUtility.NormalizeRelativePath(null));
        }

        [Test]
        public void GetFileNameFromPath_ForwardSlash_ReturnsFileName()
        {
            Assert.AreEqual("slot_1.save", FilePathUtility.GetFileNameFromPath("player1/slot_1.save"));
        }

        [Test]
        public void GetFileNameFromPath_Backslash_ReturnsFileName()
        {
            Assert.AreEqual("slot_1.save", FilePathUtility.GetFileNameFromPath(@"player1\slot_1.save"));
        }

        [Test]
        public void GetFileNameFromPath_NoSeparator_ReturnsPathAsIs()
        {
            Assert.AreEqual("slot_1.save", FilePathUtility.GetFileNameFromPath("slot_1.save"));
        }

        [Test]
        public void GetFileNameFromPath_MultiLevel_ReturnsLastSegment()
        {
            Assert.AreEqual("c.save", FilePathUtility.GetFileNameFromPath("a/b/c.save"));
        }

        [Test]
        public void GetFileNameFromPath_Null_ReturnsNull()
        {
            Assert.IsNull(FilePathUtility.GetFileNameFromPath(null));
        }

        [Test]
        public void ToRelativePath_WindowsStylePaths_ReturnsForwardSlashRelative()
        {
            // 模拟 Windows 磁盘路径（反斜杠），契约要求输出恒为正斜杠
            const string root = "C:/AppData/Game";
            const string absolute = @"C:\AppData\Game\player1\slot_1.save";

            Assert.AreEqual("player1/slot_1.save", FilePathUtility.ToRelativePath(root, absolute));
        }

        [Test]
        public void ToRelativePath_FileDirectlyInRoot_ReturnsFileName()
        {
            const string root = "C:/AppData/Game";
            const string absolute = "C:/AppData/Game/slot_1.save";

            Assert.AreEqual("slot_1.save", FilePathUtility.ToRelativePath(root, absolute));
        }

        [Test]
        public void TryNormalizeRelativePath_ValidPath_ReturnsNormalized()
        {
            Assert.IsTrue(FilePathUtility.TryNormalizeRelativePath(@"a\b\c.txt", out var normalized));
            Assert.AreEqual("a/b/c.txt", normalized);
        }

        [Test]
        public void TryNormalizeRelativePath_LeadingSlash_Stripped()
        {
            Assert.IsTrue(FilePathUtility.TryNormalizeRelativePath("/a/b.txt", out var normalized));
            Assert.AreEqual("a/b.txt", normalized);
        }

        [Test]
        public void TryNormalizeRelativePath_Empty_ValidRoot()
        {
            Assert.IsTrue(FilePathUtility.TryNormalizeRelativePath("", out var normalized));
            Assert.IsNull(normalized, "空路径表示域根目录，归一化结果应为 null");
        }

        [Test]
        public void TryNormalizeRelativePath_Null_ValidRoot()
        {
            Assert.IsTrue(FilePathUtility.TryNormalizeRelativePath(null, out var normalized));
            Assert.IsNull(normalized);
        }

        [Test]
        public void TryNormalizeRelativePath_DotDot_Rejected()
        {
            Assert.IsFalse(FilePathUtility.TryNormalizeRelativePath("../a.txt", out _), "前导 .. 应拒绝");
            Assert.IsFalse(FilePathUtility.TryNormalizeRelativePath("a/../../b.txt", out _), "中间 .. 应拒绝");
            Assert.IsFalse(FilePathUtility.TryNormalizeRelativePath("..", out _), "整体 .. 应拒绝");
            Assert.IsFalse(FilePathUtility.TryNormalizeRelativePath(@"..\..\outside.txt", out _), "反斜杠 .. 应拒绝");
        }

        [Test]
        public void TryNormalizeRelativePath_DriveLetter_Rejected()
        {
            Assert.IsFalse(FilePathUtility.TryNormalizeRelativePath(@"C:\x.txt", out _), "反斜杠盘符应拒绝");
            Assert.IsFalse(FilePathUtility.TryNormalizeRelativePath("C:/x.txt", out _), "正斜杠盘符应拒绝");
        }

        [Test]
        public void TryNormalizeRelativePath_UncPrefix_Rejected()
        {
            Assert.IsFalse(FilePathUtility.TryNormalizeRelativePath(@"\\server\share\x.txt", out _), "UNC 前导应拒绝");
        }

        [Test]
        public void TryNormalizeRelativePath_DotDotInsideFileName_Allowed()
        {
            // ".." 出现在文件名内部（非独立段）时合法，如版本号 "v1..2"
            Assert.IsTrue(FilePathUtility.TryNormalizeRelativePath("a..txt", out var normalized));
            Assert.AreEqual("a..txt", normalized);
        }
    }
}
