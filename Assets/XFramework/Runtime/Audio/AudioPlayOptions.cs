using UnityEngine;

namespace XFramework.XAudio
{
    /// <summary>
    /// 单次播放的参数。不可变值类型，零 GC。
    /// <para><b>参数归一化</b>：为了让省略写法安全，实现会把「未设置」的字段归一化为合理的默认值——
    /// <see cref="VolumeScale"/> 与 <see cref="Pitch"/> 小于等于 <c>0</c> 一律按 <c>1</c> 处理，
    /// <see cref="SpatialBlend"/> 钳到 <c>[0, 1]</c>，<see cref="Channel"/> 为 <c>null</c>/空一律按
    /// <see cref="AudioChannels.Default"/> 处理。</para>
    /// <para><b>由此带来的限制</b>：不能用 <c>VolumeScale = 0</c> 播放一个静音的声音（会被归一化成满音量）。
    /// 要静音请用 <see cref="AudioManager.SetChannelMuted"/> 或把通道音量调到 <c>0</c>。</para>
    /// <para>省略 <c>options</c> 实参时传入的是 <c>default(AudioPlayOptions)</c>（字段全为零），
    /// 经归一化后等价于 <see cref="Default"/>：2D、原音量、原速、不循环、走
    /// <see cref="AudioChannels.Default"/> 通道。</para>
    /// </summary>
    public readonly struct AudioPlayOptions
    {
        #region Public Fields

        /// <summary>
        /// 通道名。为 <c>null</c> 或空字符串时归一化为 <see cref="AudioChannels.Default"/>。
        /// <para>通道决定这次播放受哪一档音量与静音影响。未声明的通道首次使用时按默认值惰性建状态。</para>
        /// </summary>
        public readonly string Channel;

        /// <summary>
        /// 单次播放的音量缩放，与主音量、通道音量相乘。
        /// <para>小于等于 <c>0</c> 一律按 <c>1</c> 处理（见类型注释的归一化说明）。</para>
        /// </summary>
        public readonly float VolumeScale;

        /// <summary>是否循环播放。循环的播放不会被自动回收，必须显式停止。</summary>
        public readonly bool Loop;

        /// <summary>
        /// 播放速度与音高。小于等于 <c>0</c> 一律按 <c>1</c> 处理。
        /// </summary>
        public readonly float Pitch;

        /// <summary>
        /// 2D/3D 混合，<c>0</c> 为纯 2D（默认），<c>1</c> 为纯 3D。超出范围会钳到 <c>[0, 1]</c>。
        /// <para>大于 <c>0</c> 时 <see cref="Position"/> 才会被写入播放位置。</para>
        /// </summary>
        public readonly float SpatialBlend;

        /// <summary>
        /// 3D 播放的世界坐标位置。仅在 <see cref="SpatialBlend"/> 大于 <c>0</c> 时有意义。
        /// <para>框架自持的播放源是<b>定点</b>的：本框架不提供「跟随移动物体」的 3D 发射器
        /// （那需要一个每帧写 Transform 的宿主，属项目侧 MonoBehaviour）。</para>
        /// </summary>
        public readonly Vector3 Position;

        #endregion

        #region Constructors

        /// <summary>
        /// 构造一份播放参数。所有参数都有符合直觉的默认值。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <param name="volumeScale">单次播放的音量缩放；<c>&lt;= 0</c> 按 <c>1</c> 处理。</param>
        /// <param name="loop">是否循环。</param>
        /// <param name="pitch">速度与音高；<c>&lt;= 0</c> 按 <c>1</c> 处理。</param>
        /// <param name="spatialBlend">2D/3D 混合，钳到 <c>[0, 1]</c>。</param>
        /// <param name="position">3D 播放位置。</param>
        public AudioPlayOptions(string channel = null, float volumeScale = 1f, bool loop = false,
            float pitch = 1f, float spatialBlend = 0f, Vector3 position = default)
        {
            Channel = channel;
            VolumeScale = volumeScale;
            Loop = loop;
            Pitch = pitch;
            SpatialBlend = spatialBlend;
            Position = position;
        }

        #endregion

        #region Static

        /// <summary>
        /// 默认参数：2D、原音量、原速、不循环、走 <see cref="AudioChannels.Default"/> 通道。
        /// <para>与 <c>default(AudioPlayOptions)</c> 在归一化后等价——本属性只是让意图更显式。</para>
        /// </summary>
        /// <remarks>
        /// <b>必须显式传参，不能写成 <c>new AudioPlayOptions()</c></b>：C# 对结构体的 <c>new S()</c> 一律
        /// 产生「全部字段为零」的默认值，<b>不会调用任何构造函数</b>——即便存在一个全部参数都可选的构造函数。
        /// 写成无参形式会得到一个 <c>VolumeScale == 0</c> 的假默认值，与本属性的文档承诺相反。
        /// </remarks>
        public static AudioPlayOptions Default => new AudioPlayOptions(null, 1f, false, 1f, 0f, default);

        #endregion
    }
}
