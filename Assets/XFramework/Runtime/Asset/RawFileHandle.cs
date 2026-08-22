using System;
using YooAsset;

namespace XFramework.XAsset
{
    /// <summary>
    /// 原始文件句柄。包装 YooAsset <see cref="YooAsset.RawFileHandle"/>，在 <see cref="Dispose"/> 时调用
    /// <see cref="YooAsset.RawFileHandle.Release"/> 释放底层资源。
    /// <para>用于加载 RawFile 类型的原始文件（txt、json、二进制等，不经过 Unity 资源管线）。
    /// 只读结构体，通过 <c>using</c> 语句保证资源正确释放。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// using (var handle = await AssetManager.LoadRawFileAsync("configs/server_list"))
    /// {
    ///     string text = handle.GetRawFileText();
    /// }
    /// </code>
    /// </example>
    public readonly struct RawFileHandle : IDisposable
    {
        private readonly YooAsset.RawFileHandle _yooHandle;

        /// <summary>
        /// 内部构造。由 <see cref="YooAssetManagerImpl"/> 在加载完成时创建。
        /// </summary>
        internal RawFileHandle(YooAsset.RawFileHandle yooHandle)
        {
            _yooHandle = yooHandle;
        }

        #region Methods — 数据

        /// <summary>
        /// 获取文件原始字节。加载失败时为 null。
        /// </summary>
        public byte[] GetRawFileData() => _yooHandle?.GetRawFileData();

        /// <summary>
        /// 获取文件文本内容（UTF-8 解码）。加载失败时为 null。
        /// </summary>
        public string GetRawFileText() => _yooHandle?.GetRawFileText();

        /// <summary>
        /// 获取文件的本地缓存路径。加载失败时为 null。
        /// </summary>
        public string GetRawFilePath() => _yooHandle?.GetRawFilePath();

        #endregion

        #region Properties — 状态

        /// <summary>
        /// 句柄是否尚未释放且底层 Provider 未销毁。
        /// </summary>
        public bool IsValid => _yooHandle?.IsValid ?? false;

        /// <summary>
        /// 加载是否已完成。
        /// </summary>
        public bool IsDone => _yooHandle?.IsDone ?? true;

        /// <summary>
        /// 加载进度（0~1）。
        /// </summary>
        public float Progress => _yooHandle?.Progress ?? 0f;

        /// <summary>
        /// 最近的错误信息。加载成功时为空。
        /// </summary>
        public string LastError => _yooHandle?.LastError ?? string.Empty;

        /// <summary>
        /// 加载操作状态（None / Pending / Succeed / Failed）。
        /// </summary>
        public EOperationStatus Status => _yooHandle?.Status ?? EOperationStatus.None;

        #endregion

        #region Lifecycle

        /// <summary>
        /// 释放资源引用。直接调用 <see cref="YooAsset.RawFileHandle.Release"/>。
        /// <para>建议通过 <c>using</c> 块自动调用。</para>
        /// </summary>
        public void Dispose()
        {
            _yooHandle?.Release();
        }

        #endregion
    }
}
