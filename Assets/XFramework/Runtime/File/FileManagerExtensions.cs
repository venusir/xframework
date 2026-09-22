using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XFileManager
{
    /// <summary>
    /// <see cref="FileManager"/> 的同步便捷 API 与工具方法。
    /// <para><b>同步方法已收敛到门面本体</b>（<see cref="FileManager.ReadAllText"/> /
    /// <see cref="FileManager.WriteAllBytes"/> 等），此处保留同名成员只为兼容既有调用方——
    /// 二者行为完全一致，新代码请直接用门面上的那一份。</para>
    /// <para>同步方法内部调用异步实现然后阻塞等待，仅适合编辑器工具、小型配置文件等场景。</para>
    /// <para>运行时强烈建议使用异步版本以避免主线程卡顿。</para>
    /// </summary>
    public static class FileManagerExtensions
    {
        #region Synchronous Text

        /// <summary>
        /// 同步读取文件全部文本内容（直接委托给 <see cref="FileManager.ReadAllText"/>）。
        /// </summary>
        public static string ReadAllText(FileDomain domain, string relativePath)
        {
            return FileManager.ReadAllText(domain, relativePath);
        }

        /// <summary>
        /// 同步写入文本内容到文件（直接委托给 <see cref="FileManager.WriteAllText"/>）。
        /// </summary>
        public static void WriteAllText(FileDomain domain, string relativePath, string content)
        {
            FileManager.WriteAllText(domain, relativePath, content);
        }

        #endregion

        #region Synchronous Bytes

        /// <summary>
        /// 同步读取文件全部字节内容（直接委托给 <see cref="FileManager.ReadAllBytes"/>）。
        /// </summary>
        public static byte[] ReadAllBytes(FileDomain domain, string relativePath)
        {
            return FileManager.ReadAllBytes(domain, relativePath);
        }

        /// <summary>
        /// 同步写入字节内容到文件（直接委托给 <see cref="FileManager.WriteAllBytes"/>）。
        /// </summary>
        public static void WriteAllBytes(FileDomain domain, string relativePath, byte[] data)
        {
            FileManager.WriteAllBytes(domain, relativePath, data);
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
        /// 同步获取目录下所有文件路径（直接委托给 <see cref="FileManager.GetFiles"/>）。
        /// </summary>
        public static string[] GetFiles(FileDomain domain, string relativePath, string searchPattern = "*")
        {
            return FileManager.GetFiles(domain, relativePath, searchPattern);
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