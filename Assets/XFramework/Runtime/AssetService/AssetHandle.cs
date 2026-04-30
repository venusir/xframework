using System;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 资源句柄。封装对已加载资源的只读引用。
    /// <para>通过 <see cref="IAssetService.LoadAssetAsync"/> 获取，使用完毕后应调用 <see cref="Release"/> 释放引用。</para>
    /// </summary>
    public readonly struct AssetHandle : IDisposable
    {
        /// <summary>内部持有的资源对象。</summary>
        private readonly object _asset;

        /// <summary>所属的资源服务，用于释放引用计数。</summary>
        private readonly IAssetService _owner;

        /// <summary>资源定位地址，用于释放时查找引用计数。</summary>
        private readonly string _location;

        /// <summary>资源是否有效。</summary>
        public bool IsValid => _asset != null;

        /// <summary>资源定位地址。</summary>
        internal string Location => _location;

        internal AssetHandle(object asset, IAssetService owner, string location)
        {
            _asset = asset;
            _owner = owner;
            _location = location;
        }

        /// <summary>
        /// 获取资源并转换为指定类型。
        /// <para>如果资源为 null 或类型不匹配，返回 null。</para>
        /// </summary>
        public T GetAsset<T>() where T : UnityEngine.Object
        {
            return _asset as T;
        }

        /// <summary>
        /// 同步实例化 GameObject 资源。
        /// <para>如果资源不是 GameObject 类型，返回 null。</para>
        /// </summary>
        /// <param name="parent">父 Transform。</param>
        /// <returns>实例化后的 GameObject，失败返回 null。</returns>
        public GameObject Instantiate(Transform parent = null)
        {
            var prefab = _asset as GameObject;
            if (prefab == null)
                return null;

            return parent != null
                ? UnityEngine.Object.Instantiate(prefab, parent)
                : UnityEngine.Object.Instantiate(prefab);
        }

        /// <summary>
        /// 同步实例化 GameObject 资源，并设置位置和旋转。
        /// </summary>
        /// <param name="position">世界坐标位置。</param>
        /// <param name="rotation">世界坐标旋转。</param>
        /// <param name="parent">父 Transform。</param>
        /// <returns>实例化后的 GameObject，失败返回 null。</returns>
        public GameObject Instantiate(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var prefab = _asset as GameObject;
            if (prefab == null)
                return null;

            return parent != null
                ? UnityEngine.Object.Instantiate(prefab, position, rotation, parent)
                : UnityEngine.Object.Instantiate(prefab, position, rotation);
        }

        /// <summary>
        /// 释放资源。与 <see cref="Release"/> 行为一致，支持 using 语法。
        /// </summary>
        public void Dispose()
        {
            Release();
        }

        /// <summary>
        /// 释放此句柄引用的资源（引用计数减一）。
        /// <para>调用后不应再使用此句柄。</para>
        /// </summary>
        public void Release()
        {
            if (_owner != null && _asset != null)
            {
                _owner.Release(this);
            }
        }
    }
}
