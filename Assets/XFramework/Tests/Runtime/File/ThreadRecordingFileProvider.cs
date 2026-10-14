using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XFileManager;
using XFramework.XSave.Tests;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// 记录「<see cref="IFileProvider.Delete"/> 在哪个线程被调用」的包装替身，其余请求原样转发给内层
    /// <see cref="TempFileProvider"/>。
    /// <para><b>为什么必须在被观测的那一层记录</b>：测试方法自身是 <c>async Task</c>，其续体会被 Unity 的
    /// SynchronizationContext 弹回主线程——在测试体里断言测不到真实情况（同一纪律的既有先例：
    /// <c>SaveManagerImplTests</c> 的线程断言骨架）。</para>
    /// <para><b>三个接口全部实现是刻意的</b>：少实现 <see cref="IAtomicFileProvider"/> 或
    /// <see cref="IDirectoryProvider"/> 会让门面把它当成「能力缺失」而降级并告警，污染日志断言。</para>
    /// </summary>
    internal sealed class ThreadRecordingFileProvider : IFileProvider, IAtomicFileProvider, IDirectoryProvider
    {
        #region Private Fields

        private static readonly List<int> RecordedThreadIds = new();
        private readonly TempFileProvider _inner;

        #endregion

        #region Construction

        public ThreadRecordingFileProvider(TempFileProvider inner)
        {
            _inner = inner;
        }

        #endregion

        #region Static Recording

        /// <summary>已发生的删除次数（供「一个都不在主线程」这类断言连同前提一起用）。</summary>
        public static int DeleteCount => RecordedThreadIds.Count;

        /// <summary>最近一次删除被调用时的托管线程号；无记录时为 <c>-1</c>。</summary>
        public static int LastDeleteThreadId =>
            RecordedThreadIds.Count > 0 ? RecordedThreadIds[RecordedThreadIds.Count - 1] : -1;

        /// <summary>清空记录（每个用例开始前调用）。</summary>
        public static void Reset()
        {
            RecordedThreadIds.Clear();
        }

        /// <summary>本次记录里是否有任何一次删除发生在 <paramref name="threadId"/> 上。</summary>
        /// <param name="threadId">要核对的托管线程号。</param>
        public static bool AnyDeleteRanOn(int threadId)
        {
            for (int i = 0; i < RecordedThreadIds.Count; i++)
            {
                if (RecordedThreadIds[i] == threadId)
                    return true;
            }
            return false;
        }

        #endregion

        #region IFileProvider

        /// <inheritdoc/>
        public bool Exists(FileDomain domain, string relativePath) => _inner.Exists(domain, relativePath);

        /// <inheritdoc/>
        public UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
            => _inner.ExistsAsync(domain, relativePath, cancellationToken);

        /// <inheritdoc/>
        public UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
            => _inner.ReadAllTextAsync(domain, relativePath, cancellationToken);

        /// <inheritdoc/>
        public UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
            => _inner.ReadAllBytesAsync(domain, relativePath, cancellationToken);

        /// <inheritdoc/>
        public UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default)
            => _inner.WriteAllTextAsync(domain, relativePath, content, cancellationToken);

        /// <inheritdoc/>
        public UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
            => _inner.WriteAllBytesAsync(domain, relativePath, data, cancellationToken);

        /// <inheritdoc/>
        public void Delete(FileDomain domain, string relativePath)
        {
            RecordedThreadIds.Add(System.Environment.CurrentManagedThreadId);
            _inner.Delete(domain, relativePath);
        }

        /// <inheritdoc/>
        public UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default)
            => _inner.GetFilesAsync(domain, relativePath, searchPattern, cancellationToken);

        /// <inheritdoc/>
        public void CreateDirectory(FileDomain domain, string relativePath) => _inner.CreateDirectory(domain, relativePath);

        /// <inheritdoc/>
        public string GetPhysicalPath(FileDomain domain, string relativePath) => _inner.GetPhysicalPath(domain, relativePath);

        #endregion

        #region 可选能力接口（全实现，避免门面降级告警）

        /// <inheritdoc/>
        public UniTask WriteAllBytesAtomicAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
            => _inner.WriteAllBytesAtomicAsync(domain, relativePath, data, cancellationToken);

        /// <inheritdoc/>
        public UniTask<string[]> GetDirectoriesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
            => _inner.GetDirectoriesAsync(domain, relativePath, cancellationToken);

        #endregion
    }
}
