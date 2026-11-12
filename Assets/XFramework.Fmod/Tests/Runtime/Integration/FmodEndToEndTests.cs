using System;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XAudio;

namespace XFramework.XAudio.Fmod.Unity.Tests
{
    /// <summary>
    /// 自动安装端到端冒烟：<c>FmodAutoInstall</c> → <c>FmodInstaller</c> → 真实
    /// <c>FmodEventBackend</c> → <see cref="AudioManager"/> 公开门面。
    /// <para><b>前置</b>：FMOD for Unity 2.04 已安装（<c>XFRAMEWORK_FMOD</c> 编译开关）、
    /// <c>Assets/Resources/FMOD/FMODStudioSettings.asset</c> 已由 Setup Wizard 生成、根级
    /// <c>FmodAudioSettings</c> 已启用并配好 bank、Studio 工程与 bank 可用。</para>
    /// <para><b>门控</b>：<c>XFW_FMOD_E2E=1</c> 且 <c>XFW_FMOD_EVENT</c> / <c>XFW_FMOD_BANK</c> /
    /// <c>XFW_FMOD_GLOBAL_PARAM</c> 均已配置；缺省 <see cref="Assert.Ignore(string)"/>，不伪造结果。</para>
    /// </summary>
    [TestFixture]
    public class FmodEndToEndTests
    {
        [Test]
        public async Task AutoInstall_RealBackend_PublicFacade_WhenConfigured()
        {
            if (Environment.GetEnvironmentVariable("XFW_FMOD_E2E") != "1")
                Assert.Ignore("未配置 XFW_FMOD_E2E=1；跳过自动安装端到端冒烟。");

            string eventPath = Environment.GetEnvironmentVariable("XFW_FMOD_EVENT");
            string bank = Environment.GetEnvironmentVariable("XFW_FMOD_BANK");
            string globalParameter = Environment.GetEnvironmentVariable("XFW_FMOD_GLOBAL_PARAM");
            if (string.IsNullOrWhiteSpace(eventPath) || string.IsNullOrWhiteSpace(bank) ||
                string.IsNullOrWhiteSpace(globalParameter))
            {
                Assert.Ignore(
                    "端到端冒烟需要 XFW_FMOD_EVENT / XFW_FMOD_BANK / XFW_FMOD_GLOBAL_PARAM。");
            }

            try
            {
                Assert.IsTrue(AudioManager.IsInitialized,
                    "FmodAutoInstall 未在 BeforeSceneLoad 安装 provider；请确认 Resources 根级的 " +
                    "FmodAudioSettings 与 FMODStudioSettings 均已就位。");

                // 内置 provider 对全局参数返回 false；成功即证明生效的是 FMOD provider。
                Assert.IsTrue(AudioManager.SetGlobalParameter(globalParameter, 0.5f),
                    "经公开门面设置全局参数失败；生效的可能不是 FMOD provider。");

                // 安装器已在 BeforeSceneLoad 加载该 bank；LoadBank 应幂等成功。
                Assert.IsTrue(AudioManager.LoadBank(bank), "经公开门面加载 bank 失败。");

                AudioHandle handle = await AudioManager.PlayAsync(eventPath);
                try
                {
                    Assert.IsFalse(handle.IsDefault, "AudioManager.PlayAsync 未返回有效句柄。");
                    Assert.IsTrue(AudioManager.IsPlaying(handle), "真实 FMOD 事件未进入播放状态。");
                }
                finally
                {
                    AudioManager.Stop(handle);
                }

                Assert.IsTrue(AudioManager.UnloadBank(bank), "经公开门面卸载 bank 失败。");
            }
            finally
            {
                // 本 fixture 只在门控命中时触碰门面；复位静态态，避免污染同一次运行中的其它用例。
                AudioManager.Destroy();
            }
        }
    }
}
