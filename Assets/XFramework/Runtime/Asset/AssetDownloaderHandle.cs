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

        /// <summary>是否已 <see cref="Begin"/>。用于把「未启动就等待」这条静默挂起挡在入口（见 <see cref="WaitAsync"/>）。</summary>
        private bool _begun;

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

        /// <summary>失败信息。未失败时为空字符串。
        /// <para>归一化是必要的：YooAsset 的 <c>Error</c> 在成功路径从不赋值，原样透出的是 <c>null</c>
        /// 而非空串（其余两个句柄的 <c>LastError</c> 同样做了归一）。</para></summary>
        public string Error => _operation.Error ?? string.Empty;

        #endregion

        #region Events

        /// <summary>进度变化事件（0~1，仅在进度发生变化时触发）。</summary>
        public event Action<float> ProgressChanged;

        /// <summary>下载结束事件。成功时 succeed 为 true。
        /// <para>注意：主动 <see cref="Cancel"/> <b>不会</b>触发本事件——YooAsset 的取消路径只置状态、不回调结束。
        /// 以本事件驱动流程的调用方需自行处理取消。</para></summary>
        public event Action<bool> Completed;

        /// <summary>下载失败事件。fileName 为失败的文件，errorInfo 为底层错误信息。</summary>
        public event Action<string, string> DownloadError;

        #endregion

        #region Control

        /// <summary>开始下载。重复调用由 YooAsset 侧忽略。</summary>
        public void Begin()
        {
            _operation.BeginDownload();
            _begun = true;
        }

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
        /// <para><b>未 <see cref="Begin"/> 时拒绝</b>而不是静默挂起：YooAsset 的下载器在启动前既不完成也不失败，
        /// 而本方法没有任何超时——忘了 <c>Begin()</c> 的调用方会永久卡在 await 上、无异常无日志。
        /// 拒绝比挂起好排查（同 File 模块对同步等待的态度）。已经结束（如先 <see cref="Cancel"/>）的不拒绝，
        /// 照常返回状态。</para>
        /// </summary>
        public async UniTask<bool> WaitAsync(CancellationToken cancellationToken = default)
        {
            if (!_begun && !_operation.IsDone)
                throw new InvalidOperationException(
                    "[AssetManager] AssetDownloaderHandle.WaitAsync 在 Begin() 之前不会完成：请先调用 Begin() 启动下载。");

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
