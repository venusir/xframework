namespace XFramework.XUpdate
{

    /// <summary>
    /// 更新 LOD 等级，决定 <see cref="IUpdateable.OnUpdate(float)"/> 的调用频率。
    /// <para>等级越高，更新间隔越大，帧消耗越低。</para>
    /// </summary>
    public enum UpdateLOD
    {
        /// <summary>每帧更新（帧间隔 = 1 帧）</summary>
        Frame1 = 0,

        /// <summary>每 2 帧更新一次</summary>
        Frame2 = 1,

        /// <summary>每 4 帧更新一次</summary>
        Frame4 = 2,

        /// <summary>每 8 帧更新一次</summary>
        Frame8 = 3,

        /// <summary>每 16 帧更新一次</summary>
        Frame16 = 4,

        /// <summary>每 32 帧更新一次</summary>
        Frame32 = 5,

        /// <summary>最大 LOD 等级标记，用于调度器内部推导数组大小。</summary>
        Max = Frame32,
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