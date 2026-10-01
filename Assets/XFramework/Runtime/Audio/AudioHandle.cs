using System;

namespace XFramework.XAudio
{
    /// <summary>
    /// 播放句柄。由 <see cref="AudioManager.Play"/> / <see cref="AudioManager.PlayAsync"/> 返回，
    /// <see cref="Dispose"/> 时停止播放。
    /// <para>支持 <c>using</c> 语法，也支持手动 <see cref="Stop"/> / <see cref="Dispose"/>。</para>
    /// <para>零 GC：不可变值类型，只存两个整型，不存委托、不存对象引用；可自由复制与存入容器。</para>
    /// <para><b>句柄是可复制的值</b>：离开作用域不会停止播放——只有 <c>using</c> 结束、显式
    /// <see cref="Dispose"/> 或 <see cref="Stop"/> 才会。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>为什么字段是公开的</b>：本模块的后端替换点是整体替换 <see cref="IAudioManager"/>
    /// （同 <c>XInput</c> 换 Rewired 的形态）。第三方实现必须能<b>造出</b>句柄、也必须能<b>读回</b>
    /// 自己编码进去的值，因此两个字段必须是公开面。<c>LockHandle</c> 的构造函数之所以是 <c>internal</c>，
    /// 是因为只有 <c>LockManager</c> 造句柄——那条理由在这里不成立，照抄会把整体替换这条路堵死。</para>
    /// <para><b>门面不解释任何字段</b>：<see cref="AudioManager.Stop"/> 与
    /// <see cref="AudioManager.IsPlaying"/> 一律转发给当前实现，由实现校验。<b>框架级的唯一契约</b>是
    /// <see cref="Generation"/> 为 <c>0</c> 保留给 <c>default(AudioHandle)</c>，任何实现都必须把它视为无效。</para>
    /// </remarks>
    public readonly struct AudioHandle : IDisposable
    {
        #region Public Fields

        /// <summary>
        /// 实现方自定的标识。内置实现用它承载池槽位索引；中间件实现可承载事件实例 id 等。
        /// <para><b>门面不解释它</b>，只有产生它的那个实现知道含义。</para>
        /// </summary>
        public readonly int Id;

        /// <summary>
        /// 实现方自定的代际号，用于让「已被复用/停止」的旧句柄失效。
        /// <para><b><c>0</c> 是保留值</b>：<c>default(AudioHandle)</c> 的 <see cref="Generation"/> 为 <c>0</c>，
        /// 实现必须把它视为无效句柄——因此有效句柄的代际必须从 <c>1</c> 起（递增时跳过 <c>0</c>）。</para>
        /// <para>中间件实现若没有代际概念，恒定填 <c>1</c> 即可。</para>
        /// </summary>
        public readonly int Generation;

        #endregion

        #region Constructors

        /// <summary>
        /// 构造一个句柄。
        /// <para><b>本构造函数供 <see cref="IAudioManager"/> 实现方使用</b>，常规调用方不应直接调用——
        /// 请用 <see cref="AudioManager.Play"/> / <see cref="AudioManager.PlayAsync"/> 取得句柄。</para>
        /// </summary>
        /// <param name="id">实现方自定的标识。</param>
        /// <param name="generation">代际号；有效句柄必须非 <c>0</c>。</param>
        public AudioHandle(int id, int generation)
        {
            Id = id;
            Generation = generation;
        }

        #endregion

        #region Public API

        /// <summary>
        /// 本句柄是否是默认值（即 <c>default(AudioHandle)</c>）。
        /// <para>纯字段检查，不需要任何实现参与——因此它在任何生命周期阶段都是安全的。</para>
        /// <para>典型的默认值来源：池已满、加载失败、或在未初始化/已销毁的门面上调用探测型成员。
        /// 这类句柄上的 <see cref="Stop"/> 是空操作。</para>
        /// </summary>
        public bool IsDefault => Generation == 0;

        /// <summary>
        /// 停止这次播放。
        /// <para>转调 <see cref="AudioManager.Stop"/>。返回是否真的停掉了一个在播的声音；
        /// <c>default(AudioHandle)</c> 与非默认值句柄在门面已销毁时都安全返回 <c>false</c>（不抛）。</para>
        /// <para>重复调用是安全的：第一次停止后槽位代际即递增，后续调用自然落空。</para>
        /// </summary>
        /// <returns>真的停止了这次播放时返回 <c>true</c>。</returns>
        public bool Stop()
        {
            // 默认句柄直接短路：门面可能从未初始化，不必经它
            if (IsDefault)
                return false;

            return AudioManager.Stop(this);
        }

        /// <summary>
        /// 停止这次播放。等价于 <see cref="Stop"/>，供 <c>using</c> 使用。
        /// <para>与 <see cref="Stop"/> 一样对默认句柄与已销毁门面安全。</para>
        /// </summary>
        public void Dispose() => Stop();

        #endregion
    }
}
