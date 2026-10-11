using UnityEngine;
using XFramework.XLog;
using XFramework.XTimer;
using XFramework.XUpdate;

namespace XFramework.Example
{
    /// <summary>
    /// 展示 XFramework Timer 模块的用法：冷却（可查询、可重开）、固定间隔、随对象销毁自动停表、
    /// 以及两条时间轴的选择。
    /// <para>把它挂到场景里的任意 GameObject 上即可运行。本模块<b>零配置</b>：不需要 Initialize，
    /// 进入播放时就已就绪。</para>
    /// </summary>
    public class TimerSample : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private float _skillCooldown = 8f;
        [SerializeField] private float _tickInterval = 1f;

        #endregion

        #region Private Fields

        private TimerHandle _cooldown;
        private TimerHandle _tick;
        private TimerHandle _unscaled;
        private bool _ready = true;
        private int _tickCount;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            // 1. 冷却：句柄留着反复重置。Every 的首拍隔一个间隔（不是立刻），
            //    而「冷却好了没有」用回调置位、用 Remaining 读进度——这就是 UniTask.Delay 给不了的那一条
            _cooldown = TimerManager.Every(_skillCooldown, () => _ready = true);

            // 2. 固定间隔：第 k 拍恒在「锚点 + k × 间隔」上，不随帧率漂移；
            //    卡顿一秒也只补发一次，不会连发十条
            _tick = TimerManager.Every(_tickInterval, () => _tickCount++);

            // 3. 一次性延时 + 生命周期绑定：直接传 destroyCancellationToken，本对象销毁即定时器消失。
            //    static lambda 配 Action<TState> 重载是零闭包写法——注册期一个字节都不分配
            TimerManager.After(1.5f, static self => self.OnWarmupDone(), this,
                cancellationToken: destroyCancellationToken);

            // 4. 时间轴：暂停菜单、界面动画这类「暂停期间仍要走完」的倒计时用 Unscaled；
            //    其余一律用默认的 Scaled，跟着 timeScale 走
            _unscaled = TimerManager.After(3f,
                () => LogManager.Info(SampleLogCategories.Example, "这条不受 timeScale 影响"),
                UpdateTimeMode.Unscaled);
        }

        private void OnDestroy()
        {
            // 5. 句柄是可复制的值类型，离开作用域不会停表——不用了必须显式释放槽位，
            //    否则表会一直持有回调捕获的东西
            _cooldown.Dispose();
            _tick.Dispose();
            _unscaled.Dispose();

            // 设了 destroyCancellationToken 的那条不必管，它已经跟着本次销毁一起失效了
        }

        private void OnWarmupDone()
        {
            LogManager.Info(SampleLogCategories.Example, "预热结束");
        }

        #endregion

        #region 演示：接到 UI 上的几个入口

        /// <summary>技能按钮：冷却没好就按不动。</summary>
        /// <returns>本次是否真的放出了技能。</returns>
        public bool UseSkill()
        {
            if (!_ready)
                return false;

            _ready = false;
            _cooldown.Restart(); // 重置回满：下一次触发在 _skillCooldown 秒之后
            LogManager.Info(SampleLogCategories.Example, "技能释放");
            return true;
        }

        /// <summary>冷却读条的分母与分子——UI 每帧读它即可，不需要缓存任何东西。</summary>
        public float CooldownRemaining => _ready ? 0f : _cooldown.Remaining;

        /// <summary>冷却进度 0~1，可直接喂给进度条。</summary>
        public float CooldownProgress => _ready ? 1f : 1f - _cooldown.Remaining / _skillCooldown;

        /// <summary>调试用：固定间隔定时器已经跳了多少拍。</summary>
        public int TickCount => _tickCount;

        /// <summary>调试用：当前正在计时的定时器数量（两轴合计）。</summary>
        public int ActiveCount => TimerManager.ActiveCount;

        /// <summary>清场按钮/切场景：一次停净本模块的全部定时器。</summary>
        public void CancelEverything()
        {
            TimerManager.CancelAll();
        }

        #endregion

        #region 与相邻模块接线（可选，故只留成注释）

        // Timer **只依赖 Update**，不依赖任何可选模块。几条常见接线：
        //
        //   · 暂停：不要自己做。UpdateManager.Pause() 会连带冻住本模块所有 Scaled 定时器，
        //     而 Unscaled 的照常走——与 Update 的口径完全一致，使用方不必再学一套。
        //
        //   · 面板/实体销毁：把它的 destroyCancellationToken 传进每个创建调用（见上面第 3 条），
        //     比在 OnDestroy 里逐个 Dispose 更不容易漏。
        //
        //   · 实体级的批量清理：本模块没有「按 owner 分组」的 API，一个对象的多个定时器请各存句柄；
        //     若确实需要分组，那通常说明该实体的生命周期本身该由使用方的架构管，而不是让框架替它决定。

        #endregion
    }
}
