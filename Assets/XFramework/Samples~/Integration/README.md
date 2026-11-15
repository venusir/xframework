# XFramework 全流程接入示例（Integration）

> 定位：**接入参考**。它把核心包的大多数模块串成一条完整的游戏流程——启动 → 主菜单 → 设置 → 语言 → 进入游戏 → 暂停 → 结算 → 退出。内置的两个极简玩法只用来驱动流程，不主张任何游戏架构。
>
> 如果你只想「导入后按 Play 就能跑」，请先看 `Samples~/Playable`（零资产、不需要 YooAsset 采集配置）。

## 1. 它演示了什么

| 模块 | 在示例里怎么用 |
|---|---|
| Bootstrap | `IntegrationLauncher` 继承 `GameLauncher`，用自建的 `IntegrationBootstrapProfile` 资产 + UI 根装配 Asset / Data / Save / UI / Input / Localization，再追加 Settings 与 Config 阶段；`AppFlowController` 用 `Bootstrap.WaitUntilReadyAsync` 等就绪。**框架不提供开箱启动器——这两个文件就是模板** |
| Asset | `EditorSimulate` 初始化、按地址加载面板 / 语言表 / 音频、预载与池状态 |
| UI | `UIRootNode` + `UIManager.OpenAsync/PushAsync/PopAsync`、对话框、模态遮罩、语言回调 |
| Audio | `AudioManager.Initialize`、BGM / 点击 / 判定音、音量通道 |
| Settings | 纯 POCO + `SettingRef` 句柄、校验器、迁移器、显式保存 |
| Localization | 引导语言表 + `LanguageAssetPath` 模板切换语言 |
| Input | `InputSystemOptionsAsset` 指定 `.inputactions` 与初始 ActionMap |
| Data / Save | 数据块、存档槽位、结算后保存 |
| Config | 模式表与全局规则：JSON 经 `AssetManager` 加载，运行期按 id 查询 |
| Data / Save | 数据块、存档槽位、结算后保存最佳分 |
| Scene / Pipeline | 加载游戏场景的进度管线：Asset 预载 → Scene 加载 → UI 打开，进度汇总到加载面板 |
| Camera | CinemachineBrain + 相机注册 / 切换 / 跟随 / 震屏 |
| Message / Reactive / Timer / Update / Pool / Lock / File / Serialize / Log / Diagnostics | 流程、倒计时、对象池、退出锁、摘要落盘、诊断页签 |

> 本示例只使用**公开 API**；它不随包编译，由仓库的示例编译门禁单独校验。

> **启动器模板**：`Scripts/Bootstrap/IntegrationLauncher.cs` + `IntegrationBootstrapProfile.cs` 是「自己写启动器与 Profile」的完整参考——框架只提供 `GameLauncher` 底座，字段面与装配都由使用方写。Profile 的字段按项目需要增删（例如换输入后端时删掉 `_inputOptions`，改登记自己的 provider）。

## 2. 导入与配置（三步）

1. **导入示例**：Package Manager → XFramework → Samples → **XFramework 全流程接入示例** → Import。
   实际路径形如 `Assets/Samples/XFramework/<版本>/Integration`。
2. **加入 YooAsset 采集**：打开 YooAsset 的 AssetBundle Collector 设置：
   - 在 `DefaultPackage` 上勾选 **Enable Addressable**；
   - 新建分组（如 `Integration`）；
   - 添加一个 **MainAssetCollector**，路径指向导入的 `Integration` 根目录；
   - Address Rule 选 **AddressByFileName**，Pack Rule 选 **PackDirectory**，Filter Rule 选 **CollectAll**。
   示例的所有资源文件名都以 `Integration.` 开头并保持唯一，因此地址就是文件名去扩展名
   （`Integration.MainMenu.prefab` → `Integration.MainMenu`）。
3. **运行**：打开 `Integration.Boot.unity`，按 Play。

> 示例 Profile（`Data/Integration.BootstrapProfile.asset`）里的 Asset Play Mode 固定为 `EditorSimulate`
> （仅编辑器可用）。要出 Player，请把它改成 `Offline` 或 `Host`，并先用 YooAsset 构建资源包。
> 该模式忽略加密与远端服务。

## 3. 操作

- **主菜单**：开始游戏 / 设置 / 语言 / 退出。
- **模式选择**：点星与找异色两种模式。
- **点星**：随机目标亮起，点中得分；30 秒倒计时结束进入结算。
- **找异色**：每回合在色块中选出异色，答错扣时间；回合走完或超时进入结算。
- **暂停**：游戏中按 Esc（或手柄 Start）打开暂停面板（继续 / 重开 / 主菜单 / 退出）。
- **设置**：主音量、BGM、音效三个滑条 + 提示开关；**应用**写盘，**恢复默认**回退。
- **语言**：中文 / English，切换后面板文本即时刷新。
- **退出**：二次确认 → 关面板 → 存档 → 保存设置 → 写摘要 → 逆序清理 → 退出。
  编辑器内 `Application.Quit()` 无效，会显示「服务已清理，请手动停止 Play」。

## 4. 目录

```text
Integration/
├── Scripts/
│   ├── Bootstrap/   启动器与自定义引导阶段
│   ├── App/         流程控制、地址常量、消息、摘要落盘
│   ├── Settings/    设置 POCO、句柄、校验器、迁移器
│   ├── Config/      模式表与全局规则
│   ├── Data/        数据块
│   ├── Game/        会话接口、棋盘视图、点星玩法与工厂
│   ├── Pipeline/    加载管线（Asset 预载 → Scene → UI）
│   ├── UI/          启动屏、主菜单、设置、语言、确认框、模式选择、玩法、暂停、加载、结算
│   └── Diagnostics/ 诊断页签
├── Prefabs/         面板预制体（文件名即地址）
├── Scenes/          Integration.Boot.unity / Integration.Game.unity
├── Audio/           BGM 与音效
└── Data/            语言表、模式表、输入动作、示例自己的 Bootstrap Profile 资产
```

## 5. 已知限制

- **必须配置 YooAsset 采集**才能运行；这是真实资源接入示例的代价（Playable 是零配置版本）。
- **`EditorSimulate` 仅编辑器可用**；Player 构建需改 Offline / Host 并构建资源包。
- 示例**不附带** `AssetBundleCollectorSetting.asset`：YooAsset 全工程只允许一份，随示例附带会与
  你项目里已有的那份冲突。
- 示例用普通 `UnityEngine.UI.Text`（不依赖 TMP Essentials）；生产项目可换成 TMP 或自己的 UI 技术栈。
- 示例的玩法与流程控制是**一种**接线方式，不是框架规定的架构。
