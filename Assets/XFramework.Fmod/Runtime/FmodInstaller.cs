using System;
using System.Collections.Generic;
using XFramework.XLog;

namespace XFramework.XAudio.Fmod
{
    /// <summary>
    /// FMOD 自动安装的 FMOD-free 逻辑：检查 <see cref="AudioManager"/> 状态、加载 bank、构造 provider
    /// 并接线。
    /// <para>真实 backend 由调用方（条件集成程序集）通过 <c>backendFactory</c> 提供；本类型不引用
    /// FMOD 的集成程序集，干净克隆可编译、可用 fake backend 测试。</para>
    /// <para><b>线程</b>：主线程调用。</para>
    /// </summary>
    internal static class FmodInstaller
    {
        #region Internal

        /// <summary>
        /// 尝试安装 FMOD provider。
        /// </summary>
        /// <param name="settings">配置资产；<c>null</c> 或 <see cref="FmodAudioSettings.Enabled"/> 为
        /// <c>false</c> 时跳过。</param>
        /// <param name="backendFactory">真实 backend 工厂。</param>
        /// <param name="fmodSettingsReady">FMOD 的 <c>FMODStudioSettings</c> 资产是否已存在。</param>
        /// <returns>成功接线返回 <c>true</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="backendFactory"/> 为 null 时抛出。</exception>
        internal static bool TryInstall(
            FmodAudioSettings settings,
            Func<IFmodEventBackend> backendFactory,
            bool fmodSettingsReady)
        {
            if (settings == null || !settings.Enabled)
                return false;

            if (AudioManager.IsInitialized)
            {
                LogManager.Warning(LogCategories.Audio,
                    "AudioManager 已初始化，跳过 FMOD 自动安装；不覆盖项目已显式注入的 manager/provider。");
                return false;
            }

            if (!fmodSettingsReady)
            {
                LogManager.Error(LogCategories.Audio,
                    "未找到 FMODStudioSettings，跳过 FMOD 自动安装；请先运行 FMOD Setup Wizard 并配置 Studio 工程/bank。");
                return false;
            }

            if (backendFactory == null)
                throw new ArgumentNullException(nameof(backendFactory));

            IFmodEventBackend backend = null;
            var preExistingBanks = new List<string>();

            try
            {
                backend = backendFactory();
                if (backend == null)
                {
                    LogManager.Error(LogCategories.Audio,
                        "FMOD backend 工厂返回 null，自动安装中止。");
                    return false;
                }

                string[] banks = settings.Banks;
                for (int i = 0; i < banks.Length; i++)
                {
                    string bank = banks[i];
                    if (string.IsNullOrWhiteSpace(bank) || Contains(preExistingBanks, bank))
                        continue;

                    if (backend.IsBankLoaded(bank))
                    {
                        // 项目预加载的 bank 永不卸载：只记录，不纳入 ownership。
                        preExistingBanks.Add(bank);
                        continue;
                    }

                    if (!backend.LoadBank(bank, settings.LoadBankSamples))
                    {
                        LogManager.Error(LogCategories.Audio,
                            "FMOD bank '{0}' 加载失败，自动安装中止。", bank);
                        Cleanup(backend, banks, preExistingBanks);
                        DisposeBackend(backend);
                        return false;
                    }
                }

                var provider = new FmodAudioProvider(backend, settings.CreateProviderOptions());
                try
                {
                    AudioManager.SetProvider(provider, settings.CreateAudioInitOptions());
                }
                catch (Exception e)
                {
                    LogManager.Exception(LogCategories.Audio, e,
                        "FMOD 自动安装：AudioManager.SetProvider 抛出异常，已回滚。");
                    Cleanup(backend, banks, preExistingBanks);
                    try
                    {
                        provider.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        LogManager.Exception(LogCategories.Audio, disposeException,
                            "FMOD 自动安装回滚时 provider.Dispose 抛出异常，已隔离。");
                    }

                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e, "FMOD 自动安装失败，已回滚。");
                if (backend != null)
                {
                    Cleanup(backend, settings.Banks, preExistingBanks);
                    DisposeBackend(backend);
                }

                return false;
            }
        }

        #endregion

        #region Private

        private static bool Contains(List<string> banks, string bank)
        {
            for (int i = 0; i < banks.Count; i++)
            {
                if (string.Equals(banks[i], bank, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 失败清理：只卸载本次新加载、且当前仍已加载的 bank；项目预加载的 bank 不动。
        /// </summary>
        private static void Cleanup(IFmodEventBackend backend, string[] banks, List<string> preExistingBanks)
        {
            if (backend == null || banks == null)
                return;

            for (int i = 0; i < banks.Length; i++)
            {
                string bank = banks[i];
                if (string.IsNullOrWhiteSpace(bank) || Contains(preExistingBanks, bank))
                    continue;

                try
                {
                    if (backend.IsBankLoaded(bank))
                        backend.UnloadBank(bank);
                }
                catch (Exception e)
                {
                    LogManager.Exception(LogCategories.Audio, e,
                        "FMOD 自动安装清理 bank 时抛出异常，已隔离。");
                }
            }
        }

        private static void DisposeBackend(IFmodEventBackend backend)
        {
            try
            {
                backend.Dispose();
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e,
                    "FMOD 自动安装清理 backend 时抛出异常，已隔离。");
            }
        }

        #endregion
    }
}
