using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace XFramework.XAsset
{
    /// <summary>
    /// 资源下载器句柄。包装 YooAsset <see cref="DownloaderOperation"/>，
    /// 提供轮询属性、事件回调与暂停/恢复/取消控制。
    /// <para>由 <see cref="AssetManager.CreateDownloader"/> 创建，创建后需调用 <see cref="Begin"/> 启动下载。
    /// 也支持 <see cref="AssetManager.DownloadAssetsAsync"/> 一键下载（内部自动创建并启动）。</para>
    /// <para>注意：<see cref="Dispose"/> 仅解除事件订阅，不中止下载（下载器由 YooAsset 底层管理）。</para>
    /// </summary>
    public sealed class AssetDownloaderHandle : IDisposable
    {
        private readonly DownloaderOperation _operation;
        private bool _disposed;

        internal AssetDownloaderHandle(DownloaderOperation operation)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            _operation.DownloadUpdateCallback += OnDownloadUpdate;
            _operation.DownloadFinishCallback += OnDownloadFinish;
            _operation.DownloadErrorCallback += OnDownloadError;
        }

        #region Data

        /// <summary>总下载文件数。</summary>
        public int TotalDownloadCount => _operation.TotalDownloadCount;

        /// <summary>总下载字节数。</summary>
        public long TotalDownloadBytes => _operation.TotalDownloadBytes;

        /// <summary>已下载文件数。</summary>
        public int CurrentDownloadCount => _operation.CurrentDownloadCount;

        /// <summary>已下载字节数。</summary>
        public long CurrentDownloadBytes => _operation.CurrentDownloadBytes;

        /// <summary>
        /// 下载进度 0~1。无待下载文件（TotalDownloadBytes == 0）时返回 1f，避免除零产生 NaN。
        /// </summary>
        public float Progress => TotalDownloadBytes == 0 ? 1f : (float)CurrentDownloadBytes / TotalDownloadBytes;

        /// <summary>下载是否结束（成功或失败）。</summary>
        public bool IsDone => _operation.IsDone;

        /// <summary>下载状态。</summary>
        public EOperationStatus Status => _operation.Status;

        /// <summary>失败信息。未失败时为空字符串。</summary>
        public string Error => _operation.Error;

        #endregion

        #region Events

        /// <summary>进度变化事件（0~1，仅在进度发生变化时触发）。</summary>
        public event Action<float> ProgressChanged;

        /// <summary>下载结束事件。成功时 succeed 为 true。</summary>
        public event Action<bool> Completed;

        /// <summary>下载失败事件。fileName 为失败的文件，errorInfo 为底层错误信息。</summary>
        public event Action<string, string> DownloadError;

        #endregion

        #region Control

        /// <summary>开始下载。</summary>
        public void Begin() => _operation.BeginDownload();

        /// <summary>暂停下载（正在传输的文件完成后暂停创建新下载任务）。</summary>
        public void Pause() => _operation.PauseDownload();

        /// <summary>恢复下载。</summary>
        public void Resume() => _operation.ResumeDownload();

        /// <summary>取消下载。已下载的缓存会保留，下次下载自动断点续传。</summary>
        public void Cancel() => _operation.CancelDownload();

        #endregion

        #region Await

        /// <summary>
        /// 等待下载结束，返回是否全部成功。
        /// <para>取消时自动 <see cref="Cancel"/> 中止下载并抛出 <see cref="OperationCanceledException"/>。</para>
        /// </summary>
        public async UniTask<bool> WaitAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                while (!_operation.IsDone)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Cancel();
                throw;
            }
            return _operation.Status == EOperationStatus.Succeed;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 解除事件订阅。不中止下载——如需中止调用 <see cref="Cancel"/>。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _operation.DownloadUpdateCallback -= OnDownloadUpdate;
            _operation.DownloadFinishCallback -= OnDownloadFinish;
            _operation.DownloadErrorCallback -= OnDownloadError;
        }

        #endregion

        #region Internal

        private void OnDownloadUpdate(DownloadUpdateData data) => ProgressChanged?.Invoke(data.Progress);

        private void OnDownloadFinish(DownloaderFinishData data) => Completed?.Invoke(data.Succeed);

        private void OnDownloadError(DownloadErrorData data) => DownloadError?.Invoke(data.FileName, data.ErrorInfo);

        #endregion
    }
}
