namespace XFramework.XUpdate
{

    /// <summary>
    /// 更新档位，决定 <see cref="IUpdateable.OnUpdate(float, float)"/> 的调用频率。
    /// <para>档位越高，更新间隔越大，帧消耗越低。第 k 档的周期是 2^k 个<b>节拍格</b>，而一格
    /// 有多长取决于时机：变步长轴（Update / LateUpdate）按 60Hz 基准计，Tier1~Tier7 依次约为
    /// 33 / 67 / 133 / 267 / 533 / 1067 / 2133ms（Tier0 为每帧）；固定步轴每步一格，第 k 档即
    /// 2^k 个固定步。完整分级表见 <c>Update/README.md</c>。</para>
    /// <para>因此档位名刻意只表达序数、不表达具体周期——周期属于模块约定，这样调整节拍基准时
    /// 不必再次改名。变步长轴上帧长超过 50ms（低于约 20fps）时补格被上限截住，周期会随帧率
    /// 线性拉长（宁可延长也不突发）；帧长达到 2 格（约 30fps 及以下）时 <c>Tier1</c> 与
    /// <c>Tier0</c> 同频——同一帧内不重复访问同一档位，详见 <c>Update/README.md</c>。</para>
    /// </summary>
    public enum UpdateTier
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

        /// <summary>第 6 档：约 1067ms（固定步轴上为 64 个固定步）</summary>
        Tier6 = 6,

        /// <summary>第 7 档：约 2133ms（固定步轴上为 128 个固定步）</summary>
        Tier7 = 7,

