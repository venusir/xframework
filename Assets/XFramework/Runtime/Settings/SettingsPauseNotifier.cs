using UnityEngine;

namespace XFramework.XSettings
{
    /// <summary>
    /// 切后台兜底的帧外宿主。
    /// <para><b>为什么必须有 MonoBehaviour：</b><c>Application</c> 的静态事件里没有 pause
    /// （只有 <c>quitting</c> / <c>wantsToQuit</c> / <c>focusChanged</c> / <c>lowMemory</c>），
    /// 而 <c>focusChanged</c> 并不等价于移动端的 <c>OnApplicationPause</c>——Unity 自己的文档
    /// 在「应用被切到后台」这个场景上指的就是后者。故只能由一个组件来接收。</para>
    /// <para><b>为什么不自持 GameObject 到进程结束：</b>仅在确有类型开启
    /// <see cref="SettingsOptions.SaveOnPause"/> 时创建，且最后一个这样的类型释放时销毁——
    /// 与「关闭自动保存时不注册任何帧回调」同一取舍：默认路径零开销、零残留。</para>
    /// </summary>
    internal sealed class SettingsPauseNotifier : MonoBehaviour
    {
        #region Private Fields

        private static SettingsPauseNotifier _instance;
        private static int _refCount;

        #endregion

        #region Internal

        /// <summary>当前是否存在宿主实例。供测试断言生命周期，不做他用。</summary>
        internal static SettingsPauseNotifier Instance => _instance;

        /// <summary>
        /// 申请宿主：首个调用者创建它，其余只增加计数。
        /// <para>用计数而非布尔是因为宿主被「开了该选项的每个设置类型」共享。</para>
        /// </summary>
        /// <summary>会话代际：每次会话复位递增；上一会话的 Release 携带旧代际，不得扣减新会话计数（R3-SET-004）。</summary>
        private static int _generation;

        internal static int Acquire()
        {
            _refCount++;

            // Unity 的「伪 null」在这里正好合用：宿主被销毁（如退出播放模式）后此判断仍为真，
            // 于是下次 Acquire 会重建，不会留下一个指向已销毁组件的引用
            if (_instance == null)
            {
                var host = new GameObject("[SettingsManager] Pause Notifier");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<SettingsPauseNotifier>();
            }

            return _generation;
        }

        /// <summary>
        /// 归还宿主：计数归零时销毁 GameObject。
        /// </summary>
        /// <param name="generation"><see cref="Acquire"/> 返回的代际；旧会话的归还直接忽略。</param>
        internal static void Release(int generation)
        {
            if (generation != _generation)
                return; // 上一会话的 Release 不得串到新会话（R3-SET-004）

            // 防御性钳制：Acquire/Release 由 _saveOnPause 快照成对控制，正常不会失衡，
            // 但计数为负会让后续 Release 永远销毁不掉宿主
            if (_refCount > 0)
                _refCount--;

            if (_refCount > 0 || _instance == null)
                return;

            Destroy(_instance.gameObject);
            _instance = null;
        }

        /// <summary>
        /// 会话边界复位：清掉计数，并销毁活着的宿主。
        /// <para><b>为什么必须清计数：</b>上一会话 <see cref="Acquire"/> 过的类型在会话边界上只被丢引用、
        /// 不会被 <c>Dispose</c>（与七门面「只丢引用不 Dispose」同形），那份计数于是永不归还——第二会话
        /// 再 <see cref="Acquire"/> 就变成 2，此后任何一次 <see cref="Release"/> 都归不了零，
        /// <c>DontDestroyOnLoad</c> 宿主从此永不销毁、计数逐会话累积。</para>
        /// <para>由 <see cref="SettingsManager.ResetForNewSession"/> 调用。<b>不给它自己挂
        /// <c>RuntimeInitializeOnLoadMethod</c></b>——它是门面的内部件，族清单只登记门面那一处。</para>
        /// </summary>
        internal static void ResetForNewSession()
        {
            // 宿主可能已被引擎随播放会话销毁（伪 null），故先判再销毁
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
            }

            _instance = null;
            _refCount = 0;
            _generation++;   // 使上一会话未归还的 Release 失效（R3-SET-004）
        }

        #endregion

        #region Unity Messages

        /// <summary>
        /// 只在<b>进入后台</b>时落盘；恢复前台没有新的丢失风险，再写一次纯属多余 IO。
        /// </summary>
        /// <param name="pauseStatus">true 表示正在进入后台。</param>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
                SettingsManager.FlushDirtyForPause();
        }

        #endregion
    }
}
