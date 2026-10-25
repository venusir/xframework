using System;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XConfig
{
    /// <summary>
    /// 配置资源句柄的最小视图。
    /// <para><b>为什么需要它</b>：<see cref="AssetHandle{T}"/> 只有一个接受 YooAsset 句柄的
    /// internal 构造函数，测试进程造不出「有效句柄 + <c>Asset == null</c>」这一态；
    /// 而「类型不符时句柄必须释放」正是要锁住的行为。视图把「资源本体 / 状态 / 释放」
    /// 抽成可替身的四件事，生产实现包住真实句柄，测试实现可以精确记释放次数。</para>
    /// <para>配置加载不是每帧路径，每次加载一个包装实例的分配可接受。</para>
    /// </summary>
    internal interface IConfigAssetLease<out T> : IDisposable where T : UnityEngine.Object
    {
        /// <summary>加载的资源本体；加载失败或类型不符时为 <c>null</c>。</summary>
        T Asset { get; }

        /// <summary>资源定位路径（来自 YooAsset 的 AssetPath）。</summary>
        string Location { get; }

        /// <summary>加载操作状态。</summary>
        AssetOperationStatus Status { get; }

        /// <summary>最近的错误信息；成功时为空。</summary>
        string LastError { get; }
    }

    /// <summary>
    /// <see cref="IConfigAssetLease{T}"/> 的生产实现：包住 <see cref="AssetHandle{T}"/>。
    /// </summary>
    internal sealed class AssetHandleLease<T> : IConfigAssetLease<T> where T : UnityEngine.Object
    {
        private readonly AssetHandle<T> _handle;

        internal AssetHandleLease(AssetHandle<T> handle)
        {
            _handle = handle;
        }

        /// <inheritdoc/>
        public T Asset => _handle.Asset;

        /// <inheritdoc/>
        public string Location => _handle.Location;

        /// <inheritdoc/>
        public AssetOperationStatus Status => _handle.Status;

        /// <inheritdoc/>
        public string LastError => _handle.LastError;

        /// <inheritdoc/>
        public void Dispose()
        {
            _handle.Dispose();
        }
    }

    /// <summary>
    /// 配置资源缺失时的统一归因：区分「加载失败/资源不存在」与「资源存在但类型不符」。
    /// </summary>
    internal static class ConfigAssetError
    {
        /// <summary>
        /// 构造缺失原因。调用方必须已确认 <paramref name="lease"/> 的 <see cref="IConfigAssetLease{T}.Asset"/>
        /// 为 <c>null</c>。
        /// </summary>
        /// <param name="kind">资源种类（text / SO），只用于消息。</param>
        /// <param name="expectedType">期望的资源类型名。</param>
        /// <param name="lease">已加载的句柄视图。</param>
        /// <param name="assetPath">调用方请求的地址。</param>
        internal static ConfigException Missing(
            string kind,
            string expectedType,
            IConfigAssetLease<UnityEngine.Object> lease,
            string assetPath)
        {
            string location = string.IsNullOrEmpty(lease.Location) ? assetPath : lease.Location;

            if (lease.Status == AssetOperationStatus.Succeed)
            {
                return new ConfigException(
                    $"Loaded {kind} asset at '{location}' is not of expected type '{expectedType}' " +
                    "(type mismatch); the resource exists but the loaded object is null.");
            }

            if (lease.Status == AssetOperationStatus.Failed || !string.IsNullOrEmpty(lease.LastError))
            {
                return new ConfigException(
                    $"Failed to load {kind} asset '{assetPath}' (status {lease.Status}, location '{location}'): " +
                    $"{lease.LastError}. Ensure AssetManager is initialized and the asset exists.");
            }

            return new ConfigException(
                $"Failed to load {kind} asset '{assetPath}' (status {lease.Status}); " +
                "AssetManager may not be initialized or the load did not complete.");
        }
    }
}
