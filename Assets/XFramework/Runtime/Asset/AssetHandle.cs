using System;
using UnityEngine;

namespace XFramework.XAsset
{
    /// <summary>
    /// 资源句柄。包装 <see cref="IAssetManager.LoadAsync{T}"/> 返回的资源，
    /// 在 <see cref="Dispose"/> 时自动调用 <see cref="IAssetManager.Release"/>。
    /// <para>只读结构体，零 GC 分配。通过 <c>using</c> 语句保证资源正确释放，避免手动 try-finally。</para>
    /// </summary>
    /// <typeparam name="T">Unity 资源类型</typeparam>
    /// <example>
    /// <code>
    /// using (var handle = await AssetManager.LoadAsync<TextAsset>(location, ct))
    /// {
    ///     var text = handle.Asset.text;
    ///     // ... 使用 text ...
    /// } // 自动 Release
    /// </code>
    /// </example>
    public readonly struct AssetHandle<T> : IDisposable where T : UnityEngine.Object
    {
        /// <summary>
        /// 加载的资源本体。如果加载失败则为 <c>null</c>。
        /// </summary>
        public T Asset { get; }

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public string Location { get; }

        /// <summary>
        /// 句柄是否有效（是否已 Dispose 或来自未初始化的构造）。
        /// </summary>
        public bool IsValid => _manager != null;

        private readonly IAssetManager _manager;

        /// <summary>
        /// 内部构造。由 <see cref="AssetManagerImpl"/> 在加载时创建。
        /// </summary>
        internal AssetHandle(T asset, string location, IAssetManager manager)
        {
            Asset = asset;
            Location = location;
            _manager = manager;
        }

        /// <summary>
        /// 释放资源引用。调用 <see cref="IAssetManager.Release(string)"/> 将资源引用计数 -1。
        /// <para>建议通过 <c>using</c> 块自动调用，无需手动调用。</para>
        /// </summary>
        public void Dispose()
        {
            if (_manager != null && Location != null)
                _manager.Release(Location);
        }
    }
}