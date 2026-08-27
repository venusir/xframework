using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 文件提供者接口。封装不同平台的文件读写实现。
    /// <para>内置实现：<see cref="DesktopFileProvider"/>（Win/Linux/mac）、<see cref="MobileFileProvider"/>（iOS/Android）。</para>
    /// <para>第三方可通过实现此接口扩展 Console（Xbox/PS5/Switch）等平台支持。</para>
    /// </summary>
    public interface IFileProvider
    {
        /// <summary>
        /// 检查指定文件是否存在。
        /// <para>注意:移动端 <see cref="FileDomain.Streaming"/> 域通过 UnityWebRequest 查询,
        /// 同步调用会阻塞主线程,推荐改用 <see cref="ExistsAsync"/>。</para>
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <returns>文件存在返回 <c>true</c>。</returns>
        bool Exists(FileDomain domain, string relativePath);

        /// <summary>
        /// 异步检查指定文件是否存在。
        /// <para>移动端 <see cref="FileDomain.Streaming"/> 域的查询为非阻塞方式,不会卡住主线程。</para>
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件存在返回 <c>true</c>。</returns>
        UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步读取文件全部文本内容。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件文本内容。文件不存在时返回 <c>null</c>。</returns>
        UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步读取文件全部字节内容。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件字节数组。文件不存在时返回 <c>null</c>。</returns>
        UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步写入文本内容到文件。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="content">要写入的文本内容。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步写入字节内容到文件。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="data">要写入的字节数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除指定文件。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        void Delete(FileDomain domain, string relativePath);

        /// <summary>
        /// 异步获取目录下所有文件路径。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的目录路径。</param>
        /// <param name="searchPattern">搜索模式，默认为 <c>*</c>（匹配所有文件）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>
        /// 匹配的文件相对路径数组。
        /// <para><b>契约：</b>返回的相对路径一律使用正斜杠 <c>/</c> 分隔（与传入的相对路径同规范），
        /// 可直接用于本接口的其他方法。</para>
        /// </returns>
        UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default);

        /// <summary>
        /// 创建目录（包括所有父目录）。
        /// <para>如果目录已存在，不执行任何操作。</para>
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的目录路径。</param>
        void CreateDirectory(FileDomain domain, string relativePath);

        /// <summary>
        /// 将路径域转换为物理绝对路径。
        /// <para>此方法供内部或需要绕过 FileManager 的底层操作使用。</para>
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的相对路径。为 <c>null</c> 时仅返回域根目录。</param>
        /// <returns>物理绝对路径。</returns>
        string GetPhysicalPath(FileDomain domain, string relativePath);
    }
}