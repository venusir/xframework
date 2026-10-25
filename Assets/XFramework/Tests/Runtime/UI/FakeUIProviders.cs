using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// HUD provider 替身：只为观察「每帧通路是否还在驱动我」与「抛异常时的隔离与日志阻尼」。
    /// <para>与 <see cref="FakeUIManager"/> 同一取舍：不被断言的成员一律抛，避免「用例其实调到了没实现的
    /// 成员」变成绿灯。唯独 <c>DetachAll</c> 给空实现——换 provider、<c>Dispose</c>、<c>CloseAllAsync</c>
    /// 三条正常路径都会调它，抛异常等于让替身自己在无关用例里炸掉。</para>
    /// </summary>
    internal sealed class FaultyHudProvider : IUIHudProvider
    {
        /// <summary>被每帧通路驱动（<c>Update</c>）的次数。</summary>
        public int UpdateCount { get; private set; }

        /// <summary>被要求回收全部 HUD 的次数。</summary>
        public int DetachAllCount { get; private set; }

        /// <summary>置 true 时 <c>Update</c> 抛异常。</summary>
        public bool ThrowOnUpdate { get; set; }

        public bool HasActive => false;

        /// <summary>置 true 时 <c>DetachAll</c> 抛异常（R3-UIA-004 的隔离用例）。</summary>
        public bool ThrowOnDetachAll { get; set; }

        /// <summary>置 true 时 <c>Detach</c> 抛异常（R4-UIB-004 的隔离用例）。</summary>
        public bool ThrowOnDetach { get; set; }

        /// <summary>被要求 detach 的次数。</summary>
        public int DetachCount { get; private set; }

        public void Update(float deltaTime, float time)
        {
            UpdateCount++;

            if (ThrowOnUpdate)
                throw new InvalidOperationException("[FaultyHudProvider] 故意在 Update 里抛异常");
        }

        public void DetachAll()
        {
            DetachAllCount++;

            if (ThrowOnDetachAll)
                throw new InvalidOperationException("[FaultyHudProvider] 故意在 DetachAll 里抛异常");
        }

        // SetUIRoot 记数：注入路径会在初始化后补调一次（R3-ADJ-12），用例据此断言接线
        public int UIRootSetCount { get; private set; }

        public void SetUIRoot(Transform uiRoot)
        {
            UIRootSetCount++;
        }

        public UniTask<T> AttachAsync<T>(Transform target, string assetPath, Vector2? offset = null,
            CancellationToken cancellationToken = default) where T : UIHudItem
            => throw new NotSupportedException("[FaultyHudProvider] 本替身不实现 AttachAsync");

        public void Detach(Transform target)
        {
            DetachCount++;

            if (ThrowOnDetach)
                throw new InvalidOperationException("[FaultyHudProvider] 故意在 Detach 里抛异常");
        }
    }

    /// <summary>
    /// Tip provider 替身：同 <see cref="FaultyHudProvider"/>，用于验证「一个 provider 抛异常不得吃掉
    /// 同帧另一个 provider 的更新」。
    /// </summary>
    internal sealed class CountingTipProvider : IUITipProvider
    {
        /// <summary>被每帧通路驱动（<c>Update</c>）的次数。</summary>
        public int UpdateCount { get; private set; }

        /// <summary>被要求回收全部 Tip 的次数。</summary>
        public int DetachAllCount { get; private set; }

        /// <summary>置 true 时 <c>Update</c> 抛异常。</summary>
        public bool ThrowOnUpdate { get; set; }

        /// <summary>置 true 时 <c>DetachAll</c> 抛异常（R4-UIB-004 的隔离用例）。</summary>
        public bool ThrowOnDetachAll { get; set; }

        /// <summary>被要求显示 Tip 的次数。</summary>
        public int ShowTipCallCount { get; private set; }

        public void Update(float deltaTime, float time)
        {
            UpdateCount++;

            if (ThrowOnUpdate)
                throw new InvalidOperationException("[CountingTipProvider] 故意在 Update 里抛异常");
        }

        public void DetachAll()
        {
            DetachAllCount++;

            if (ThrowOnDetachAll)
                throw new InvalidOperationException("[CountingTipProvider] 故意在 DetachAll 里抛异常");
        }

        public int UIRootSetCount { get; private set; }

        public void SetUIRoot(Transform uiRoot)
        {
            UIRootSetCount++;
        }

        public UniTask ShowTipAsync(string text, TipConfig config = default,
            CancellationToken cancellationToken = default)
        {
            ShowTipCallCount++;
            return UniTask.CompletedTask;
        }
    }
}
