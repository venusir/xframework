using System;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机句柄。命名相机与动态相机都经 <see cref="CameraManager.Register{T}(string, T)"/> /
    /// <see cref="CameraManager.Register{T}(T)"/>（或 <see cref="ICameraManager"/> 同名成员）注册后取得；
    /// 之后的激活 / 反激活 / 过渡 / 查询 / 注销都只收句柄。
    /// <para>支持 <c>using</c> 语法（<see cref="Dispose"/> 即注销），也可手动 <see cref="Unregister"/>。</para>
    /// <para>零 GC：不可变值类型，只存两个整型 + 一个命名 id 字符串引用；不存委托、不存实现对象；
    /// 复制不分配。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>为什么字段是公开的</b>：本模块的后端替换点是整体替换 <see cref="ICameraManager"/>。
    /// 第三方实现必须能<b>造出</b>句柄、也必须能<b>读回</b>自己编码进去的值，因此字段必须是公开面
    /// （与 <see cref="XAudio.AudioHandle"/> 同一条理由）。</para>
    /// <para><b>门面不解释 <see cref="Id"/> / <see cref="Generation"/></b>：<see cref="Activate"/> /
    /// <see cref="TransitionTo"/> / <see cref="Unregister"/> 一律转发给当前实现，由实现校验。<b>框架级的唯一
    /// 契约</b>是 <see cref="Generation"/> 为 <c>0</c> 保留给 <c>default(CameraHandle)</c>，任何实现都必须把它视为无效。</para>
    /// <para><b>注销后的旧句柄</b>：实现应按「槽位 + 代际」双校验；槽位复用后代际递增，旧句柄自然失效——
    /// 激活 / 过渡 / 反激活告警 no-op，<see cref="IsActive"/> / <see cref="Unregister"/> 返回 <c>false</c>。</para>
    /// <para><b><see cref="NamedId"/></b>：命名相机的 string id；动态相机与 <c>default</c> 为 <c>null</c>。
    /// <c>CameraManager.ActiveCameraId</c> 直接读它（O(1)），实现必须为命名槽位填上对应的 id。</para>
    /// <para><b>门面未初始化 / 已销毁时</b>：命令（<see cref="Activate"/> / <see cref="Deactivate"/> /
    /// <see cref="TransitionTo"/>）仍抛 <see cref="InvalidOperationException"/>；<see cref="IsActive"/> 返回
    /// <c>false</c>，<see cref="Unregister"/> / <see cref="Dispose"/> 返回 <c>false</c>——预制在 <c>OnDestroy</c>
    /// 里清理时，门面可能已经先销毁。</para>
    /// </remarks>
    public readonly struct CameraHandle : IEquatable<CameraHandle>, IDisposable
    {
        #region Public Fields

        /// <summary>
        /// 实现方自定的标识。内置实现用它承载槽位下标；第三方实现可承载自己的句柄 id。
        /// <para><b>门面不解释它</b>，只有产生它的那个实现知道含义。</para>
        /// </summary>
        public readonly int Id;

        /// <summary>
        /// 实现方自定的代际号，用于让「已被注销 / 槽位已复用」的旧句柄失效。
        /// <para><b><c>0</c> 是保留值</b>：<c>default(CameraHandle)</c> 的 <see cref="Generation"/> 为 <c>0</c>，
        /// 实现必须把它视为无效句柄——有效句柄的代际必须从 <c>1</c> 起。</para>
        /// </summary>
        public readonly int Generation;

        /// <summary>
        /// 命名相机的 string id；动态相机与 <c>default</c> 为 <c>null</c>。
        /// <para><b>不参与身份比较</b>（身份是 <see cref="Id"/> + <see cref="Generation"/>）：同一槽位 / 代际
        /// 必然对同一个命名 id。它供 <c>CameraManager.ActiveCameraId</c> 直接读取。</para>
        /// </summary>
        public readonly string NamedId;

        #endregion

        #region Constructors

        /// <summary>
        /// 构造一个句柄。
        /// <para><b>本构造函数供 <see cref="ICameraManager"/> 实现方使用</b>，常规调用方不应直接调用——
        /// 请用 <see cref="CameraManager.Register{T}(string, T)"/> / <see cref="CameraManager.Register{T}(T)"/>
        /// 取得句柄。</para>
        /// </summary>
        /// <param name="id">实现方自定的标识。</param>
        /// <param name="generation">代际号；有效句柄必须非 <c>0</c>。</param>
        /// <param name="namedId">命名相机的 string id；动态相机传 <c>null</c>（默认）。</param>
        public CameraHandle(int id, int generation, string namedId = null)
        {
            Id = id;
            Generation = generation;
            NamedId = namedId;
        }

        #endregion

        #region Public API

        /// <summary>本句柄是否是默认值（即 <c>default(CameraHandle)</c>）。</summary>
        public bool IsDefault => Generation == 0;

        /// <summary>立即切换到这台相机（cut）。<c>default</c> 句柄为空操作。</summary>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public void Activate()
        {
            if (IsDefault)
                return;

            CameraManager.Activate(this);
        }

        /// <summary>反激活这台相机。<c>default</c> 句柄为空操作。</summary>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public void Deactivate()
        {
            if (IsDefault)
                return;

            CameraManager.Deactivate(this);
        }

        /// <summary>过渡到这台相机。<c>default</c> 句柄为空操作。</summary>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut。</param>
        /// <param name="style">过渡样式。</param>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public void TransitionTo(float duration, CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            if (IsDefault)
                return;

            CameraManager.TransitionTo(this, duration, style);
        }

        /// <summary>这台相机是否为 Brain 当前活动（过渡期间 incoming 也算活动）。</summary>
        /// <returns><c>default</c> 句柄、门面未初始化 / 已销毁时返回 <c>false</c>。</returns>
        public bool IsActive()
        {
            return !IsDefault && CameraManager.IsInitialized && CameraManager.IsActive(this);
        }

        /// <summary>注销这台相机；返回是否真的移除了一个已注册的相机。</summary>
        /// <remarks><c>default</c> 句柄、已注销的旧句柄、或门面尚未初始化 / 已销毁时返回 <c>false</c>——
        /// 预制在 <c>OnDestroy</c> 里调用它时门面可能已经先销毁。</remarks>
        public bool Unregister()
        {
            return !IsDefault && CameraManager.Unregister(this);
        }

        /// <summary>注销这台相机，供 <c>using</c> 使用。等价于 <see cref="Unregister"/>。</summary>
        public void Dispose()
        {
            Unregister();
        }

        #endregion

        #region Equality

        /// <inheritdoc/>
        public bool Equals(CameraHandle other)
        {
            return Id == other.Id && Generation == other.Generation;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is CameraHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (Id * 397) ^ Generation;
        }

        /// <summary>相等比较：<see cref="Id"/> 与 <see cref="Generation"/> 都相同（<see cref="NamedId"/> 不参与）。</summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        public static bool operator ==(CameraHandle left, CameraHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>不等比较。</summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        public static bool operator !=(CameraHandle left, CameraHandle right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return NamedId == null
                ? "CameraHandle(" + Id + ", " + Generation + ")"
                : "CameraHandle(" + Id + ", " + Generation + ", " + NamedId + ")";
        }

        #endregion
    }
}
