using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 目录枚举能力契约（可选）。
    /// <para>实现 <see cref="IFileProvider"/> 的平台可额外实现本接口以获得子目录枚举能力，
    /// 与 <see cref="IAtomicFileProvider"/> 同构：均为「按需探测、缺失即降级并告警」的可选能力，
    /// 从而不必给 <see cref="IFileProvider"/> 增加成员而破坏既有第三方实现。</para>
    /// <para>未实现本接口的平台（如 Console）调用 <see cref="FileManager.GetDirectoriesAsync"/>
    /// 时返回空数组并告警。</para>
    /// </summary>
    public interface IDirectoryProvider
    {
        /// <summary>
        /// 异步获取目录下的直接子目录（非递归）。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的目录路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>
        /// 子目录的相对路径数组；目录不存在时返回空数组。
        /// <para><b>契约：</b>返回的相对路径一律使用正斜杠 <c>/</c> 分隔（与 <see cref="IFileProvider.GetFilesAsync"/>
        /// 同规范），可直接回传 <see cref="FileManager"/> 的其他方法。</para>
        /// </returns>
        UniTask<string[]> GetDirectoriesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default);
    }
}
