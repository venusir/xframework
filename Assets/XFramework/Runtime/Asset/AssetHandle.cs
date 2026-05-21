using System;
using YooAsset;

namespace XFramework.XAsset
{
    /// <summary>
    /// 资源句柄。直接委托给 YooAsset 的 <see cref="YooAsset.AssetHandle"/>，在 <see cref="Dispose"/> 时调用
    /// <see cref="YooAsset.AssetHandle.Release"/> 释放底层资源。
    /// <para>只读结构体，按需访问，零额外缓存。通过 <c>using</c> 语句保证资源正确释放。</para>
    /// </summary>
    /// <typeparam name="T">Unity 资源类型</typeparam>
    /// <example>
    /// <code>
    /// using (var handle = await AssetManager.LoadAsync<TextAsset>(location, ct))
    /// {
    ///     var text = handle.Asset.text;
    /// } // 自动 Release
    /// </code>
    /// </example>
    public readonly struct AssetHandle<T> : IDisposable where T : UnityEngine.Object
    {
        private readonly YooAsset.AssetHandle _yooHandle;

        /// <summary>
        /// 内部构造。由 <see cref="YooAssetManagerImpl"/> 在加载完成时创建。
        /// </summary>
        internal AssetHandle(YooAsset.AssetHandle yooHandle)
        {
            _yooHandle = yooHandle;
        }

        #region Properties — 数据

        /// <summary>
        /// 加载的资源本体。如果加载失败则为 <c>null</c>。
        /// </summary>
        public T Asset => _yooHandle?.GetAssetObject<T>();

        /// <summary>
        /// 资源定位路径（来自 YooAsset 的 <see cref="AssetInfo.AssetPath"/>）。
        /// </summary>
        public string Location => _yooHandle?.GetAssetInfo()?.AssetPath;

        #endregion

        #region Properties — 状态

        /// <summary>
        /// 句柄是否尚未释放且底层 Provider 未销毁。
        /// <para>注意：与 <c>_yooHandle != null</c> 不同，这还会检查 Provider 存活状态。</para>
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
        /// 释放资源引用。直接调用 <see cref="YooAsset.AssetHandle.Release"/>。
        /// <para>建议通过 <c>using</c> 块自动调用。</para>
        /// </summary>
        public void Dispose()
        {
            _yooHandle?.Release();
        }

        #endregion
    }
}