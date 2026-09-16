using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using XFramework.XUI.View;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 测试用面板：记录生命周期回调顺序，供各 fixture 断言开合、焦点、暂停等语义。
    /// </summary>
    public class FakePanel : UIPanelBase
    {
        #region Lifecycle Log

        /// <summary>生命周期回调顺序（如 "OnOpen" / "OnBlur" / "OnClose"）。</summary>
        public readonly List<string> Log = new List<string>();

        /// <summary>最近一次 OnOpen 收到的 userData。</summary>
        public object LastUserData;

        /// <summary>日志中是否出现过指定回调。</summary>
        public bool Logged(string entry) => Log.Contains(entry);

        #endregion

        protected override UniTask OnOpen(object userData)
        {
            Log.Add("OnOpen");
            LastUserData = userData;
            return UniTask.CompletedTask;
        }

        protected override UniTask OnClose()
        {
            Log.Add("OnClose");
            return UniTask.CompletedTask;
        }

        // 覆写 protected internal 成员必须沿用同一修饰符（CS0507 实测），不能降为 protected
        protected internal override void OnFocus()
        {
            Log.Add("OnFocus");
            base.OnFocus();
        }

        protected internal override void OnBlur()
        {
            Log.Add("OnBlur");
            base.OnBlur();
        }

        protected internal override void OnPause()
        {
            Log.Add("OnPause");
            base.OnPause();
        }

        protected internal override void OnResume()
        {
            Log.Add("OnResume");
            base.OnResume();
        }
    }

    /// <summary>第二个测试面板类型。导航测试需要多个类型才能构成多层面板栈。</summary>
    public class FakePanelB : FakePanel
    {
    }

    /// <summary>第三个测试面板类型。</summary>
    public class FakePanelC : FakePanel
    {
    }

    /// <summary>
    /// 在 <c>OnOpen</c> 里请求关闭自己的面板。
    /// <para>用于验证「OnOpen 期间可关闭自身」：关闭请求应被延迟到 OnOpen 返回之后执行，
    /// 且此时 <c>IsOpen</c> 必须已经为 true，否则请求会被守卫静默丢弃。</para>
    /// </summary>
    public class SelfClosingOnOpenPanel : FakePanel
    {
        protected override async UniTask OnOpen(object userData)
        {
            await base.OnOpen(userData);   // Log: "OnOpen"

            CloseSelfAsync().Forget();
            Log.Add("OnOpenReturned");     // 必须排在 "OnClose" 之前
        }
    }

    /// <summary>
    /// 在 <c>OnOpen</c> 里探测自身状态的面板，用于锁定「打开期间 IsOpen 即为 true」。
    /// </summary>
    public class StateProbePanel : FakePanel
    {
        public bool IsOpenDuringOnOpen { get; private set; }
        public bool IsOpeningDuringOnOpen { get; private set; }

        protected override async UniTask OnOpen(object userData)
        {
            await base.OnOpen(userData);

            IsOpenDuringOnOpen = IsOpen;
            IsOpeningDuringOnOpen = IsOpening;
        }
    }

    /// <summary>
    /// 在 <c>OnOpen</c> 里抛异常的面板，用于验证打开失败时实例被回滚回收、不留孤儿。
    /// </summary>
    public class ThrowingPanel : FakePanel
    {
        protected override UniTask OnOpen(object userData)
        {
            throw new System.InvalidOperationException("[ThrowingPanel] 故意在 OnOpen 里抛异常");
        }
    }

    /// <summary>
    /// 记录每次 <c>OnUpdate</c> 收到的实测参数，用于验证派发方给出的 <c>deltaTime</c>/<c>time</c>。
    /// </summary>
    public class UpdateRecordingPanel : FakePanel
    {
        public int UpdateCount { get; private set; }
        public float LastDeltaTime { get; private set; }
        public float LastTime { get; private set; }

        /// <summary>语言切换回调次数。</summary>
        public int LanguageChangedCount { get; private set; }

        protected internal override void OnLanguageChanged(string lang)
        {
            LanguageChangedCount++;
        }

        protected internal override void OnUpdate(float deltaTime, float time)
        {
            UpdateCount++;
            LastDeltaTime = deltaTime;
            LastTime = time;
        }
    }

    /// <summary>
    /// 在自己的 <see cref="UIViewBase.OnUpdate"/> 里关闭自己的面板。
    /// <para>这是「遍历中改集合」崩溃的触发场景：默认控制器下的关闭路径同步走完，
    /// 若驱动方直接遍历活动面板集合就会当场抛 InvalidOperationException。</para>
    /// </summary>
    public class SelfClosingPanel : FakePanel
    {
        /// <summary>OnUpdate 是否已被驱动过（避免重复触发）。</summary>
        public bool WasUpdated { get; private set; }

        protected internal override void OnUpdate(float deltaTime, float time)
        {
            if (WasUpdated)
                return;

            WasUpdated = true;
            CloseSelfAsync().Forget();
        }
    }
}
