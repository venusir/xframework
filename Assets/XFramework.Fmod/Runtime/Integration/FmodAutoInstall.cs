using System;
using UnityEngine;

namespace XFramework.XAudio.Fmod.Unity
{
    /// <summary>
    /// FMOD 条件集成程序集的自动安装入口。只在 FMOD 2.04 存在（<c>XFRAMEWORK_FMOD</c>）时编译；
    /// 具体安装逻辑在 FMOD-free 的 <see cref="FmodInstaller"/> 中，便于干净克隆测试。
    /// </summary>
    internal static class FmodAutoInstall
    {
        #region Internal

        /// <summary>测试/消费方逃生口：设为 <c>1</c> 时跳过自动安装。</summary>
        internal const string DisableEnvironmentVariable = "XFW_DISABLE_FMOD_AUTOINSTALL";

        /// <summary>消费方配置资产在 <c>Resources</c> 中的名字。</summary>
        internal const string SettingsResourcePath = "FmodAudioSettings";

        /// <summary>FMOD Setup Wizard 生成的设置资产在 <c>Resources</c> 中的路径。</summary>
        internal const string FmodStudioSettingsResourcePath = "FMOD/FMODStudioSettings";

        #endregion

        #region Private

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInstall()
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable(DisableEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            FmodAudioSettings settings = Resources.Load<FmodAudioSettings>(SettingsResourcePath);
            if (settings == null)
                return;

            bool fmodSettingsReady = Resources.Load(FmodStudioSettingsResourcePath) != null;

            FmodInstaller.TryInstall(
                settings,
                () => new FmodEventBackend(settings.OwnsBanks),
                fmodSettingsReady);
        }

        #endregion
    }
}
