using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
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
        internal Exception CloseException;
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

        public UniTask ReplaceDeclarativeAsync(UIPanelBase current, string targetAssetPath, int layer,
            CancellationToken cancellationToken)
        {
            ReplaceCalls++;
            LastReplaceTarget = targetAssetPath;
            return UniTask.CompletedTask;
        }

        public UniTask PopAsync(bool immediate, CancellationToken cancellationToken)
        {
            PopCalls++;
            return UniTask.CompletedTask;
        }

        public UniTask CloseAsync(UIPanelBase panel, bool immediate, CancellationToken cancellationToken)
        {
            CloseCalls++;
            LastClosedPanel = panel;
            if (CloseException != null)
                throw CloseException;
            return UniTask.CompletedTask;
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
            return UniTask.FromResult(DialogResult);
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

        internal static void InvokePoolRecycle(UIPanelBase panel)
        {
            MethodInfo method = typeof(UIPanelBase).GetMethod(
                "OnPoolRecycle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(panel, null);
        }

        internal static void Destroy(UnityEngine.Object obj)
        {
            if (obj != null)
                UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
