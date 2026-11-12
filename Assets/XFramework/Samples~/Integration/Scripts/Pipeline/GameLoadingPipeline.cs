using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XAsset;
using XFramework.XPipeline;
using XFramework.XScene;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 「进入游戏」的加载管线：预载面板 → Additive 加载游戏场景 → 打开玩法面板。
    /// <para>用到的模块：Pipeline（阶段编排与加权进度）、Asset（预载 / 地址校验）、Scene（场景句柄）、
    /// UI（加载面板与玩法面板）。进度经 <see cref="IPipeline.OnProgressUpdate"/> 推给加载面板。</para>
    /// </summary>
    public static class GameLoadingPipeline
    {
        #region Constants

        private static readonly string[] PreloadLocations =
        {
            IntegrationAddresses.ModeSelect,
            IntegrationAddresses.GamePanel,
            IntegrationAddresses.Result,
            IntegrationAddresses.Pause,
            IntegrationAddresses.Loading,
            IntegrationAddresses.Confirm,
            IntegrationAddresses.Bgm,
            IntegrationAddresses.Click,
            IntegrationAddresses.Correct,
            IntegrationAddresses.Wrong,
        };

        #endregion

        #region Public API

        /// <summary>运行加载管线并返回游戏场景句柄。</summary>
        public static async UniTask<AssetSceneHandle> RunAsync(string modeId, LoadingPanel loading,
            CancellationToken cancellationToken = default)
        {
            var state = new LoadState();
            var pipeline = Pipeline.Create();
            pipeline.AddStage(new PreloadStage());
            pipeline.AddStage(new LoadSceneStage(state));
            pipeline.AddStage(new OpenPanelStage(modeId));

            if (loading != null)
            {
                pipeline.OnProgressUpdate += progress =>
                    loading.SetProgress(progress.OverallProgress, progress.Description);
            }

            await pipeline.RunAsync(cancellationToken);

            if (pipeline.Status != PipelineStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"游戏加载管线未完成：{pipeline.Status} {pipeline.FailureReason}");
            }

            return state.Scene;
        }

        #endregion

        #region Stages

        private sealed class LoadState
        {
            public AssetSceneHandle Scene;
        }

        private sealed class PreloadStage : IPipelineStage
        {
            public string Name => "Preload";
            public float Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                context.SetDescription("Preloading UI and audio...");

                for (int i = 0; i < PreloadLocations.Length; i++)
                {
                    if (!AssetManager.CheckLocationValid(PreloadLocations[i]))
                    {
                        XFramework.XLog.LogManager.Warning(IntegrationLogCategories.Flow,
                            "地址 '{0}' 不在资源清单里，预载将跳过它。请检查 YooAsset 采集配置。",
                            PreloadLocations[i]);
                    }
                }

                await AssetManager.PreloadAllAsync(PreloadLocations,
                    progress => context.SetProgress(progress), cancellationToken);

                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
            }
        }

        private sealed class LoadSceneStage : IPipelineStage
        {
            private readonly LoadState _state;

            public LoadSceneStage(LoadState state)
            {
                _state = state;
            }

            public string Name => "LoadScene";
            public float Weight => 2f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                context.SetDescription("Loading game scene...");

                _state.Scene = await SceneManager.LoadSceneAsync(
                    IntegrationAddresses.GameScene,
                    SceneLoadOptions.Additive,
                    new Progress<SceneLoadProgress>(p => context.SetProgress(p.Progress)),
                    cancellationToken);

                if (_state.Scene.IsDefault)
                {
                    throw new InvalidOperationException(
                        $"游戏场景 '{IntegrationAddresses.GameScene}' 加载失败。请检查 YooAsset 采集配置。");
                }

                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
            }
        }

        private sealed class OpenPanelStage : IPipelineStage
        {
            private readonly string _modeId;

            public OpenPanelStage(string modeId)
            {
                _modeId = modeId;
            }

            public string Name => "OpenPanel";
            public float Weight => 1f;

            public async UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
            {
                context.SetDescription("Opening game panel...");
                await UIManager.PushAsync<GamePanel>(
                    IntegrationAddresses.GamePanel, UILayers.Default, _modeId, cancellationToken);
                context.SetProgress(1f);
                context.SetState(PipelineStageState.Completed);
            }
        }

        #endregion
    }
}
