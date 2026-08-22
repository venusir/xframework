using System;
using System.Collections.Generic;
using YooAsset;

namespace XFramework.XAsset
{
    /// <summary>
    /// 子资源句柄。包装 YooAsset <see cref="YooAsset.SubAssetsHandle"/>，在 <see cref="Dispose"/> 时调用
    /// <see cref="YooAsset.SubAssetsHandle.Release"/> 释放底层资源。
    /// <para>用于加载图集（SpriteAtlas）、多 Sprite 贴图等含多个子资源的资源。
    /// 只读结构体，通过 <c>using</c> 语句保证资源正确释放。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// using (var handle = await AssetManager.LoadSubAssetsAsync("ui/icon_atlas"))
    /// {
    ///     var icons = handle.GetSubAssets&lt;Sprite&gt;();
    /// }
    /// </code>
    /// </example>
    public readonly struct SubAssetsHandle : IDisposable
    {
        private readonly YooAsset.SubAssetsHandle _yooHandle;

        /// <summary>
        /// 内部构造。由 <see cref="YooAssetManagerImpl"/> 在加载完成时创建。
        /// </summary>
        internal SubAssetsHandle(YooAsset.SubAssetsHandle yooHandle)
        {
            _yooHandle = yooHandle;
        }

        #region Properties — 数据

        /// <summary>
        /// 子资源数量。加载失败时为 0。
        /// </summary>
        public int Count => _yooHandle?.SubAssetObjects.Count ?? 0;

        /// <summary>
        /// 全部子资源对象。
        /// </summary>
        public IReadOnlyList<UnityEngine.Object> SubAssetObjects
            => _yooHandle?.SubAssetObjects ?? Array.Empty<UnityEngine.Object>();

        /// <summary>
        /// 按名称获取指定类型的子资源。不存在时返回 null。
        /// </summary>
        public T GetSubAsset<T>(string assetName) where T : UnityEngine.Object
            => _yooHandle == null ? null : _yooHandle.GetSubAssetObject<T>(assetName);

        /// <summary>
        /// 获取全部指定类型的子资源。
        /// </summary>
        public T[] GetSubAssets<T>() where T : UnityEngine.Object
            => _yooHandle == null ? Array.Empty<T>() : _yooHandle.GetSubAssetObjects<T>();

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
        /// 释放资源引用。直接调用 <see cref="YooAsset.SubAssetsHandle.Release"/>。
        /// <para>建议通过 <c>using</c> 块自动调用。</para>
        /// </summary>
        public void Dispose()
        {
            _yooHandle?.Release();
        }

        #endregion
    }
}
