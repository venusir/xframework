namespace XFramework.XUpdate
{

    /// <summary>
    /// 更新 LOD 档位，决定 <see cref="IUpdateable.OnUpdate(float)"/> 的调用频率。
    /// <para>档位越高，更新间隔越大，帧消耗越低。第 k 档的周期是 2^k 个<b>节拍格</b>，而一格
    /// 有多长取决于时机：变步长轴（Update / LateUpdate）按 60Hz 基准计，即约
    /// 17 / 33 / 67 / 133 / 267 / 533ms；固定步轴每步一格，即 1 / 2 / 4 / 8 / 16 / 32 个固定步。</para>
    /// <para>因此档位名刻意只表达序数、不表达具体周期——周期属于模块约定（见 <c>Update/README.md</c>
    /// 的分级表），这样调整节拍基准时不必再次改名。变步长轴上帧率低于约 30fps 时补格被上限截住，
    /// 周期会按 60/帧率拉长（宁可延长也不突发）。</para>
    /// </summary>
    public enum UpdateLOD
    {
        /// <summary>第 0 档：每帧更新（不切片）</summary>
        Tier0 = 0,

        /// <summary>第 1 档：约 33ms（固定步轴上为 2 个固定步）</summary>
        Tier1 = 1,

        /// <summary>第 2 档：约 67ms（固定步轴上为 4 个固定步）</summary>
        Tier2 = 2,

        /// <summary>第 3 档：约 133ms（固定步轴上为 8 个固定步）</summary>
        Tier3 = 3,

        /// <summary>第 4 档：约 267ms（固定步轴上为 16 个固定步）</summary>
        Tier4 = 4,

        /// <summary>第 5 档：约 533ms（固定步轴上为 32 个固定步）</summary>
        Tier5 = 5,

        /// <summary>最大档位标记，用于调度器内部推导数组大小。</summary>
        Max = Tier5,
    }

    /// <summary>
    /// 所有时机共用的生命周期契约。
    /// <para>启用/禁用由 <see cref="UpdateManager.Enable(IUpdateLifecycle)"/> 与
    /// <see cref="UpdateManager.Disable(IUpdateLifecycle)"/> 触发，禁用期间不会收到该时机的派发回调。</para>
    /// </summary>
    public interface IUpdateLifecycle
    {
        /// <summary>
        /// 对象被启用时调用。恢复派发前重置状态。
        /// </summary>
        void OnEnable();

        /// <summary>
        /// 对象被禁用时调用。清理派发中的临时状态。
        /// </summary>
        void OnDisable();
    }

    /// <summary>
    /// 可更新接口。<see cref="UpdateTiming.Update"/> 时机的派发契约，由 <see cref="UpdateManager"/> 统一调度。
    /// <para><see cref="OnUpdate(float, float)"/> 的返回值决定下一次派发所采用的 <see cref="UpdateLOD"/> 等级。</para>
    /// <para>经 <see cref="UpdateManager.Disable(IUpdateLifecycle)"/> / <see cref="UpdateManager.Enable(IUpdateLifecycle)"/>
    /// 控制启用与禁用，禁用期间不会收到 <see cref="OnUpdate"/> 调用。</para>
    /// </summary>
    public interface IUpdateable : IUpdateLifecycle
    {

        /// <summary>
        /// 执行更新并返回下一帧的 <see cref="UpdateLOD"/> 等级。
        /// </summary>
        /// <param name="deltaTime">距上次更新的时间差。</param>
        /// <param name="time">当前时间（<see cref="UnityEngine.Time.time"/>），可用于绝对时间计算。</param>
        /// <returns>下一帧的更新频率等级。</returns>
        UpdateLOD OnUpdate(float deltaTime, float time);
    }

    /// <summary>
    /// 固定步长更新接口。时机与 <c>MonoBehaviour.FixedUpdate</c> 一致：随 Unity 的固定步长走，
    /// <c>timeScale = 0</c> 时随之停摆。
    /// <para>适合与物理、确定性模拟相关的逻辑——它们需要固定的时间增量，而不是每帧变化的 delta。
    /// 这里的时间基准是 <c>Time.fixedTime</c>，因此 <see cref="UpdateLOD"/> 的档位在此是
    /// 「每 2^k 个<b>固定步</b>」（默认 0.02s 一步）：固定步长本就等长，不存在需要修正的漂移，
    /// 故这一轴刻意不参与变步长轴的 60Hz 节拍。</para>
    /// <para>没有时间轴参数：Unity 的固定步长本就随 <c>timeScale</c> 停摆，
    /// 不存在「暂停期间仍运行」的固定步语义。</para>
    /// </summary>
    public interface IFixedUpdateable : IUpdateLifecycle
    {
        /// <summary>
        /// 执行固定步长更新并返回下一次派发所采用的 <see cref="UpdateLOD"/> 等级。
        /// </summary>
        /// <param name="deltaTime">距上次派发的时间差（通常是若干个固定步的整数倍）。</param>
        /// <param name="fixedTime">当前固定步时间（<see cref="UnityEngine.Time.fixedTime"/>）。</param>
        /// <returns>下一次派发的更新频率等级。</returns>
        UpdateLOD OnFixedUpdate(float deltaTime, float fixedTime);
    }

    /// <summary>
    /// 可延迟更新接口。<see cref="UpdateTiming.LateUpdate"/> 时机的派发契约。
    /// <para>语义与 <see cref="IUpdateable"/> 相同，只是派发时机在 <c>MonoBehaviour.LateUpdate</c> 前后——
    /// 适合需要「本帧所有 Update 都已跑完」的逻辑，例如跟随移动的目标位置、相机跟随。</para>
    /// <para>生命周期（启用/禁用）与 <see cref="IUpdateable"/> 共用
    /// <see cref="IUpdateLifecycle"/>，故一个对象可以只实现本接口。</para>
    /// </summary>
    public interface ILateUpdateable : IUpdateLifecycle
    {
        /// <summary>
        /// 执行延迟更新并返回下一次派发所采用的 <see cref="UpdateLOD"/> 等级。
        /// </summary>
        /// <param name="deltaTime">距上次派发的时间差。</param>
        /// <param name="time">当前时间（<see cref="UnityEngine.Time.time"/>）。</param>
        /// <returns>下一次派发的更新频率等级。</returns>
        UpdateLOD OnLateUpdate(float deltaTime, float time);
    }

    /// <summary>
    /// 可选契约：让对象声明自己跑在哪条时间轴上。
    /// <para>不实现本接口的对象一律登记在 <see cref="UpdateTimeMode.Scaled"/> 轴上。
    /// 需要「暂停期间仍运行」的对象（暂停菜单、UI 动画、手柄振动到期）请声明
    /// <see cref="UpdateTimeMode.Unscaled"/>。</para>
    /// <para>轴在<b>注册时读取一次</b>，之后由调度器记住；中途改变声明不会自动迁移，
    /// 需要先注销再重新注册。</para>
    /// </summary>
    public interface IUpdateTimeMode
    {
        /// <summary>本对象所在的更新调度时间轴。</summary>
        UpdateTimeMode TimeMode { get; }
    }
}