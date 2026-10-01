using System;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XTimer
{
    /// <summary>
    /// 一条时间轴一个的驱动器：把该轴收到的 <c>deltaTime</c> 灌进逻辑时钟，然后扫尾该轴的全部槽位表。
    /// <para><b>它是本模块唯一接触 Update 的地方</b>，也是「暂停语义免费」的落点：定时器的截止时刻建在
    /// 这个由 <c>deltaTime</c> 累加出的逻辑时钟上，于是 <c>timeScale = 0</c> 或
    /// <c>UpdateManager.Pause()</c> 时该轴不派发、时钟自然冻结，恢复后从冻结点续上——
    /// 本模块没有一行暂停代码，也没有第二条时间口径。</para>
    /// <para><b>为何两条轴各要一个实例</b>：调度器内部按节点维护单值的「轴 × 档位」桶索引，同一个节点
    /// 无法同时挂在两条轴上。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>异常绝不允许逃出 <see cref="OnUpdate"/></b>：<c>UpdateScheduler</c> 会把抛出异常的节点
    /// 当作坏节点<b>永久注销</b>——那会表现为「该轴全部定时器静默死亡」，且没有恢复路径。故每个回调在
    /// 槽位表的扫尾里单独收口，这里再兜一层，保证任何遗漏也不会波及驱动器本身。</para>
    /// <para><b>空闲即退出调度</b>：该轴一个正在计时的定时器都没有时注销自己，让「框架装了但没用定时器」
    /// 的开销真正归零；下一次创建会重新注册。</para>
    /// </remarks>
    internal sealed class TimerTicker : IUpdateable
    {
        #region Private Fields

        private readonly TimerCore _core;
        private readonly int _axis;
        private bool _registered;

        #endregion

        #region Constructors

        /// <summary>建一个尚未注册的驱动器。</summary>
        /// <param name="core">所属核心。</param>
        /// <param name="axis">时间轴。</param>
        internal TimerTicker(TimerCore core, int axis)
        {
            _core = core;
            _axis = axis;
        }

        #endregion

        #region Internal — Registration

        /// <summary>该驱动器当前是否已注册进 Update。</summary>
        internal bool IsRegistered => _registered;

        /// <summary>确保已注册；重复调用是安全的。</summary>
        internal void EnsureRegistered()
        {
            if (_registered)
                return;

            _registered = true;
            UpdateManager.Register(this, order: 0, initialTier: UpdateTier.Tier0, timeMode: (UpdateTimeMode)_axis);
        }

        /// <summary>退出调度；重复调用是安全的。</summary>
        internal void GoIdle()
        {
            if (!_registered)
                return;

            _registered = false;
            UpdateManager.Unregister(this);
        }

        #endregion

        #region IUpdateable

        /// <inheritdoc />
        public void OnEnable()
        {
        }

        /// <inheritdoc />
        public void OnDisable()
        {
        }

        /// <inheritdoc />
        public UpdateTier OnUpdate(float deltaTime, float time)
        {
            try
            {
                _core.Sweep(_axis, deltaTime);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Timer] 定时器扫尾出现未预期异常，本拍已跳过：{e}");
            }

            return UpdateTier.Tier0;
        }

        #endregion
    }
}
