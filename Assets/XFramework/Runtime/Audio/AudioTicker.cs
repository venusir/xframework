using XFramework.XUpdate;

namespace XFramework.XAudio
{
    /// <summary>
    /// 播放源回收的帧驱动。只在确实有活跃播放时注册，最后一个播放结束时注销——闲置时零帧开销。
    /// <para><b>档位 <see cref="UpdateTier.Tier1"/></b>（约 33ms）：回收延迟对音效池无感，
    /// 不需要 <c>Tier0</c> 的每帧精度。</para>
    /// <para><b>时间轴用 <see cref="UpdateTimeMode.Unscaled"/></b>：回收是记账而不是玩法逻辑，
    /// 游戏暂停（<c>timeScale = 0</c>）期间照样要能把播完的槽位收回来——否则暂停一场战斗会把整池
    /// 播放源占死。这与 Update README 举的「手柄振动到期」是同一类需求。</para>
    /// <para><b>注册/注销都幂等</b>：由持有方用一个布尔量记账，先摘再插交给调度器保证。</para>
    /// </summary>
    internal sealed class AudioTicker : IUpdateable
    {
        #region Private Fields

        private readonly AudioManagerImpl _owner;

        #endregion

        #region Constructors

        internal AudioTicker(AudioManagerImpl owner)
        {
            _owner = owner;
        }

        #endregion

        #region IUpdateLifecycle

        /// <summary>本对象没有启用/停用时要做的额外事情。</summary>
        public void OnEnable()
        {
        }

        /// <summary>本对象没有启用/停用时要做的额外事情。</summary>
        public void OnDisable()
        {
        }

        #endregion

        #region IUpdateable

        /// <summary>扫一遍槽位并回收播完的。</summary>
        /// <param name="deltaTime">距上次派发的时间差（本对象不用它）。</param>
        /// <param name="time">当前时间（本对象不用它）。</param>
        /// <returns>恒定 <see cref="UpdateTier.Tier1"/>——档位在注册时就声明，这里只是维持。</returns>
        public UpdateTier OnUpdate(float deltaTime, float time)
        {
            _owner.SweepVoices();
            return UpdateTier.Tier1;
        }

        #endregion
    }
}
