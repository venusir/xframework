using XFramework.XUpdate;

namespace XFramework.XSettings
{
    /// <summary>
    /// 自动保存的帧驱动器。仅在 <see cref="SettingsOptions.AutoSave"/> 开启时由管理器创建，
    /// 并经 <see cref="UpdateManager.Register"/> 注册（静态服务直接注册，无需节点树）。
    /// </summary>
    /// <remarks>
    /// <para><b>去抖（debounce）而非节流：</b>等待窗口从<b>最后一次改动</b>起算，因此玩家拖动
    /// 滑条期间不会写盘，松手静默 <see cref="SettingsOptions.AutoSaveDelay"/> 秒后才写一次。
    /// 若做成节流，一次三秒的拖动会写六次。</para>
    /// <para>判定「是否有新改动」用的是管理器的变更计数而非 <c>IsDirty</c>：后者在整个去抖窗口内
    /// 恒为真，区分不出「刚改过」与「改动已久」。</para>
    /// </remarks>
    internal sealed class SettingsAutoSaveTicker<T> : IUpdateable where T : class, new()
    {
        #region Private Fields

        private readonly SettingsManagerImpl<T> _owner;
        private readonly float _delay;
        private int _lastSeenChange;
        private float _countdown;

        #endregion

        #region Constructors

        internal SettingsAutoSaveTicker(SettingsManagerImpl<T> owner, float delay)
        {
            _owner = owner;
            _delay = delay < 0f ? 0f : delay;
            _countdown = _delay;
        }

        #endregion

        #region IUpdateable

        /// <inheritdoc />
        public void OnEnable() { }

        /// <inheritdoc />
        public void OnDisable() { }

        /// <inheritdoc />
        public UpdateLOD OnUpdate(float deltaTime, float time)
        {
            // 释放后可能仍被调度一次(注销与当帧调度的竞态),此时直接退出
            if (_owner.IsDisposed)
                return UpdateLOD.Tier5;

            var current = _owner.ChangeCount;
            if (current != _lastSeenChange)
            {
                // 有新改动:重置等待窗口。这一步是「去抖」而非「节流」的关键
                _lastSeenChange = current;
                _countdown = _delay;
                return UpdateLOD.Tier0; // 窗口内需要较细的粒度才能守住 delay
            }

            if (!_owner.IsDirty)
            {
                _countdown = _delay;
                return UpdateLOD.Tier3; // 无待提交改动,几乎不必醒来
            }

            _countdown -= deltaTime;
            if (_countdown > 0f)
                return UpdateLOD.Tier0;

            _owner.Save();
            return UpdateLOD.Tier3;
        }

        #endregion
    }
}
