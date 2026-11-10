using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;
using XFramework.XMessage;
using XFramework.XReactive;
using XFramework.XUI;
using XFramework.XUI.Authoring;

namespace XFramework.Example
{
    /// <summary>
    /// 声明式 UI 示例：打开 DemoPanel，按钮动作来自定义资产
    /// （<c>ShowDialog</c> / <c>Custom</c> / <c>CloseSelf</c>），文本/颜色/滑条/开关来自
    /// <see cref="UIAuthoringDemoDataProvider"/>；本脚本只保留注册数据源、一次打开调用与消息订阅。
    /// <para><b>前置</b>：场景里有 <c>UIRootNode</c>；DemoPanel / DemoDialog / DemoHud 预制体与三个定义资产
    /// 已加入消费方 YooAsset 主资源采集（面板地址与 <c>panelAddress</c> 一致，对话框地址与定义里
    /// <c>ShowDialog.AssetPath</c> 一致，默认为 <c>ui/dialogs/demo</c>）。</para>
    /// <para>完整说明见包 README 的「模板与示例」。</para>
    /// </summary>
    public class UIAuthoringDemo : MonoBehaviour
    {
        private static readonly LogCategory DemoLog = LogCategory.Get("UIAuthoringDemo");

        [Header("面板资源地址（换成你自己的）")]
        [SerializeField] private string panelAddress = "ui/panels/demo";

        [Header("HUD（可选：把场景里的 3D 目标拖进来）")]
        [SerializeField] private Transform hudTarget;
        [SerializeField] private string hudAddress = "ui/huds/demo";

        private IDisposable _customSubscription;
        private IDisposable _dialogSubscription;
        private UIAuthoringDemoDataProvider _dataProvider;
        private float _elapsed;
        private float _progressDirection = 1f;
        private float _hpDirection = -1f;

        private void Start()
        {
            // 数据源是全局接缝：先注册，再打开/附加任何声明式视图。
            _dataProvider = new UIAuthoringDemoDataProvider();
            UIDataProviderRegistry.Register(_dataProvider);

            // 动作消息不带面板引用；用返回的句柄控制生命周期
            _customSubscription = MessageManager.Subscribe<string, UIAuthoringActionMessage>(
                "demo.custom", OnCustomAction);
            _dialogSubscription = MessageManager.Subscribe<string, UIAuthoringDialogResultMessage>(
                "demo.confirm", OnDialogResult);

            OpenAsync().Forget();
            AttachHudAsync().Forget();
        }

        private void Update()
        {
            if (_dataProvider == null)
                return;

            // 每 0.1 秒推一次值，演示「数据变化 → 视图自动刷新」；滑条/开关仍可由玩家操作写回。
            _elapsed += Time.deltaTime;
            if (_elapsed < 0.1f)
                return;
            _elapsed = 0f;

            Push(_dataProvider.GetProperty<float>(UIAuthoringDemoDataProvider.ProgressKey),
                ref _progressDirection, 0.05f);
            Push(_dataProvider.GetProperty<float>(UIAuthoringDemoDataProvider.HpKey),
                ref _hpDirection, 0.1f);
        }

        private static void Push(ReactiveProperty<float> property, ref float direction, float step)
        {
            if (property == null)
                return;

            float next = property.Value + step * direction;
            if (next >= 1f)
            {
                next = 1f;
                direction = -1f;
            }
            else if (next <= 0f)
            {
                next = 0f;
                direction = 1f;
            }

            property.Value = next;
        }

        private void OnDestroy()
        {
            _customSubscription?.Dispose();
            _dialogSubscription?.Dispose();

            UIDataProviderRegistry.Unregister(_dataProvider);
            _dataProvider?.Dispose();
            _dataProvider = null;

            // HUD 的关闭入口只有一个：按目标调用 HideHud（面板的关闭入口不适用）。
            if (hudTarget != null && UIManager.IsInitialized)
                UIManager.HideHud(hudTarget);
        }

        private async UniTaskVoid OpenAsync()
        {
            UIDeclarativePanel panel = await UIAuthoringNavigation.OpenAsync(panelAddress);
            if (panel == null)
            {
                // 资源加载失败 / 被控制器拦下时返回 null，不抛异常
                LogManager.Warning(DemoLog, "打开声明式面板失败：{0}", panelAddress);
                return;
            }

            LogManager.Info(DemoLog, "已打开声明式面板：{0}", panel.AssetPath);
        }

        private async UniTaskVoid AttachHudAsync()
        {
            if (hudTarget == null || string.IsNullOrEmpty(hudAddress))
                return;

            // HUD 没有 userData；它跟随 hudTarget，目标丢失/CloseAllAsync 时由核心自动回收。
            UIDeclarativeHudItem hud = await UIManager.ShowHudAsync<UIDeclarativeHudItem>(
                hudTarget, hudAddress);
            if (hud == null)
            {
                LogManager.Warning(DemoLog, "附加声明式 HUD 失败：{0}", hudAddress);
                return;
            }

            LogManager.Info(DemoLog, "已附加声明式 HUD：{0}", hudAddress);
        }

        private static void OnCustomAction(UIAuthoringActionMessage message)
        {
            // 领域逻辑放这里；需要面板上下文时由项目自行获取，不要缓存消息里的东西
            LogManager.Info(DemoLog, "Custom 动作：{0} / {1}", message.ActionId, message.Payload);
        }

        private static void OnDialogResult(UIAuthoringDialogResultMessage message)
        {
            switch (message.Status)
            {
                case UIAuthoringDialogStatus.Selected:
                    LogManager.Info(DemoLog, "对话框结果：{0}", message.ResultId);
                    break;
                case UIAuthoringDialogStatus.Canceled:
                    LogManager.Info(DemoLog, "对话框被取消");
                    break;
                default:
                    LogManager.Warning(DemoLog, "对话框没有成功打开");
                    break;
            }
        }
    }
}
