using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// HUD 提供者接口。第三方可实现此接口来替换 HUD 的管理方式（如使用世界空间 Canvas、自定义对象池等）。
    /// <para>默认实现为 <see cref="UIHudManagerImpl"/>，通过 <see cref="UIManager.SetHudProvider"/> 注入。</para>
    /// </summary>
    public interface IUiHudProvider
    {
        /// <summary>
        /// 设置 UI 根节点。在 <see cref="UIManager.Initialize"/> 时自动调用。
        /// </summary>
        /// <param name="uiRoot">UIRoot Transform。</param>
        void SetUIRoot(Transform uiRoot);

        /// <summary>
        /// 为 3D 目标附加一个 HUD 实例。同一个 target 同时只能绑定一个 HUD，重复调用会先 Detach 旧的。
        /// </summary>
        /// <typeparam name="T">HUD 类型（继承 <see cref="UIHudItem"/>）。</typeparam>
        /// <param name="target">要跟随的 3D 目标 Transform。</param>
        /// <param name="assetPath">HUD 预制体的 YooAsset 地址。</param>
        /// <param name="offset">屏幕坐标偏移（像素）。</param>
        /// <returns>附加的 HUD 实例。</returns>
        UniTask<T> AttachAsync<T>(Transform target, string assetPath, Vector2? offset = null)
            where T : UIHudItem;

        /// <summary>
        /// 分离指定目标绑定的 HUD。
        /// </summary>
        /// <param name="target">3D 目标 Transform。如果传入 null 则不执行任何操作。</param>
        void Detach(Transform target);

        /// <summary>
        /// 分离所有 HUD。
        /// </summary>
        void DetachAll();

        /// <summary>
        /// 由 <see cref="UIManager.Update"/> 调用，驱动所有活跃 HUD 的每帧更新。
        /// </summary>
        void Update();

        /// <summary>
        /// 是否有活跃的 HUD。
        /// </summary>
        bool HasActive { get; }
    }
}