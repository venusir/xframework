using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 原子写入能力契约（可选）。
    /// <para>实现 <see cref="IFileProvider"/> 的平台可额外实现本接口以获得原子写入能力：
    /// 先写临时文件、写入成功后再替换正式文件，写入中途崩溃不会损坏已有文件。</para>
    /// <para>不支持原子写的平台（如 WebGL）可仅实现 <see cref="IFileProvider"/>；
    /// <see cref="FileManager.WriteAllBytesAtomicAsync"/> 会在能力缺失时降级为普通写入并告警。</para>
    /// </summary>
    public interface IAtomicFileProvider
    {
        /// <summary>
        /// 原子写入字节内容到文件：先写 <c>.tmp</c> 临时文件，写入成功后再替换正式文件。
        /// <para>写入或替换失败时，原有正式文件保持完整。</para>
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="data">要写入的字节数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask WriteAllBytesAtomicAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default);
    }
}
