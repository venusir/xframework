using System;

namespace XFramework.XAudio
{
    /// <summary>
    /// provider 层的播放句柄。由 <see cref="IAudioProvider.PlayAsync"/> 返回，核心槽位表保存它。
    /// <para><b>字段对核心不透明</b>：实现自行产生与校验。有效句柄的 <see cref="Generation"/> 必须非 <c>0</c>；
    /// <c>default(AudioProviderHandle)</c> 必须被视为无效。</para>
    /// <para>零 GC：不可变值类型，只存两个整型。</para>
    /// </summary>
    public readonly struct AudioProviderHandle : IEquatable<AudioProviderHandle>
    {
        #region Public Fields

        /// <summary>实现方自定的标识（内置实现承载 provider 槽位索引）。</summary>
        public readonly int Id;

        /// <summary>
        /// 实现方自定的代际号。<c>0</c> 保留给 <c>default</c>；有效句柄从 <c>1</c> 起。
        /// </summary>
        public readonly int Generation;

        #endregion

        #region Constructors

        /// <summary>
        /// 构造一个 provider 句柄。供 <see cref="IAudioProvider"/> 实现方使用。
        /// </summary>
        /// <param name="id">实现方自定的标识。</param>
        /// <param name="generation">代际号；有效句柄必须非 <c>0</c>。</param>
        public AudioProviderHandle(int id, int generation)
        {
            Id = id;
            Generation = generation;
        }

        #endregion

        #region Public API

        /// <summary>是否是默认值（即 <c>default(AudioProviderHandle)</c>）。</summary>
        public bool IsDefault => Generation == 0;

        #endregion

        #region Equality

        /// <inheritdoc/>
        public bool Equals(AudioProviderHandle other) => Id == other.Id && Generation == other.Generation;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is AudioProviderHandle other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Id * 397) ^ Generation;
            }
        }

        /// <summary>两个 provider 句柄的 Id 与 Generation 都相同。</summary>
        public static bool operator ==(AudioProviderHandle left, AudioProviderHandle right) => left.Equals(right);

        /// <summary>两个 provider 句柄的 Id 或 Generation 不同。</summary>
        public static bool operator !=(AudioProviderHandle left, AudioProviderHandle right) => !left.Equals(right);

        #endregion
    }
}
