using UnityEngine;

namespace XFramework.XAsset
{

    /// <summary>
    /// 实例追踪器。自动挂载到 <see cref="AssetManager"/> 实例化的 GameObject 上。
    /// <para>当用户直接调用 <see cref="Object.Destroy(GameObject)"/> 时，通过 OnDestroy 自动通知 <see cref="AssetManager"/> 释放引用。</para>
    /// <para>内部类，用户无感知。</para>
    /// </summary>
    internal class InstanceTracker : MonoBehaviour
    {
        /// <summary>所属的资源服务实例。</summary>
        internal AssetManagerImpl OwnerManager;

        /// <summary>资源定位地址。</summary>
        internal string Location;

        /// <summary>是否由 DestroyInstance 主动触发（避免重复通知）。</summary>
        internal bool IsBeingReleased;

        private void OnDestroy()
        {
            if (IsBeingReleased) return;
            if (OwnerManager == null || string.IsNullOrEmpty(Location)) return;

            OwnerManager.OnInstanceDestroyed(Location, gameObject);
        }
    }


}
