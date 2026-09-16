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
        /// <summary>生命周期回调顺序（如 "OnOpen" / "OnBlur" / "OnClose"）。</summary>
        public readonly List<string> Log = new List<string>();

        /// <summary>最近一次 OnOpen 收到的 userData。</summary>
        public object LastUserData;

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
    }
}
