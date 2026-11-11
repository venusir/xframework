using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XReactive;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring.Tests
{
    internal sealed class FakeActionRuntime : IUIActionRuntime
    {
        internal UIDeclarativePanel DeclarativePanel;
        internal int OpenDeclarativeCalls;
        internal string LastDeclarativeAddress;
        internal int OpenCSharpCalls;
        internal string LastOpenAssetPath;
        internal int LastOpenLayer;
        internal int PushCSharpCalls;
        internal int PopCalls;
        internal int CloseCalls;
        internal UIPanelBase LastClosedPanel;
        internal int CloseAllCalls;
        internal int ShowTipCalls;
        internal string LastTipText;
        internal TipConfig LastTipConfig;
        internal int SetLayerVisibilityCalls;
        internal int SetLayerInteractiveCalls;
        internal string LastLayerCall;
        internal int ReplaceCalls;
        internal string LastReplaceTarget;
        internal UIDeclarativeDialogPanel DeclarativeDialog;
        internal int ShowDeclarativeDialogCalls;
        internal string LastDialogAddress;
        internal string LastDialogActionId;
        internal UIAuthoringDialogResult DialogResult =
            new UIAuthoringDialogResult(UIAuthoringDialogStatus.Selected, "confirm");
        internal UIDeclarativePopupPanel DeclarativePopup;
        internal int ShowDeclarativePopupCalls;
        internal string LastPopupAddress;
        internal RectTransform LastPopupAnchor;
        internal UIPopupDirection LastPopupDirection;
        internal UIPopupAlign LastPopupAlign;
        internal bool LastPopupCloseOnClickOutside;
        internal Exception CloseException;
        internal Exception DialogException;
        internal bool CloseSucceeds = true;
        internal bool ReplaceSucceeds = true;
        internal UniTaskCompletionSource Gate;

        public bool IsDeclarativePanelOpen => DeclarativePanel != null;

        public UIDeclarativePanel GetDeclarativePanel() => DeclarativePanel;

        public UniTask<UIDeclarativePanel> OpenDeclarativeAsync(string assetPath, int layer,
            CancellationToken cancellationToken)
        {
            OpenDeclarativeCalls++;
            LastDeclarativeAddress = assetPath;
            return UniTask.FromResult(DeclarativePanel);
        }

        public async UniTask<UIPanelBase> OpenCSharpAsync(string assetPath, int layer,
            CancellationToken cancellationToken)
        {
            OpenCSharpCalls++;
            LastOpenAssetPath = assetPath;
            LastOpenLayer = layer;
            if (Gate != null)
                await Gate.Task;
            return null;
        }

        public UniTask<UIPanelBase> PushCSharpAsync(string assetPath, int layer,
            CancellationToken cancellationToken)
        {
            PushCSharpCalls++;
            LastOpenAssetPath = assetPath;
            LastOpenLayer = layer;
            return UniTask.FromResult<UIPanelBase>(null);
        }

        public UniTask<bool> ReplaceDeclarativeAsync(UIPanelBase current, string targetAssetPath, int layer,
            CancellationToken cancellationToken)
        {
            ReplaceCalls++;
            LastReplaceTarget = targetAssetPath;
            return UniTask.FromResult(ReplaceSucceeds);
        }

        public UniTask PopAsync(bool immediate, CancellationToken cancellationToken)
        {
            PopCalls++;
            return UniTask.CompletedTask;
        }

        public UniTask<bool> CloseAsync(UIPanelBase panel, bool immediate, CancellationToken cancellationToken)
        {
            CloseCalls++;
            LastClosedPanel = panel;
            if (CloseException != null)
                throw CloseException;
            return UniTask.FromResult(CloseSucceeds);
        }

        public UniTask CloseAllAsync(bool immediate, CancellationToken cancellationToken)
        {
            CloseAllCalls++;
            return UniTask.CompletedTask;
        }

        public UniTask ShowTipAsync(string text, TipConfig config, CancellationToken cancellationToken)
        {
            ShowTipCalls++;
            LastTipText = text;
            LastTipConfig = config;
            return UniTask.CompletedTask;
        }

        public bool IsDeclarativeDialogOpen => DeclarativeDialog != null;

        public UIDeclarativeDialogPanel GetDeclarativeDialog() => DeclarativeDialog;

        public UniTask<UIAuthoringDialogResult> ShowDeclarativeDialogAsync(
            string assetPath, string actionId, CancellationToken cancellationToken)
        {
            ShowDeclarativeDialogCalls++;
            LastDialogAddress = assetPath;
            LastDialogActionId = actionId;
            if (DialogException != null)
                throw DialogException;
            return UniTask.FromResult(DialogResult);
        }

        public bool IsDeclarativePopupOpen => DeclarativePopup != null;

        public UIDeclarativePopupPanel GetDeclarativePopup() => DeclarativePopup;

        public UniTask<UIDeclarativePopupPanel> ShowDeclarativePopupAsync(RectTransform anchor,
            string assetPath, UIPopupDirection direction, UIPopupAlign align, bool closeOnClickOutside,
            CancellationToken cancellationToken)
        {
            ShowDeclarativePopupCalls++;
            LastPopupAnchor = anchor;
            LastPopupAddress = assetPath;
            LastPopupDirection = direction;
            LastPopupAlign = align;
            LastPopupCloseOnClickOutside = closeOnClickOutside;
            return UniTask.FromResult(DeclarativePopup);
        }

        public void SetLayerVisibility(int layer, bool visible)
        {
            SetLayerVisibilityCalls++;
            LastLayerCall = "visibility:" + layer + ":" + visible;
        }

        public void SetLayerInteractive(int layer, bool interactive)
        {
            SetLayerInteractiveCalls++;
            LastLayerCall = "interactive:" + layer + ":" + interactive;
        }
    }

    /// <summary>
    /// 数据源测试替身：键 → 响应式属性的字典；可选让每次解析抛异常，验证注册表的异常隔离。
    /// </summary>
    internal sealed class FakeDataProvider : IUIDataProvider
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);

        internal Exception ThrowOnTryGet;

        public bool TryGet<T>(string key, out IReactiveProperty<T> source)
        {
            if (ThrowOnTryGet != null)
                throw ThrowOnTryGet;

            source = null;
            if (!Values.TryGetValue(key, out object value))
                return false;

            if (value is IReactiveProperty<T> typed)
            {
                source = typed;
                return true;
            }

            return false;
        }
    }

    internal static class UIAuthoringTestFactory
    {
        internal static UIDeclarativePanel CreatePanel(string name = "Panel")
        {
            var go = new GameObject(name, typeof(RectTransform));
            return go.AddComponent<UIDeclarativePanel>();
        }

        internal static UIDeclarativeDialogPanel CreateDialogPanel(string name = "Dialog")
        {
            var go = new GameObject(name, typeof(RectTransform));
            return go.AddComponent<UIDeclarativeDialogPanel>();
        }

        internal static UIDeclarativePopupPanel CreatePopup(string name = "Popup")
        {
            var go = new GameObject(name, typeof(RectTransform));
            return go.AddComponent<UIDeclarativePopupPanel>();
        }

        internal static UIDeclarativeHudItem CreateHud(string name = "Hud")
        {
            var go = new GameObject(name, typeof(RectTransform));
            return go.AddComponent<UIDeclarativeHudItem>();
        }

        internal static UIPanelDefinition CreateDefinition(
            UIPanelDefinitionKind kind = UIPanelDefinitionKind.Panel)
        {
            var definition = ScriptableObject.CreateInstance<UIPanelDefinition>();
            definition.Kind = kind;
            return definition;
        }

        internal static Transform CreateChild(Transform parent, string name, params Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            for (int i = 0; i < components.Length; i++)
                go.AddComponent(components[i]);
            return go.transform;
        }

        internal static void SetAssetPath(UIPanelBase panel, string assetPath)
        {
            PropertyInfo property = typeof(UIViewBase).GetProperty(
                "AssetPath",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(panel, assetPath);
        }

        internal static void InvokePoolRecycle(UIViewBase view)
        {
            MethodInfo method = typeof(UIViewBase).GetMethod(
                "OnPoolRecycle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(view, null);
        }

        internal static void Destroy(UnityEngine.Object obj)
        {
            if (obj != null)
                UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
