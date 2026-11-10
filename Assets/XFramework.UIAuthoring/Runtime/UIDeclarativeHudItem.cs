using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 HUD：根节点挂本组件，用 <see cref="UIPanelDefinition"/>（<see cref="UIPanelDefinitionKind.Hud"/>）
    /// 配置静态文本。
    /// <para>阶段 2b 只做文本/本地化，不绑定动作；HUD 跟随、屏幕后隐藏、目标丢失回收与池化全部由
    /// <see cref="UIHudItem"/> / 核心 HUD 管理器负责。关闭 HUD 用 <c>UIManager.HideHud(target)</c>。</para>
    /// <para>文本订阅登记在本视图的 <c>Track</c> 上，回池时由基类统一释放；本类不重写
    /// <c>OnPoolRecycle</c>，也不定义 <c>OnDestroy</c>（会遮住基类的直接销毁兜底）。</para>
    /// </summary>
    public sealed class UIDeclarativeHudItem : UIHudItem
    {
        [SerializeField] private UIPanelDefinition _definition;

        internal void SetDefinitionForTest(UIPanelDefinition definition)
        {
            _definition = definition;
        }

        internal UniTask OpenForTestAsync(object userData = null)
        {
            return OnOpenImpl(userData);
        }

        internal UniTask CloseForTestAsync()
        {
            return OnCloseImpl(true);
        }

        protected override async UniTask OnOpenImpl(object userData)
        {
            // 先让基类缓存相机/CanvasGroup 并关闭 Raycaster（HUD 默认不可交互）。
            await base.OnOpenImpl(userData);

            if (_definition == null)
                return;

            if (_definition.Kind != UIPanelDefinitionKind.Hud)
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Definition kind '{0}' does not match UIDeclarativeHudItem; text binding skipped.",
                    _definition.Kind);
                return;
            }

            try
            {
                var binder = new UIDeclarativeBinder(this, _definition);
                binder.BindTexts();
                binder.BindData();
            }
            catch (Exception ex)
            {
                LogManager.Error(UIAuthoringLog.Category, "Declarative HUD binding failed: {0}", ex);
            }
        }
    }
}
