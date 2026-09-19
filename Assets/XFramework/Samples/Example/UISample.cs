using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XMessage;
using XFramework.XUI;
using XFramework.XUI.Data;
using XFramework.XUI.View;

namespace XFramework.Example
{
    /// <summary>
    /// 展示 XFramework UI 模块的用法：面板开合与显示栈、模态遮罩的引用计数句柄、
    /// 生命周期归口的消息订阅、层级开关、查询 API、Tip 与 HUD。
    /// <para><b>前置</b>：场景里放一个 <see cref="UIRootNode"/>（或在本组件的 Inspector 上勾
    /// <c>initializeIfNeeded</c> 由它代劳）。面板预制体一律经 YooAsset 按地址加载，故
    /// <c>assetPath</c> 要换成你自己的地址——本示例只演示调用方式，不附带预制体。</para>
    /// <para>各 API 的完整语义见 <c>Runtime/UI/README.md</c>。</para>
    /// </summary>
    public class UISample : MonoBehaviour
    {
        #region Inspector

        [Header("面板资源地址（换成你自己的）")]
        [SerializeField] private string mainPanelPath = "ui/panels/main";
        [SerializeField] private string settingsPanelPath = "ui/panels/settings";

        [Header("UIRoot")]
        [Tooltip("场景里没有 UIRootNode 时，由本组件用它自己的 Transform 初始化 UIManager。")]
        [SerializeField] private bool initializeIfNeeded = true;

        #endregion

        #region Lifecycle

        private void Start()
        {
            if (initializeIfNeeded && !UIManager.IsInitialized)
            {
                // 通常交给场景里的 UIRootNode；这里只是让示例可以独立跑起来
                UIManager.Initialize(transform);
            }

            // 订阅归口：传 this（MonoBehaviour），本组件销毁时自动退订
            UIManager.Subscribe((PanelOpenedMessage msg) =>
                Debug.Log($"[UISample] opened: {msg.PanelType.Name}"), this);

            OpenMainAsync().Forget();
        }

        private void OnDestroy()
        {
            // UIManager 是全局的：只有本组件同时充当了根时才拆它，否则会拆掉别人在用的
            if (initializeIfNeeded && ReferenceEquals(UIManager.UIRoot, transform))
            {
                UIManager.Destroy();
            }
        }

        #endregion

        #region 面板与显示栈

        private async UniTaskVoid OpenMainAsync()
        {
            // OpenAsync 与 PushAsync 都会入栈；差别只在语义（Push 会先把当前栈顶失焦）
            var main = await UIManager.OpenAsync<UIPanelBase>(mainPanelPath, UILayers.Default);

            // 打开失败（被 Controller 拦下、资源加载失败、取消）时返回 null，不抛异常
            if (main == null)
                return;

            // 再打开一个已打开的面板 = 把它重新置顶并恢复焦点，不会重复创建
            await UIManager.PushAsync<UIPanelBase>(settingsPanelPath, UILayers.Popup);

            // 返回上一层面板（等价于 PopAsync，语义化命名，供返回键调用）
            if (UIManager.CanGoBack)
                await UIManager.GoBackAsync();
        }

        #endregion

        #region 模态遮罩

        /// <summary>
        /// 遮罩按引用计数：多个系统各自 ShowMask 时，先结束的那个不该把别人的遮罩一起关掉。
        /// <para>用 <c>using</c> 让句柄与作用域绑定；面板也可以作为 owner，随面板关闭自动释放。</para>
        /// </summary>
        private async UniTask<bool> ConfirmAsync(string message)
        {
            // 句柄是 readonly struct，Dispose 幂等
            using (UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, new Color(0f, 0f, 0f, 0.6f),
                       clickToClose: true)))
            {
                await UniTask.Delay(500);
            }

            return true;
        }

        #endregion

        #region 层级开关与查询

        /// <summary>整层显隐与整层禁交互。两者都记住期望值，之后新建的层容器也会照办。</summary>
        public void TogglePopupLayer(bool visible, bool interactive)
        {
            UIManager.SetLayerVisibility(UILayers.Popup, visible);
            UIManager.SetLayerInteractive(UILayers.Popup, interactive);
        }

        /// <summary>查询当前开着的面板。逐帧调用请用 <c>CopyPanels</c>（零分配，缓冲区自持）。</summary>
        public string DescribeOpenPanels(List<UIPanelBase> buffer)
        {
            int count = UIManager.CopyPanels(buffer);

            var top = UIManager.GetTopPanel();
            return $"open={count} top={(top != null ? top.GetType().Name : "(none)")} " +
                   $"mask={UIManager.IsMaskShowing}";
        }

        #endregion

        #region Tip 与 HUD

        /// <summary>Tip 由统一的每帧通路推进，故它同面板一样受档位与暂停约束。</summary>
        public void ShowDamageTip(int damage, Vector3 worldPos)
        {
            var config = new TipConfig
            {
                WorldPos = worldPos,
                Duration = 1.2f,
                FloatDistance = 60f,
            };

            UIManager.ShowTipAsync($"-{damage}", config).Forget();
        }

        /// <summary>世界空间 HUD：跟随 3D 目标；同一目标同时只有一个 HUD，重复附加会替换旧的。</summary>
        public async UniTask BindHealthBarAsync(Transform target, string hudAssetPath)
        {
            await UIManager.ShowHudAsync<UIHudItem>(target, hudAssetPath, new Vector2(0f, 80f));
        }

        public void UnbindHealthBar(Transform target) => UIManager.HideHud(target);

        #endregion
    }
}
