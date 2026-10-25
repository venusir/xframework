using System.Threading;
using UnityEngine;

namespace XFramework.XAudio
{
    /// <summary>
    /// 核心交给 <see cref="IAudioProvider.PlayAsync"/> 的最终播放参数。
    /// <para><b>由核心构造</b>（构造函数 internal），provider 只读取。所有可归一化字段已归一化：
    /// <see cref="Pitch"/> 非正已按 1、<see cref="SpatialBlend"/> 已钳到 <c>[0,1]</c>、
    /// <see cref="Volume"/> 是三档相乘后的最终值。</para>
    /// </summary>
    public readonly struct AudioPlaybackRequest
    {
        #region Public Fields

        /// <summary>是否循环。中间件实现若不支持强制循环（如 FMOD Studio 事件），以事件编排为准。</summary>
        public readonly bool Loop;

        /// <summary>音高；已归一化（非正按 1）。</summary>
        public readonly float Pitch;

        /// <summary>空间混合；已钳到 <c>[0,1]</c>。</summary>
        public readonly float SpatialBlend;

        /// <summary>3D 位置；仅在 <see cref="SpatialBlend"/> 大于 0 时有意义。</summary>
        public readonly Vector3 Position;

        /// <summary>最终音量（主 × 通道 × 单次，已钳到 <c>[0,1]</c>）。provider 不得再乘。</summary>
        public readonly float Volume;

        /// <summary>发起时的管理器暂停快照；实现应在起播后立即进入暂停。</summary>
        public readonly bool Paused;

        /// <summary>
        /// 核心槽位有效性令牌。<b>在 <c>start()</c> 之前检查</b>；已取消说明核心槽位已被
        /// <c>Stop</c>/<c>StopAll</c> 作废，实现应释放已加载资源并返回 <c>default</c>，
        /// 而不是抛 <see cref="System.OperationCanceledException"/>。
        /// </summary>
        public readonly CancellationToken ValidityToken;

        #endregion

        #region Constructors

        internal AudioPlaybackRequest(
            bool loop,
            float pitch,
            float spatialBlend,
            Vector3 position,
            float volume,
            bool paused,
            CancellationToken validityToken)
        {
            Loop = loop;
            Pitch = pitch;
            SpatialBlend = spatialBlend;
            Position = position;
            Volume = volume;
            Paused = paused;
            ValidityToken = validityToken;
        }

        #endregion
    }
}
