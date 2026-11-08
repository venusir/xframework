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
        /// <para><b>失败回滚只卸载本次安装成功加载的 bank</b>：安装开始时会快照并去重
        /// <see cref="FmodAudioSettings.Banks"/>；安装前已加载的项目 bank 永不纳入 ownership。
        /// 回滚使用同一份快照，不重新读取配置资产。</para>
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

            // 安装开始时的快照：之后不再读 settings；factory/加载/回滚使用同一份数据。
            List<string> plannedBanks = SnapshotBanks(settings.Banks);
            bool loadSamples = settings.LoadBankSamples;
            FmodProviderOptions providerOptions = settings.CreateProviderOptions();
            AudioInitOptions initOptions = settings.CreateAudioInitOptions();

            // 只记录本次安装成功加载的 bank；预加载 bank 永不进入这里。
            var loadedBanks = new List<string>();
            IFmodEventBackend backend = null;

            try
            {
                backend = backendFactory();
                if (backend == null)
                {
                    LogManager.Error(LogCategories.Audio,
                        "FMOD backend 工厂返回 null，自动安装中止。");
                    return false;
                }

                for (int i = 0; i < plannedBanks.Count; i++)
                {
                    string bank = plannedBanks[i];

                    if (backend.IsBankLoaded(bank))
                        continue;

                    if (!backend.LoadBank(bank, loadSamples))
                    {
                        LogManager.Error(LogCategories.Audio,
                            "FMOD bank '{0}' 加载失败，自动安装中止。", bank);
                        Cleanup(backend, loadedBanks);
                        DisposeBackend(backend);
                        return false;
                    }

                    loadedBanks.Add(bank);
                }

                var provider = new FmodAudioProvider(backend, providerOptions);
                try
                {
                    AudioManager.SetProvider(provider, initOptions);
                }
                catch (Exception e)
                {
                    LogManager.Exception(LogCategories.Audio, e,
                        "FMOD 自动安装：AudioManager.SetProvider 抛出异常，已回滚。");
                    Cleanup(backend, loadedBanks);
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
                    Cleanup(backend, loadedBanks);
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

        /// <summary>复制并去重 bank 列表；空白项跳过。</summary>
        private static List<string> SnapshotBanks(string[] banks)
        {
            var snapshot = new List<string>(banks?.Length ?? 0);
            if (banks == null)
                return snapshot;

            for (int i = 0; i < banks.Length; i++)
            {
                string bank = banks[i];
                if (string.IsNullOrWhiteSpace(bank) || Contains(snapshot, bank))
                    continue;

                snapshot.Add(bank);
            }

            return snapshot;
        }

        /// <summary>
        /// 失败清理：只卸载本次安装成功加载、且当前仍已加载的 bank；项目预加载的 bank 从未进入
        /// <paramref name="loadedBanks"/>，因此不会被这里卸载。
        /// </summary>
        private static void Cleanup(IFmodEventBackend backend, List<string> loadedBanks)
        {
            if (backend == null || loadedBanks == null)
                return;

            // 逆序卸载：后加载的 bank 可能依赖先加载的 bank。
            for (int i = loadedBanks.Count - 1; i >= 0; i--)
            {
                string bank = loadedBanks[i];
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
