using System;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XSettings
{
    /// <summary>
    /// 自动保存的帧驱动器。仅在 <see cref="SettingsOptions.AutoSave"/> 开启时由管理器创建，
    /// 并经 <see cref="UpdateManager.Register"/> 注册（静态服务直接注册）。
    /// </summary>
    /// <remarks>
    /// <para><b>去抖（debounce）而非节流：</b>等待窗口从<b>最后一次改动</b>起算，因此玩家拖动
    /// 滑条期间不会写盘，松手静默 <see cref="SettingsOptions.AutoSaveDelay"/> 秒后才写一次。
    /// 若做成节流，一次三秒的拖动会写六次。</para>
    /// <para>判定「是否有新改动」用的是管理器的变更计数而非 <c>IsDirty</c>：后者在整个去抖窗口内
    /// 恒为真，区分不出「刚改过」与「改动已久」。</para>
    /// <para><b>写失败不得冒泡：</b>存储后端抛异常（配额、签名、平台 SDK 失败等）时，异常若从
    /// <see cref="OnUpdate"/> 冒出，<c>UpdateScheduler</c> 会把它当作坏节点<b>永久注销</b>——
    /// 于是一个可由环境恢复的失败会变成「本次会话再也不自动保存」，且没有重新注册的路径。
    /// 故此处吞住、按去抖窗口重试、每个失败周期只告警一次。</para>
    /// </remarks>
    internal sealed class SettingsAutoSaveTicker<T> : IUpdateable where T : class, new()
    {
        #region Private Fields

        private readonly SettingsManagerImpl<T> _owner;
        private readonly float _delay;
        private int _lastSeenChange;
        private float _countdown;

        /// <summary>当前失败周期是否已告警。成功一次即复位，使下一个失败周期重新告警。</summary>
        private bool _saveFailureLogged;

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
        public UpdateTier OnUpdate(float deltaTime, float time)
        {
            // 释放后可能仍被调度一次(注销与当帧调度的竞态),此时直接退出
            if (_owner.IsDisposed)
                return UpdateTier.Tier5;

            var current = _owner.ChangeCount;
            if (current != _lastSeenChange)
            {
                // 有新改动:重置等待窗口。这一步是「去抖」而非「节流」的关键
                _lastSeenChange = current;
                _countdown = _delay;
                // 窗口内要的是「粒度」而非「时长」：deadline 是 AutoSaveDelay（默认 0.5 秒），
                // 靠逐帧累减 deltaTime 才守得住。长周期档位在这里帮不上忙——换粗只会让写盘
                // 时间漂移；可省的只有下面的空闲档位，而它本就只值每秒几次字段读
                return UpdateTier.Tier0;
            }

            if (!_owner.IsDirty)
            {
                _countdown = _delay;
                return UpdateTier.Tier3; // 无待提交改动:约 133ms 一次,已足够「几乎不醒来」
            }

            _countdown -= deltaTime;
            if (_countdown > 0f)
                return UpdateTier.Tier0;

            try
            {
                _owner.Save();
                _saveFailureLogged = false;
                return UpdateTier.Tier3;
            }
            catch (Exception e)
            {
                // 释放竞态窗口：此时不该再重试，也不该留日志（与顶部守卫同因）
                if (_owner.IsDisposed)
                    return UpdateTier.Tier5;

                // 每个失败周期只告警一次：持续失败的 store 若每次重试都打印，
                // 就是每秒两次的日志洪水，那本身就是新的缺陷
                if (!_saveFailureLogged)
                {
                    Debug.LogWarning(
                        $"[SettingsManager] 自动保存失败，将在下一个去抖窗口（{_delay:0.##} 秒）后重试，" +
                        $"后续失败不再重复打印：{e.GetType().Name}: {e.Message}");
                    _saveFailureLogged = true;
                }

                // 重新起算窗口：不重算就是每帧重试，持续失败的 store 会以约 7.5 次/秒空转。
                // 改动不会丢——Save 只在写入正常返回后才推进已提交档位，故 IsDirty 自动保持为真
                _countdown = _delay;
                return UpdateTier.Tier0;
            }
        }

        #endregion
    }
}
