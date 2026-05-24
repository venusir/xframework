using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XFileManager
{
    /// <summary>
    /// <see cref="FileManager"/> 的扩展方法，提供同步便捷 API 和工具方法。
    /// <para>同步方法内部调用异步实现然后阻塞等待，仅适合编辑器工具、小型配置文件等场景。</para>
    /// <para>运行时强烈建议使用异步版本以避免主线程卡顿。</para>
    /// </summary>
    public static class FileManagerExtensions
    {
        #region Synchronous Text

        /// <summary>
        /// 同步读取文件全部文本内容。
        /// <para>内部调用 <see cref="FileManager.ReadAllTextAsync"/> 并阻塞等待。</para>
        /// </summary>
        public static string ReadAllText(FileDomain domain, string relativePath)
        {
            return FileManager.ReadAllTextAsync(domain, relativePath)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// 同步写入文本内容到文件。
        /// </summary>
        public static void WriteAllText(FileDomain domain, string relativePath, string content)
        {
            FileManager.WriteAllTextAsync(domain, relativePath, content)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        #endregion

        #region Synchronous Bytes

        /// <summary>
        /// 同步读取文件全部字节内容。
        /// </summary>
        public static byte[] ReadAllBytes(FileDomain domain, string relativePath)
        {
            return FileManager.ReadAllBytesAsync(domain, relativePath)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// 同步写入字节内容到文件。
        /// </summary>
        public static void WriteAllBytes(FileDomain domain, string relativePath, byte[] data)
        {
            FileManager.WriteAllBytesAsync(domain, relativePath, data)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        #endregion

        #region Synchronous — Exists / Delete / Directory

        /// <summary>
        /// 同步检查文件是否存在（直接委托给 <see cref="FileManager.Exists"/>）。
        /// </summary>
        public static bool Exists(FileDomain domain, string relativePath)
        {
            return FileManager.Exists(domain, relativePath);
        }

        /// <summary>
        /// 同步删除文件（直接委托给 <see cref="FileManager.Delete"/>）。
        /// </summary>
        public static void Delete(FileDomain domain, string relativePath)
        {
            FileManager.Delete(domain, relativePath);
        }

        /// <summary>
        /// 同步获取目录下所有文件路径。
        /// </summary>
        public static string[] GetFiles(FileDomain domain, string relativePath, string searchPattern = "*")
        {
            return FileManager.GetFilesAsync(domain, relativePath, searchPattern)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// 同步创建目录（直接委托给 <see cref="FileManager.CreateDirectory"/>）。
        /// </summary>
        public static void CreateDirectory(FileDomain domain, string relativePath)
        {
            FileManager.CreateDirectory(domain, relativePath);
        }

        #endregion

        #region Utility Extensions

        /// <summary>
        /// 使用指定的 <see cref="CancellationToken"/> 调用 <see cref="FileManager.ReadAllTextAsync"/>。
        /// <para>便捷扩展，避免调用方需要自己管理 CancellationToken 参数。</para>
        /// </summary>
        public static UniTask<string> ReadAllTextWithTokenAsync(this FileDomain domain, string relativePath, CancellationToken cancellationToken)
        {
            return FileManager.ReadAllTextAsync(domain, relativePath, cancellationToken);
        }

        /// <summary>
        /// 使用指定的 <see cref="CancellationToken"/> 调用 <see cref="FileManager.WriteAllTextAsync"/>。
        /// </summary>
        public static UniTask WriteAllTextWithTokenAsync(this FileDomain domain, string relativePath, string content, CancellationToken cancellationToken)
        {
            return FileManager.WriteAllTextAsync(domain, relativePath, content, cancellationToken);
        }

        #endregion
    }
}