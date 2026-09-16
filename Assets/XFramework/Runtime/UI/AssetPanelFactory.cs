using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// <see cref="IUIPanelFactory"/> 的默认实现：经 <see cref="AssetManager"/> 加载面板预制体，
    /// 并复用其按地址索引的对象池与引用计数。
    /// </summary>
    internal sealed class AssetPanelFactory : IUIPanelFactory
    {
        #region IUIPanelFactory

        /// <inheritdoc/>
        public async UniTask<T> CreateAsync<T>(string assetPath, Transform parent,
            CancellationToken cancellationToken = default) where T : UIPanelBase
        {
            var type = typeof(T);

            // AssetManager.InstantiateAsync 内部已处理对象池逻辑：
            // 池中有闲置实例 → 直接复用，池中无 → 加载资源并实例化
            var go = await AssetManager.InstantiateAsync(assetPath, parent, cancellationToken);
            if (go == null)
                return null;

            go.name = type.Name;

            var panel = go.GetComponent<T>();
            if (panel == null)
            {
                Debug.LogError(
                    $"[UIManager] Prefab at '{assetPath}' lacks component {type.Name}. Destroying instance.");
                AssetManager.DestroyInstance(go);
                return null;
            }

            return panel;
        }

        /// <inheritdoc/>
        public void Release(UIPanelBase panel)
        {
            if (panel != null && panel.gameObject != null)
                AssetManager.DestroyInstance(panel.gameObject);
        }

        #endregion
    }
}
