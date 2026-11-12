using System.IO;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using XFramework.Samples.Integration;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.XInput.Default;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration.Editor
{
    /// <summary>
    /// 一次性资产生成器：产出 Integration 示例的预制体、启动场景与输入配置资产。
    /// <para><b>为什么需要它</b>：<c>Samples~/</c> 对 Unity 不可见（AssetDatabase 不导入），
    /// 预制体与场景无法在示例目录里直接生成。流程是：把示例脚本与数据复制到
    /// <c>Assets/__XfwIntegrationGen__/</c>（保持 <c>.meta</c> GUID）→ 本类在临时目录里生成 →
    /// 由 <c>Tools/generate-integration-sample.ps1</c> 把产物移回 <c>Samples~/Integration/</c> → 删除临时目录。</para>
    /// <para>本文件不随包发布，也不参与 Unity 的常规编译；只在生成时被复制进临时工程。</para>
    /// </summary>
    public static class IntegrationAssetGenerator
    {
        #region Constants

        private const string TempRoot = "Assets/__XfwIntegrationGen__";
        private const string GeneratedRoot = TempRoot + "/Generated";
        private const string PrefabRoot = GeneratedRoot + "/Prefabs";
        private const string SceneRoot = GeneratedRoot + "/Scenes";

        private static Font _font;

        #endregion

        #region Entry Point

        /// <summary>生成全部示例资产。由 Unity batchmode 的 <c>-executeMethod</c> 调用。</summary>
        public static void Generate()
        {
            EnsureFolder(PrefabRoot);
            EnsureFolder(SceneRoot);
            AssetDatabase.Refresh();

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
                throw new System.InvalidOperationException("Builtin font 'LegacyRuntime.ttf' not found.");

            var languageTable = AssetDatabase.LoadAssetAtPath<TextAsset>(
                TempRoot + "/Data/Integration.Lang_zh_Hans.json");
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                TempRoot + "/Data/Integration.Actions.inputactions");
            if (languageTable == null || actions == null)
                throw new System.InvalidOperationException(
                    "Temp data assets are missing. Copy Data/*.json and Data/*.inputactions with their .meta first.");

            CreateInputOptions(actions);

            CreateMainMenuPrefab();
            CreateSettingsPrefab();
            CreateLanguagePrefab();
            CreateConfirmPrefab();
            CreateModeSelectPrefab();
            CreateGamePanelPrefab();
            CreateResultPrefab();
            CreatePausePrefab();
            CreateLoadingPrefab();

            CreateBootScene(languageTable);
            CreateGameScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Integration] Asset generation finished.");
        }

        #endregion

        #region Prefabs

        private static void CreateMainMenuPrefab()
        {
            var root = CreatePanelRoot<MainMenuPanel>("Integration.MainMenu");
            CreateText(root, "txt_title", "XFramework 接入示例", 40, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -90f), new Vector2(900f, 60f));
            CreateText(root, "txt_last", "last run", 18, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.8f),
                new Vector2(0f, -170f), new Vector2(900f, 30f));

            CreateButton(root, "btn_play", "开始游戏", new Vector2(0f, -260f), new Vector2(320f, 56f));
            CreateButton(root, "btn_settings", "设置", new Vector2(0f, -340f), new Vector2(320f, 56f));
            CreateButton(root, "btn_language", "语言", new Vector2(0f, -420f), new Vector2(320f, 56f));
            CreateButton(root, "btn_exit", "退出", new Vector2(0f, -500f), new Vector2(320f, 56f));

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.MainMenu.prefab");
        }

        private static void CreateSettingsPrefab()
        {
            var root = CreatePanelRoot<SettingsPanel>("Integration.Settings");
            CreateText(root, "txt_title", "设置", 36, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -60f), new Vector2(900f, 50f));

            CreateText(root, "txt_master", "主音量", 22, TextAnchor.MiddleLeft, Color.white,
                new Vector2(-260f, -150f), new Vector2(240f, 36f));
            CreateSlider(root, "sld_master", new Vector2(140f, -150f), new Vector2(480f, 36f));

            CreateText(root, "txt_bgm", "背景音乐", 22, TextAnchor.MiddleLeft, Color.white,
                new Vector2(-260f, -220f), new Vector2(240f, 36f));
            CreateSlider(root, "sld_bgm", new Vector2(140f, -220f), new Vector2(480f, 36f));

            CreateText(root, "txt_sfx", "音效", 22, TextAnchor.MiddleLeft, Color.white,
                new Vector2(-260f, -290f), new Vector2(240f, 36f));
            CreateSlider(root, "sld_sfx", new Vector2(140f, -290f), new Vector2(480f, 36f));

            CreateText(root, "txt_hints", "显示操作提示", 22, TextAnchor.MiddleLeft, Color.white,
                new Vector2(-260f, -370f), new Vector2(240f, 40f));
            CreateToggle(root, "tgl_hints", new Vector2(40f, -370f), new Vector2(40f, 40f));

            CreateText(root, "txt_status", "", 20, TextAnchor.MiddleCenter, new Color(0.4f, 0.9f, 0.5f),
                new Vector2(0f, -440f), new Vector2(900f, 30f));

            CreateButton(root, "btn_apply", "应用", new Vector2(-180f, -540f), new Vector2(260f, 56f));
            CreateButton(root, "btn_reset", "恢复默认", new Vector2(180f, -540f), new Vector2(260f, 56f));
            CreateButton(root, "btn_back", "返回", new Vector2(0f, -620f), new Vector2(260f, 56f));

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.Settings.prefab");
        }

        private static void CreateLanguagePrefab()
        {
            var root = CreatePanelRoot<LanguagePanel>("Integration.Language");
            CreateText(root, "txt_title", "语言", 36, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -80f), new Vector2(900f, 50f));
            CreateText(root, "txt_status", "", 20, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.8f),
                new Vector2(0f, -160f), new Vector2(900f, 30f));

            CreateButton(root, "btn_zh", "中文", new Vector2(0f, -240f), new Vector2(260f, 56f));
            CreateButton(root, "btn_en", "English", new Vector2(0f, -320f), new Vector2(260f, 56f));
            CreateButton(root, "btn_back", "返回", new Vector2(0f, -430f), new Vector2(260f, 56f));

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.Language.prefab");
        }

        private static void CreateConfirmPrefab()
        {
            var root = CreatePanelRoot<ConfirmDialog>("Integration.Confirm");
            var rt = (RectTransform)root;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(520f, 280f);
            rt.anchoredPosition = Vector2.zero;

            var bg = CreateImage(root, "img_bg", new Color(0.12f, 0.14f, 0.18f, 0.98f));
            Stretch(bg.rectTransform);

            CreateText(root, "txt_message", "确定要退出游戏吗？", 24, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -90f), new Vector2(460f, 80f), anchorAtTop: true);

            CreateButton(root, "btn_confirm", "确定", new Vector2(130f, -220f), new Vector2(180f, 52f),
                anchorAtTop: true);
            CreateButton(root, "btn_cancel", "取消", new Vector2(-130f, -220f), new Vector2(180f, 52f),
                anchorAtTop: true);

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.Confirm.prefab");
        }

        private static void CreateModeSelectPrefab()
        {
            var root = CreatePanelRoot<ModeSelectPanel>("Integration.ModeSelect");
            CreateText(root, "txt_title", "选择模式", 36, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -80f), new Vector2(900f, 50f));
            CreateButton(root, "btn_mode_tap", "点星", new Vector2(0f, -200f), new Vector2(360f, 64f));
            CreateButton(root, "btn_mode_odd", "找异色", new Vector2(0f, -290f), new Vector2(360f, 64f));
            CreateText(root, "txt_hint", "", 18, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.8f),
                new Vector2(0f, -390f), new Vector2(900f, 30f));
            CreateButton(root, "btn_back", "返回", new Vector2(0f, -500f), new Vector2(260f, 56f));

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.ModeSelect.prefab");
        }

        private static void CreateGamePanelPrefab()
        {
            var root = CreatePanelRoot<GamePanel>("Integration.GamePanel");
            CreateText(root, "txt_mode", "", 30, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -60f), new Vector2(900f, 44f));
            CreateText(root, "txt_score", "0", 26, TextAnchor.MiddleLeft, Color.white,
                new Vector2(-320f, -130f), new Vector2(240f, 40f));
            CreateText(root, "txt_time", "", 24, TextAnchor.MiddleRight, Color.white,
                new Vector2(320f, -130f), new Vector2(240f, 40f));

            var timeFill = CreateImage(root, "img_time_fill", new Color(0.3f, 0.6f, 1f, 1f));
            var timeRt = timeFill.rectTransform;
            timeRt.anchorMin = timeRt.anchorMax = new Vector2(0.5f, 1f);
            timeRt.pivot = new Vector2(0.5f, 1f);
            timeRt.sizeDelta = new Vector2(600f, 16f);
            timeRt.anchoredPosition = new Vector2(0f, -190f);
            timeFill.type = Image.Type.Filled;
            timeFill.fillMethod = Image.FillMethod.Horizontal;
            timeFill.fillOrigin = 0;
            timeFill.fillAmount = 1f;

            var x = new[] { -220f, 0f, 220f };
            var y = new[] { -310f, -430f };
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int index = row * 3 + col;
                    CreateButton(root, "btn_target_" + index, "★",
                        new Vector2(x[col], y[row]), new Vector2(140f, 100f));
                }
            }

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.GamePanel.prefab");
        }

        private static void CreateResultPrefab()
        {
            var root = CreatePanelRoot<ResultPanel>("Integration.Result");
            CreateText(root, "txt_title", "本局结束", 36, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -120f), new Vector2(900f, 50f));
            CreateText(root, "txt_score", "", 28, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -220f), new Vector2(900f, 40f));
            CreateText(root, "txt_best", "", 22, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.8f),
                new Vector2(0f, -270f), new Vector2(900f, 34f));
            CreateButton(root, "btn_retry", "再来一局", new Vector2(-140f, -400f), new Vector2(240f, 56f));
            CreateButton(root, "btn_menu", "返回主菜单", new Vector2(140f, -400f), new Vector2(240f, 56f));

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.Result.prefab");
        }

        private static void CreatePausePrefab()
        {
            var root = CreatePanelRoot<PausePanel>("Integration.Pause");
            CreateText(root, "txt_title", "已暂停", 36, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -100f), new Vector2(900f, 50f));
            CreateText(root, "txt_binding", "", 20, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.8f),
                new Vector2(0f, -160f), new Vector2(900f, 30f));
            CreateButton(root, "btn_resume", "继续", new Vector2(0f, -260f), new Vector2(300f, 56f));
            CreateButton(root, "btn_restart", "重开一局", new Vector2(0f, -340f), new Vector2(300f, 56f));
            CreateButton(root, "btn_menu", "返回主菜单", new Vector2(0f, -420f), new Vector2(300f, 56f));
            CreateButton(root, "btn_exit", "退出游戏", new Vector2(0f, -500f), new Vector2(300f, 56f));

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.Pause.prefab");
        }

        private static void CreateLoadingPrefab()
        {
            var root = CreatePanelRoot<LoadingPanel>("Integration.Loading");
            CreateText(root, "txt_title", "加载中", 32, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -200f), new Vector2(900f, 44f));
            CreateText(root, "txt_status", "", 20, TextAnchor.MiddleCenter, new Color(0.7f, 0.75f, 0.8f),
                new Vector2(0f, -270f), new Vector2(900f, 30f));

            var progress = CreateImage(root, "img_progress", new Color(0.3f, 0.6f, 1f, 1f));
            var rt = progress.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(600f, 20f);
            rt.anchoredPosition = new Vector2(0f, -330f);
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Horizontal;
            progress.fillAmount = 0f;

            SavePrefab(root.gameObject, PrefabRoot + "/Integration.Loading.prefab");
        }

        private static void CreateGameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 一个简单的 3D 背景：Additive 加载后由 Boot 场景的相机渲染，UI 在其上。
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backdrop.name = "GameBackdrop";
            backdrop.transform.position = new Vector3(0f, 0f, 5f);
            backdrop.transform.localScale = new Vector3(12f, 8f, 1f);

            var gameCameraGo = new GameObject("GameCamera",
                typeof(CinemachineCamera), typeof(CinemachineImpulseListener));
            var gameCamera = gameCameraGo.GetComponent<CinemachineCamera>();
            gameCameraGo.transform.position = new Vector3(0f, 0f, -8f);

            var resultCameraGo = new GameObject("ResultCamera",
                typeof(CinemachineCamera), typeof(CinemachineImpulseListener));
            var resultCamera = resultCameraGo.GetComponent<CinemachineCamera>();
            resultCameraGo.transform.position = new Vector3(0f, 0f, -10f);

            var controllerGo = new GameObject("GameSceneController", typeof(GameSceneController));
            var controller = controllerGo.GetComponent<GameSceneController>();
            var so = new SerializedObject(controller);
            so.FindProperty("_gameCamera").objectReferenceValue = gameCamera;
            so.FindProperty("_resultCamera").objectReferenceValue = resultCamera;
            so.FindProperty("_followTarget").objectReferenceValue = backdrop.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, SceneRoot + "/Integration.Game.unity");
        }

        #endregion

        #region Scene

        private static void CreateBootScene(TextAsset languageTable)
        {
            // 在场景创建时重新按路径加载输入配置：CreateAsset 之后的实例可能已被资产导入/域重载
            // 变成「假 null」，直接把它写进场景会序列化成 fileID: 0。
            var inputOptionsPath = GeneratedRoot + "/Integration.InputOptions.asset";
            AssetDatabase.ImportAsset(inputOptionsPath, ImportAssetOptions.ForceUpdate);
            var inputOptions = AssetDatabase.LoadAssetAtPath<InputSystemOptionsAsset>(inputOptionsPath);
            if (inputOptions == null)
                throw new System.InvalidOperationException(
                    "Integration.InputOptions.asset 未能加载，启动场景的 _inputOptions 会为空。");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            // Cinemachine 的输出端；Game 场景里的 vcam 由 CameraManager 注册/切换。
            cameraGo.AddComponent<CinemachineBrain>();

            var uiRootGo = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRootNode));
            var canvas = uiRootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = uiRootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            var inputModule = eventSystemGo.GetComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();

            var appFlowGo = new GameObject("AppFlow", typeof(AppFlowController));
            var bootScreenGo = new GameObject("BootScreen", typeof(BootScreen));
            var launcherGo = new GameObject("Launcher", typeof(IntegrationLauncher));

            var appFlow = appFlowGo.GetComponent<AppFlowController>();
            var launcher = launcherGo.GetComponent<IntegrationLauncher>();

            var launcherSo = new SerializedObject(launcher);
            launcherSo.FindProperty("_assetPackageName").stringValue = string.Empty;
            launcherSo.FindProperty("_assetPlayMode").intValue = (int)AssetPlayMode.EditorSimulate;
            launcherSo.FindProperty("_autoReclaimOnLowMemory").boolValue = true;
            launcherSo.FindProperty("_saveVersion").intValue = 1;
            launcherSo.FindProperty("_uiRoot").objectReferenceValue = uiRootGo.transform;
            launcherSo.FindProperty("_defaultLanguage").stringValue = "zh_Hans";
            launcherSo.FindProperty("_languageTable").objectReferenceValue = languageTable;
            launcherSo.FindProperty("_appFlow").objectReferenceValue = appFlow;
            launcherSo.ApplyModifiedPropertiesWithoutUndo();

            // _inputOptions 用反射直接写：SerializedObject 对继承自包程序集的 ScriptableObject 字段
            // 在该 batchmode 场景下会序列化成 fileID: 0（实测），直接写字段 + SetDirty 可稳定落盘。
            var inputOptionsField = typeof(DefaultGameLauncher).GetField("_inputOptions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (inputOptionsField == null)
                throw new System.InvalidOperationException("DefaultGameLauncher._inputOptions not found.");

            inputOptionsField.SetValue(launcher, inputOptions);
            EditorUtility.SetDirty(launcher);

            EditorSceneManager.SaveScene(scene, SceneRoot + "/Integration.Boot.unity");
        }

        #endregion

        #region Input Options

        private static void CreateInputOptions(InputActionAsset actions)
        {
            var options = ScriptableObject.CreateInstance<InputSystemOptionsAsset>();
            var so = new SerializedObject(options);
            so.FindProperty("_asset").objectReferenceValue = actions;
            so.FindProperty("_initialActionMap").stringValue = "Integration";
            so.ApplyModifiedPropertiesWithoutUndo();

            var path = GeneratedRoot + "/Integration.InputOptions.asset";
            AssetDatabase.CreateAsset(options, path);
            AssetDatabase.SaveAssets();
        }

        #endregion

        #region UI Builders

        private static RectTransform CreatePanelRoot<T>(string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var rt = go.GetComponent<RectTransform>();
            Stretch(rt);
            go.AddComponent<T>();
            return rt;
        }

        private static Text CreateText(Transform parent, string name, string text, int size, TextAnchor anchor,
            Color color, Vector2 position, Vector2 sizeDelta, bool anchorAtTop = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorAtTop ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 1f);
            rt.anchorMax = rt.anchorMin;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = position;

            var label = go.GetComponent<Text>();
            label.font = _font;
            label.text = text;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, string labelText, Vector2 position,
            Vector2 sizeDelta, bool anchorAtTop = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = position;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.18f, 0.32f, 0.55f, 0.95f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var label = CreateText(go.transform, "txt_label", labelText, 22, TextAnchor.MiddleCenter,
                Color.white, Vector2.zero, sizeDelta);
            Stretch(label.rectTransform);
            return button;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 position, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = position;

            var slider = go.AddComponent<Slider>();

            var background = CreateImage(go.transform, "Background", new Color(0.15f, 0.15f, 0.18f, 1f));
            Stretch(background.rectTransform);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(go.transform, false);
            fillArea.anchorMin = new Vector2(0f, 0.25f);
            fillArea.anchorMax = new Vector2(1f, 0.75f);
            fillArea.offsetMin = new Vector2(8f, 0f);
            fillArea.offsetMax = new Vector2(-8f, 0f);

            var fill = CreateImage(fillArea, "Fill", new Color(0.3f, 0.6f, 1f, 1f));
            Stretch(fill.rectTransform);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform))
                .GetComponent<RectTransform>();
            handleArea.SetParent(go.transform, false);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);

            var handle = CreateImage(handleArea, "Handle", Color.white);
            var handleRt = handle.rectTransform;
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(0f, 1f);
            handleRt.sizeDelta = new Vector2(20f, 0f);
            handleRt.anchoredPosition = Vector2.zero;

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            return slider;
        }

        private static Toggle CreateToggle(Transform parent, string name, Vector2 position, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Toggle));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = position;

            var background = go.GetComponent<Image>();
            background.color = new Color(0.15f, 0.15f, 0.18f, 1f);

            var check = CreateImage(go.transform, "Checkmark", new Color(0.3f, 0.85f, 0.45f, 1f));
            Stretch(check.rectTransform);
            check.rectTransform.offsetMin = new Vector2(6f, 6f);
            check.rectTransform.offsetMax = new Vector2(-6f, -6f);

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = check;
            toggle.isOn = true;
            return toggle;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        #endregion

        #region Helpers

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        #endregion
    }
}
