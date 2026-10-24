using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.Luban.Sample.Generated;
using XFramework.XAsset;
using XFramework.XConfig;
using XFramework.XConfig.Luban;

namespace XFramework.Luban.Sample
{
    /// <summary>
    /// 最小 Luban 配置示例：初始化 AssetManager（EditorSimulate）→ 加载 Luban 数据 →
    /// 通过 ConfigManager 查询 map 表与单行表，同时访问纯列表表的强类型 DataList。
    /// </summary>
    public sealed class LubanSample : MonoBehaviour
    {
        [Tooltip("YooAsset collector 暴露的数据地址前缀；默认 configs/<表名>.json。")]
        [SerializeField]
        private string _dataRoot = "configs";

        private async void Start()
        {
            try
            {
                if (!AssetManager.IsInitialized)
                {
                    await AssetManager.InitializeAsync(new AssetInitOptions
                    {
                        PlayMode = AssetPlayMode.EditorSimulate,
                    });
                }

                await LubanConfig.InitializeAsync(
                    new LubanSampleConfigAdapter(),
                    new LubanOptions { DataRoot = _dataRoot });

                var item = ConfigManager.Get<cfg.demo.Item, int>(1001);
                var global = ConfigManager.GetGlobal<cfg.demo.Global>();
                var tables = LubanConfig.GetTables<cfg.Tables>();

                Debug.Log(
                    $"[LubanSample] item={item.Name} price={item.Price}, " +
                    $"version={global.Version}, spawnPoints={tables.TbSpawnPoint.DataList.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void OnDestroy()
        {
            LubanConfig.Destroy();
        }
    }
}
