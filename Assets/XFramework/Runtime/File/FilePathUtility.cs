using System;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 文件路径工具（静态纯函数）。
    /// <para>全框架相对路径统一契约：一律使用正斜杠 <c>/</c> 分隔（与 Windows / Linux / macOS 均兼容，
    /// <see cref="IFileProvider"/> 与 <see cref="FileManager"/> 按此约定接收与返回相对路径）。</para>
    /// </summary>
    public static class FilePathUtility
    {
        /// <summary>
        /// 路径分隔符（正斜杠与反斜杠），用于跨平台兼容的文件名提取。
        /// </summary>
        private static readonly char[] PathSeparators = { '/', '\\' };

        /// <summary>
        /// 归一化相对路径：反斜杠转正斜杠、去除前导斜杠。
        /// <para>空或 <c>null</c> 输入原样返回（表示域根目录）。</para>
        /// </summary>
        /// <param name="relativePath">原始相对路径。</param>
        /// <returns>归一化后的相对路径，一律以正斜杠分隔。</returns>
        public static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return relativePath;

            return relativePath.Replace('\\', '/').TrimStart('/');
        }

        /// <summary>
        /// 从（相对）路径中提取文件名部分（去掉最后一级目录前缀）。
        /// <para>兼容 <c>/</c> 与 <c>\\</c> 两种分隔符；无分隔符时原样返回。</para>
        /// </summary>
        /// <param name="path">相对路径。</param>
        /// <returns>文件名部分。</returns>
        public static string GetFileNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            var slashIndex = path.LastIndexOfAny(PathSeparators);
            if (slashIndex >= 0)
                return path.Substring(slashIndex + 1);
            return path;
        }

        /// <summary>
        /// 将绝对路径转换为相对于根目录的相对路径，统一使用正斜杠分隔。
        /// <para>字符串切片替代 <see cref="Uri.MakeRelativeUri"/>：后者会做 URI 解析与转义，产生额外分配。</para>
        /// </summary>
        /// <param name="rootDir">根目录绝对路径。</param>
        /// <param name="absolutePath">位于根目录之下的文件绝对路径。</param>
        /// <returns>相对路径（正斜杠分隔）。</returns>
        public static string ToRelativePath(string rootDir, string absolutePath)
        {
            return absolutePath.Substring(rootDir.Length).TrimStart('\\', '/').Replace('\\', '/');
        }
    }
}
