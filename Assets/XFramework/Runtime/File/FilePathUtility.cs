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
        /// 安全归一化相对路径：拒绝路径穿越与绝对路径形态。
        /// <para>校验规则：任何 <c>..</c> 段（<c>../x</c>、<c>a/../b</c>、整体 <c>..</c>）、
        /// 盘符前缀（<c>C:\x</c>）、UNC 前导（<c>\\server\share</c>）均为非法；
        /// 空或 <c>null</c> 合法（表示域根目录）。</para>
        /// </summary>
        /// <param name="relativePath">原始相对路径。</param>
        /// <param name="normalized">归一化结果（正斜杠分隔、无前导斜杠）；非法输入时为 <c>null</c>。</param>
        /// <returns>路径合法返回 <c>true</c>，否则 <c>false</c>。</returns>
        public static bool TryNormalizeRelativePath(string relativePath, out string normalized)
        {
            normalized = null;

            if (string.IsNullOrEmpty(relativePath))
                return true;

            // 拒绝盘符前缀（C:\、C:/）
            if (relativePath.Length >= 2 && char.IsLetter(relativePath[0]) && relativePath[1] == ':')
                return false;

            var norm = relativePath.Replace('\\', '/');

            // 拒绝 UNC 前导（\\server\share）
            if (norm.StartsWith("//", StringComparison.Ordinal))
                return false;

            // 拒绝任何 .. 段（../、a/../b、整体 ..）。零分配扫描：
            // 仅当 .. 两侧为字符串边界或 '/' 时才算独立段，排除 a..txt 这类合法文件名
            int idx = norm.IndexOf("..", StringComparison.Ordinal);
            while (idx >= 0)
            {
                bool atStart = idx == 0 || norm[idx - 1] == '/';
                bool atEnd = idx + 2 >= norm.Length || norm[idx + 2] == '/';
                if (atStart && atEnd)
                    return false;

                idx = norm.IndexOf("..", idx + 1, StringComparison.Ordinal);
            }

            normalized = norm.TrimStart('/');
            return true;
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
