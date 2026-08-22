using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XAsset
{

    /// <summary>
    /// 实例追踪器。自动挂载到 <see cref="AssetManagerImpl"/> 实例化的 GameObject 上。
    /// <para>持有 <see cref="AssetHandle{GameObject}"/>，实例存活期间维持资源引用计数 > 0。
    /// 回池时句柄保留（资源保活），真正销毁时才释放。</para>
    /// <para>当用户直接调用 <see cref="Object.Destroy(GameObject)"/> 时，通过 OnDestroy 自动释放资源引用。
    /// 注意：直接 Destroy 的实例不会回池——OnDestroy 阶段操作对象池在 Unity 语义下不可靠，属有意设计。</para>
    /// <para>内部类，用户无感知。</para>
    /// </summary>
    internal class InstanceTracker : MonoBehaviour
    {
        /// <summary>资源定位地址。</summary>
        internal string Location;

        /// <summary>资源句柄。实例存活期间持有，确保底层资源引用计数 > 0。</summary>
        private AssetHandle<GameObject> _handle;

        /// <summary>句柄是否已释放。防止多条销毁路径（池满销毁、Dispose 清理、用户直接 Destroy）重复 Release。</summary>
        private bool _handleReleased;

        /// <summary>location → 当前活跃（SetActive(true)）实例数，供 <see cref="IAssetManager.GetPoolStatus"/> 统计。</summary>
        private static readonly Dictionary<string, int> _activeCounts = new Dictionary<string, int>();

        static InstanceTracker()
        {
            // 运行时退出时清理静态计数；Editor 退出播放模式时由域重载自动重置
            Application.quitting += () => _activeCounts.Clear();
        }

        /// <summary>
        /// 设置资源句柄和定位地址。在 InstantiateAsyncInternal 中调用。
        /// </summary>
        internal void SetHandle(AssetHandle<GameObject> handle, string location)
        {
            _handle = handle;
            Location = location;

            // AddComponent 时 OnEnable 先于 SetHandle 触发（Location 尚未赋值，OnEnable 中跳过计数），
            // 因此实例创建后此处补记一次活跃计数。
            if (gameObject.activeInHierarchy)
                IncrementCount(location);
        }

        /// <summary>
        /// 释放资源句柄（引用计数 -1）。幂等：已释放时直接返回。
        /// <para>在实例真正销毁时调用（池满销毁、Dispose 清理、用户直接 Destroy 的 OnDestroy）。</para>
        /// </summary>
        internal void DisposeHandle()
        {
            if (_handleReleased) return;
            _handleReleased = true;
            _handle.Dispose();
        }

        /// <summary>
        /// 获取指定地址的活跃实例数（调试统计用）。
        /// </summary>
        internal static int GetActiveCount(string location)
        {
            return _activeCounts.TryGetValue(location, out var count) ? count : 0;
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(Location)) return; // 创建瞬间 SetHandle 之前的 OnEnable，跳过
            IncrementCount(Location);
        }

        private void OnDisable()
        {
            DecrementCount(Location);
        }

        private void OnDestroy()
        {
            // 用户直接 Destroy 时自动释放资源引用（幂等；池满/Dispose 路径已手动释放则跳过）
            DisposeHandle();
        }

        private static void IncrementCount(string location)
        {
            _activeCounts[location] = (_activeCounts.TryGetValue(location, out var count) ? count : 0) + 1;
        }

        private static void DecrementCount(string location)
        {
            if (!_activeCounts.TryGetValue(location, out var count)) return;
            if (count <= 1) _activeCounts.Remove(location);
            else _activeCounts[location] = count - 1;
        }
    }
}