        /// <summary>最大档位标记，用于调度器内部推导数组大小。</summary>
        Max = Tier7,
    }

    /// <summary>
    /// 所有时机共用的生命周期契约。
    /// <para><b>这不是对象生命周期事件，而是「进入 / 离开派发集合」的边沿通知</b>，且
    /// <b>每套调度器各算一份</b>：注册与 <see cref="UpdateManager.Enable(IUpdateLifecycle)"/> 让对象进入
    /// 集合，<see cref="UpdateManager.Disable(IUpdateLifecycle)"/> 让它离开（禁用期间不会收到该时机的派发回调）。因此：</para>
    /// <list type="bullet">
    /// <item>每套调度器上两者<b>严格交替、且以 <see cref="OnEnable"/> 起头</b>——对象一注册就会先收到一次
    /// 启用通知，不会出现「没有配对的 <see cref="OnDisable"/>」</item>
    /// <item>同时注册在多个时机上的对象，调用次数等于它注册的<b>时机数</b>：一次
    /// <see cref="UpdateManager.Disable(IUpdateLifecycle)"/> 会收到 N 次 <see cref="OnDisable"/>。
    /// 想知道「是否已完全停用」的节点请按此时机数记账（计数归零即完全停用）</item>
    /// <item><see cref="UpdateManager.Unregister(IUpdateLifecycle)"/> 与 <c>UpdateManager.Clear()</c>
    /// <b>不宣告</b> <see cref="OnDisable"/>——那是「停止管理」而不是「暂停」，对象自己的销毁由调用方负责</item>
    /// </list>
    /// <para><b>写法建议</b>：一次性订阅与初始化放在注册之前的代码里（<see cref="OnEnable"/> 由注册同步宣告，
    /// 在构造函数里注册会让它在构造未完成、字段尚未赋值时被回调）；把这一对回调当成<b>每时机的暂停 / 恢复</b>，
    /// 清理写成幂等的。</para>
    /// </summary>
    public interface IUpdateLifecycle
    {
        /// <summary>
        /// 对象进入派发集合时调用：注册新入，或被 <see cref="UpdateManager.Enable(IUpdateLifecycle)"/> 重新启用。
        /// <para>每套调度器各一次，且总是排在对应的 <see cref="OnDisable"/> 之前。</para>
        /// </summary>
        void OnEnable();

        /// <summary>
        /// 对象离开派发集合时调用（<see cref="UpdateManager.Disable(IUpdateLifecycle)"/>）。
        /// <para>每套调度器各一次；<see cref="UpdateManager.Unregister(IUpdateLifecycle)"/> 与
        /// <c>Clear()</c> 不触发本回调。</para>
        /// </summary>
        void OnDisable();
    }

    /// <summary>
    /// 可更新接口。<see cref="UpdateTiming.Update"/> 时机的派发契约，由 <see cref="UpdateManager"/> 统一调度。
    /// <para><see cref="OnUpdate(float, float)"/> 的返回值决定下一次派发所采用的 <see cref="UpdateTier"/> 等级。</para>
    /// <para>经 <see cref="UpdateManager.Disable(IUpdateLifecycle)"/> / <see cref="UpdateManager.Enable(IUpdateLifecycle)"/>
    /// 控制启用与禁用，禁用期间不会收到 <see cref="OnUpdate"/> 调用。</para>
    /// </summary>
    public interface IUpdateable : IUpdateLifecycle
    {

        /// <summary>
        /// 执行更新并返回下一帧的 <see cref="UpdateTier"/> 等级。
        /// </summary>
        /// <param name="deltaTime">距上次更新的时间差。</param>
        /// <param name="time">当前时间（<see cref="UnityEngine.Time.time"/>），可用于绝对时间计算。</param>
        /// <returns>下一帧的更新频率等级——这是<b>运行时自适应</b>的通道；静态档位请在注册时用
        /// <c>initialTier</c> 声明（两者的分工见 <c>Update/README.md</c> 的「档位由谁决定」）。</returns>
        UpdateTier OnUpdate(float deltaTime, float time);
    }

    /// <summary>
    /// 固定步长更新接口。时机与 <c>MonoBehaviour.FixedUpdate</c> 一致：随 Unity 的固定步长走，
    /// <c>timeScale = 0</c> 时随之停摆。
    /// <para>适合与物理、确定性模拟相关的逻辑——它们需要固定的时间增量，而不是每帧变化的 delta。
    /// 这里的时间基准是 <c>Time.fixedTime</c>，因此 <see cref="UpdateTier"/> 的档位在此是
    /// 「每 2^k 个<b>固定步</b>」（默认 0.02s 一步）：固定步长本就等长，不存在需要修正的漂移，
    /// 故这一轴刻意不参与变步长轴的 60Hz 节拍。</para>
    /// <para><b>本轴的档位是「仿真频率」而非「采样频率」——降档后 deltaTime 仍然恒定</b>：
    /// Tier k 的节点每 2^k 步被派发一次，每次拿到的 <c>deltaTime</c> 恒为
    /// <c>2^k × Time.fixedDeltaTime</c>（档位不变则增量不变），即该子系统的固定速率是物理速率的
    /// 1/2^k。要的正是这件事时就用档位（经济结算 1Hz、AI 决策 12.5Hz）；但它<b>不减少物理成本</b>
    /// ——Unity 的物理照旧每步跑——所以它不是帧预算旋钮，摊帧预算请用变步长轴。直接驱动物理的
    /// 对象（写刚体速度/位置的控制环）也不宜降档：控制频率降到 1/2^k，却仍作用在每步积分的物理上。</para>
    /// <para>没有时间轴参数：Unity 的固定步长本就随 <c>timeScale</c> 停摆，
    /// 不存在「暂停期间仍运行」的固定步语义。</para>
    /// </summary>
    public interface IFixedUpdateable : IUpdateLifecycle
    {
        /// <summary>
        /// 执行固定步长更新并返回下一次派发所采用的 <see cref="UpdateTier"/> 等级。
        /// </summary>
        /// <param name="deltaTime">距上次派发的时间差，恒为 <c>2^k × Time.fixedDeltaTime</c>
        /// （k 即本节点当前的 <see cref="UpdateTier"/> 等级）——档位不变则它是不变的固定增量，
        /// 而不是「若干个固定步的整数倍」这种随派发漂移的量。注册/重新启用后的首次派发按
        /// 锚定规则记 0（见 <c>Update/README.md</c>）。</param>
        /// <param name="fixedTime">当前固定步时间（<see cref="UnityEngine.Time.fixedTime"/>）。</param>
        /// <returns>下一次派发的更新频率等级（本轴上即仿真频率）——静态档位请在注册时用
        /// <c>initialTier</c> 声明。</returns>
        UpdateTier OnFixedUpdate(float deltaTime, float fixedTime);
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
        /// 执行延迟更新并返回下一次派发所采用的 <see cref="UpdateTier"/> 等级。
        /// </summary>
        /// <param name="deltaTime">距上次派发的时间差。</param>
        /// <param name="time">当前时间（<see cref="UnityEngine.Time.time"/>）。</param>
        /// <returns>下一次派发的更新频率等级——这是<b>运行时自适应</b>的通道；静态档位请在注册时用
        /// <c>initialTier</c> 声明。</returns>
        UpdateTier OnLateUpdate(float deltaTime, float time);
    }
}