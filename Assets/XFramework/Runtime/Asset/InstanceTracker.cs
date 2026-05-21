using UnityEngine;

namespace XFramework.XAsset
{

    /// <summary>
    /// 实例追踪器。自动挂载到 <see cref="AssetManagerImpl"/> 实例化的 GameObject 上。
    /// <para>持有 <see cref="AssetHandle{GameObject}"/>，实例存活期间维持资源引用计数 > 0。</para>
    /// <para>当用户直接调用 <see cref="Object.Destroy(GameObject)"/> 时，通过 OnDestroy 自动通知 AssetManager 回收对象池。</para>
    /// <para>内部类，用户无感知。</para>
    /// </summary>
    internal class InstanceTracker : MonoBehaviour
    {
        /// <summary>资源定位地址。</summary>
        internal string Location;

        /// <summary>是否由 DestroyInstance 主动触发（避免 OnDestroy 中重复通知）。</summary>
        internal bool IsBeingReleased;

        /// <summary>资源句柄。实例存活期间持有，确保底层资源引用计数 > 0。</summary>
        private AssetHandle<GameObject> _handle;

        /// <summary>
        /// 设置资源句柄和定位地址。在 InstantiateAsyncInternal 中调用。
        /// </summary>
        internal void SetHandle(AssetHandle<GameObject> handle, string location)
        {
            _handle = handle;
            Location = location;
        }

        /// <summary>
        /// 释放资源句柄（引用计数 -1）。
        /// <para>在 DestroyInstance 回池满时、或用户直接 Destroy 时调用。</para>
        /// </summary>
        internal void DisposeHandle()
        {
            _handle.Dispose();
        }

        private void OnDestroy()
        {
            if (IsBeingReleased) return;
            // 用户直接调用 Object.Destroy，释放资源引用
            _handle.Dispose();
        }
    }

}