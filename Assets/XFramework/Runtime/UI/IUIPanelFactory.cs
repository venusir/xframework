using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// 面板实例的来源。默认实现 <see cref="AssetPanelFactory"/> 经 <see cref="XAsset.AssetManager"/>
    /// 加载预制体并复用其对象池。
    /// <para>抽成接口是为了让测试能在不初始化 YooAsset 的前提下打开真实面板——注入点是
    /// <see cref="UIManager.PanelFactoryFactory"/>。生产路径永远使用默认实现。</para>
    /// </summary>
    internal interface IUIPanelFactory
    {
        /// <summary>
        /// 创建指定类型的面板实例并挂到 <paramref name="parent"/> 下。
        /// </summary>
        /// <typeparam name="T">面板类型。</typeparam>
        /// <param name="assetPath">面板预制体的 YooAsset 地址。</param>
        /// <param name="parent">该层级对应的容器节点。</param>
        /// <returns>面板实例；加载失败或预制体缺少目标组件时返回 null（错误由实现方记录）。</returns>
        UniTask<T> CreateAsync<T>(string assetPath, Transform parent) where T : UIPanelBase;

        /// <summary>
        /// 回收面板实例（回对象池或销毁）。
        /// </summary>
        /// <param name="panel">要回收的面板；为 null 时不做任何事。</param>
        void Release(UIPanelBase panel);
    }
}
