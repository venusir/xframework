using System;

namespace XFramework.XTimer
{
    /// <summary>
    /// 定时器句柄。由 <see cref="TimerManager.After(float, Action, XUpdate.UpdateTimeMode, System.Threading.CancellationToken)"/>
    /// 或 <see cref="TimerManager.Every(float, Action, XUpdate.UpdateTimeMode, System.Threading.CancellationToken)"/> 返回，
    /// <see cref="Dispose"/> 时停表并释放槽位。
    /// <para>支持 <c>using</c> 语法，也支持手动 <see cref="Stop"/> / <see cref="Restart"/> / <see cref="Dispose"/>。</para>
    /// <para>零 GC：不可变值类型，只存三个整型，不存委托、不存对象引用；可自由复制与存入容器。</para>
    /// <para><b>句柄是可复制的值</b>：离开作用域不会停表——只有 <c>using</c> 结束、显式 <see cref="Dispose"/>
    /// 或 <see cref="Stop"/> 才会。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>为什么字段不公开、构造函数是 internal</b>（与 <c>AudioHandle</c> 相反、与 <c>LockHandle</c> 相同）：
    /// 本模块是纯静态服务，没有 <c>ITimerManager</c> 这类后端替换点，不存在需要自行造句柄的第三方实现者。
    /// 公开构造只会让使用方造出「看着有效、实则指向不存在槽位」的句柄。</para>
    /// <para><b>身份是三元组</b>：<c>Id</c> 只是槽位下标，而槽位会被复用——没有代际就无法让旧句柄失效。
    /// <c>default(TimerHandle)</c> 的代际为 <c>0</c>，被保留为无效标记，任何有效句柄的代际都不是 <c>0</c>。</para>
    /// </remarks>
    public readonly struct TimerHandle : IDisposable
    {
        #region Private Fields

        private readonly int _table;
        private readonly int _slot;
        private readonly int _generation;

        #endregion

        #region Constructors

        /// <summary>
        /// 构造一个句柄。
        /// <para><b>只有 <c>TimerCore</c> 造句柄</b>，故为 internal。</para>
        /// </summary>
        /// <param name="table">表标识。</param>
        /// <param name="slot">槽位下标。</param>
        /// <param name="generation">代际号；有效句柄必须非 <c>0</c>。</param>
        internal TimerHandle(int table, int slot, int generation)
        {
            _table = table;
            _slot = slot;
            _generation = generation;
        }

        #endregion

        #region Internal — Identity

        /// <summary>表标识。句柄的身份是三元组，核心按它找表。</summary>
        internal int TableId => _table;

        /// <summary>槽位下标。</summary>
        internal int Slot => _slot;

        /// <summary>代际号；<c>0</c> 是保留的无效标记。</summary>
        internal int Generation => _generation;

        #endregion

        #region Public API

        /// <summary>
        /// 本句柄是否是默认值（即 <c>default(TimerHandle)</c>）。
        /// <para>纯字段检查，不需要任何实现参与——因此它在任何生命周期阶段都是安全的，也是
        /// <see cref="Stop"/> / <see cref="Restart"/> / <see cref="Remaining"/> 对本句柄短路的依据。</para>
        /// <para>典型的默认值来源：创建时令牌已被取消、或 <see cref="TimerManager.CancelAll"/> 之后又被复制下来的旧句柄。</para>
        /// </summary>
        public bool IsDefault => _generation == 0;

        /// <summary>
        /// 本定时器当前是否<b>仍在计时</b>。
        /// <para><b>这是一次实时查询而非缓存字段</b>：本结构是不可变值类型，<see cref="Stop"/> / <see cref="Dispose"/>
        /// 无法改写自身状态，故答案只能向门面现取。</para>
        /// <para><b>被暂停的定时器仍算在计时</b>：<c>Time.timeScale = 0</c> 或 <c>UpdateManager.Pause()</c> 冻结的是
        /// 逻辑时间轴，不是定时器的存在——恢复后它会从暂停处续上（见模块 README 的「暂停与时间轴」）。</para>
        /// <para><b>令牌已取消的定时器立刻算作不在计时</b>（不等槽位被回收），因此本属性是「还会不会触发」的准确答案。</para>
        /// <para><see cref="Stop"/> 过的定时器为 <c>false</c>；<see cref="Restart"/> 可以让它重新为 <c>true</c>。</para>
        /// </summary>
        public bool IsActive => !IsDefault && TimerManager.IsActive(this);

        /// <summary>
        /// 距下一拍触发还剩多少秒（在该定时器所属的时间轴上度量）。
        /// <para>无效句柄、已停止、已释放、令牌已取消时返回 <c>0</c>。</para>
        /// <para>这是本模块存在的<b>首要理由</b>——<c>UniTask.Delay</c> 给不出这个答案，冷却读条、进度条分母都靠它。</para>
        /// </summary>
        public float Remaining => IsDefault ? 0f : TimerManager.Remaining(this);

        /// <summary>
        /// 停止计时，但<b>保留槽位</b>——句柄仍然有效，<see cref="Restart"/> 可以用原时长重新起算。
        /// <para>停止后 <see cref="Remaining"/> 归 <c>0</c>、<see cref="IsActive"/> 为 <c>false</c>。</para>
        /// <para><b>停止的定时器不占 CPU 但占一个槽位</b>：不再需要时请 <see cref="Dispose"/>，
        /// 否则它会一直占着（见模块 README 的「已知限制」）。</para>
        /// <para>重复调用是安全的；首次停止后句柄的计时状态即已改变，后续调用返回 <c>false</c>。</para>
        /// </summary>
        /// <returns>真的停掉了一个在计时的定时器时返回 <c>true</c>。</returns>
        public bool Stop() => !IsDefault && TimerManager.Stop(this);

        /// <summary>
        /// 用原时长重新起算（首拍 = 此刻 + 原时长）。
        /// <para>对<b>停止中</b>的定时器是「重新启动」；对<b>计时中</b>的定时器是「重置」——冷却类用法的核心动作：
        /// 技能释放后把冷却重置回满。</para>
        /// <para>一次性定时器触发后槽位即被释放，句柄随之失效——想保留重开能力请先 <see cref="Stop"/>。</para>
        /// <para>令牌仍有效：创建时传了 <c>CancellationToken</c> 的定时器，重开后依旧受它约束。</para>
        /// </summary>
        /// <returns>成功重新起算时返回 <c>true</c>；无效句柄、已释放、令牌已取消时返回 <c>false</c>。</returns>
        public bool Restart() => !IsDefault && TimerManager.Restart(this);

        /// <summary>
        /// 停表并<b>释放槽位</b>（比 <see cref="Stop"/> 更强）。句柄随之失效，之后
        /// <see cref="IsActive"/> 为 <c>false</c>、<see cref="Restart"/> 返回 <c>false</c>。
        /// <para>释放槽位是唯一会清空捕获的委托与状态的路径——不用了的定时器务必走这一步，
        /// 否则表会一直持有它们。</para>
        /// <para>对默认句柄、已释放句柄、以及令牌已取消但尚未被扫尾回收的定时器都是安全的空操作。</para>
        /// </summary>
        public void Dispose()
        {
            if (!IsDefault)
                TimerManager.Release(this);
        }

        #endregion
    }
}
