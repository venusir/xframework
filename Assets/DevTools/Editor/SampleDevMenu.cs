using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace XFramework.DevTools
{
    /// <summary>
    /// 仓库内开发菜单：把 <c>Samples~/</c> 里的示例复制到 <c>Assets/Samples/</c>（Unity 可见），
    /// 按需补齐 YooAsset 采集，并打开场景。
    /// <para><b>为什么需要它</b>：<c>Samples~/</c> 带 <c>~</c>，AssetDatabase 完全不索引；本仓开发时
    /// 场景打不开、脚本不编译、YooAsset 采集不到。复制到 <c>Assets/Samples/</c> 是仓内唯一可行的
    /// 「直接按 Play」路径（该目录已在 <c>.gitignore</c> 中）。</para>
    /// <para><b>只存在于本仓</b>：<c>Assets/DevTools/</c> 在包目录之外，不随 UPM 包发布。</para>
    /// </summary>
    public static class SampleDevMenu
    {
        #region Constants

        private const string IntegrationScene = "Assets/Samples/Integration/Scenes/Integration.Boot.unity";
        private const string PendingSceneKey = "Xfw.SampleDevMenu.PendingScene";
        private const string PendingPlayKey = "Xfw.SampleDevMenu.PendingPlay";
        private const string PendingCollectorKey = "Xfw.SampleDevMenu.PendingCollector";
        private const string PendingAttemptsKey = "Xfw.SampleDevMenu.PendingAttempts";
        private const int MaxPendingAttempts = 60;

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        #endregion

        #region Menu — Integration

        [MenuItem("Tools/XFramework/Samples/Integration/Import and Open Boot Scene", false, 10)]
        public static void ImportIntegrationAndOpen()
        {
            ImportSample("Integration", IntegrationScene, play: false, ensureCollector: true);
        }

        [MenuItem("Tools/XFramework/Samples/Integration/Import, Open and Play", false, 11)]
        public static void ImportIntegrationAndPlay()
        {
            ImportSample("Integration", IntegrationScene, play: true, ensureCollector: true);
        }

        [MenuItem("Tools/XFramework/Samples/Integration/Import and Open Boot Scene", true)]
        [MenuItem("Tools/XFramework/Samples/Integration/Import, Open and Play", true)]
        private static bool ValidateIntegrationMenus() => CanRunSilently();

        #endregion

        #region Menu — Clean

        [MenuItem("Tools/XFramework/Samples/Clean Imported Samples", false, 100)]
        public static void CleanImportedSamples()
        {
            if (!CanRun())
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            foreach (var sample in new[] { "Integration" })
                DeleteAssetFolder("Assets/Samples/" + sample);

            var samplesDir = Path.Combine(ProjectRoot, "Assets/Samples");
            if (Directory.Exists(samplesDir) && Directory.GetFileSystemEntries(samplesDir).Length == 0)
                AssetDatabase.DeleteAsset("Assets/Samples");

            IntegrationCollectorSetup.RemoveDevCollectorGroup();

            var bundles = Path.Combine(ProjectRoot, "Bundles");
            if (Directory.Exists(bundles))
                Directory.Delete(bundles, true);

            ClearPending();
            AssetDatabase.Refresh();
            Debug.Log("[XFramework DevTools] 已清理示例导入副本、开发采集组与 Bundles。");
        }

        [MenuItem("Tools/XFramework/Samples/Clean Imported Samples", true)]
        private static bool ValidateCleanMenu() => CanRunSilently();

        #endregion

        #region Import

        /// <summary>复制示例、登记待办；编译/导入完成后由 <see cref="FlushPending"/> 打开场景。</summary>
        private static void ImportSample(string sample, string scenePath, bool play, bool ensureCollector)
        {
            if (!CanRun())
                return;

            if (HasOtherImportedCopy(sample))
            {
                Warn($"Assets/Samples/XFramework 下已有另一份 {sample} 副本（Package Manager 导入）。" +
                     "两份同名类型会让 Assembly-CSharp 编译失败，请先移走其中一份。");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var source = "Assets/XFramework/Samples~/" + sample;
            var dest = "Assets/Samples/" + sample;

            if (!Directory.Exists(ToAbsolute(source)))
            {
                Warn($"找不到示例目录：{source}");
                return;
            }

            // 当前场景若在被替换的目标目录里，先切到空场景，避免删除后留下悬空场景。
            var active = EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path) &&
                active.path.StartsWith(dest, StringComparison.OrdinalIgnoreCase))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            DeleteAssetFolder(dest);
            CopyDirectory(ToAbsolute(source), ToAbsolute(dest));
            CopyFolderMeta(source, dest);

            SessionState.SetString(PendingSceneKey, scenePath);
            SessionState.SetBool(PendingPlayKey, play);
            SessionState.SetBool(PendingCollectorKey, ensureCollector);
            SessionState.SetInt(PendingAttemptsKey, 0);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorApplication.delayCall += FlushPending;
        }

        /// <summary>域重载后重新挂上待办刷新；关闭域重载时也能继续。</summary>
        [InitializeOnLoadMethod]
        private static void RegisterFlushOnReload()
        {
            EditorApplication.delayCall += FlushPending;
        }

        private static void FlushPending()
        {
            var scenePath = SessionState.GetString(PendingSceneKey, string.Empty);
            if (string.IsNullOrEmpty(scenePath))
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RetryOrAbort();
                return;
            }

            var play = SessionState.GetBool(PendingPlayKey, false);
            var ensureCollector = SessionState.GetBool(PendingCollectorKey, false);

            if (EditorUtility.scriptCompilationFailed)
            {
                ClearPending();
                Warn("示例导入后编译失败，已中止打开场景/进入 Play。请查看 Console。");
                return;
            }

            if (ensureCollector && !AssetDatabase.IsValidFolder("Assets/Samples/Integration"))
            {
                RetryOrAbort();
                return;
            }

            if (!File.Exists(ToAbsolute(scenePath)))
            {
                ClearPending();
                Warn($"场景不存在：{scenePath}。示例可能未导入完整。");
                return;
            }

            ClearPending();

            if (ensureCollector)
            {
                try
                {
                    IntegrationCollectorSetup.EnsureCollectorSetting();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Warn("创建 YooAsset 采集设置失败，已中止打开场景。");
                    return;
                }
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            if (play)
                EditorApplication.isPlaying = true;
        }

        private static void RetryOrAbort()
        {
            int attempts = SessionState.GetInt(PendingAttemptsKey, 0) + 1;
            if (attempts > MaxPendingAttempts)
            {
                ClearPending();
                Warn("等待编辑器编译/导入超时，已放弃打开示例场景。请手动重试菜单。");
                return;
            }

            SessionState.SetInt(PendingAttemptsKey, attempts);
            EditorApplication.delayCall += FlushPending;
        }

        private static void ClearPending()
        {
            SessionState.EraseString(PendingSceneKey);
            SessionState.EraseBool(PendingPlayKey);
            SessionState.EraseBool(PendingCollectorKey);
            SessionState.EraseInt(PendingAttemptsKey);
        }

        #endregion

        #region Helpers

        private static bool CanRun()
        {
            if (!CanRunSilently())
            {
                Warn("Play 模式中或编辑器正在编译/导入，请稍后再试。");
                return false;
            }

            return true;
        }

        private static bool CanRunSilently()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                   && !EditorApplication.isCompiling
                   && !EditorApplication.isUpdating;
        }

        private static bool HasOtherImportedCopy(string sample)
        {
            var pmRoot = ToAbsolute("Assets/Samples/XFramework");
            if (!Directory.Exists(pmRoot))
                return false;

            return Directory.GetDirectories(pmRoot, sample, SearchOption.AllDirectories).Length > 0;
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);

            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private static void CopyFolderMeta(string sourceAssetPath, string destAssetPath)
        {
            var sourceMeta = ToAbsolute(sourceAssetPath) + ".meta";
            if (!File.Exists(sourceMeta))
                return;

            File.Copy(sourceMeta, ToAbsolute(destAssetPath) + ".meta", true);
        }

        private static void DeleteAssetFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
                return;
            }

            var abs = ToAbsolute(assetPath);
            if (Directory.Exists(abs))
                Directory.Delete(abs, true);
            if (File.Exists(abs + ".meta"))
                File.Delete(abs + ".meta");
        }

        private static string ToAbsolute(string assetPath)
        {
            return Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Warn(string message)
        {
            Debug.LogWarning("[XFramework DevTools] " + message);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("XFramework Samples", message, "OK");
        }

        #endregion
    }
}
