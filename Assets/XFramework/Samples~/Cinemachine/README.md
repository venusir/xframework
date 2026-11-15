# Cinemachine 相机示例

导入本示例后打开 `Cinemachine.unity`，按 Play。场景里有一台带 `CinemachineBrain` 的主相机、
两台 `CinemachineCamera`（First / Second）和一个可旋转的胶囊目标；IMGUI 面板提供切换、压栈、
跟随与震屏操作。

## 前置

- Cinemachine 3.x 由核心包依赖提供（当前 3.1.7），不需要额外安装。
- 不需要 YooAsset、预制体或 TMP。

## 场景里有什么

- `Main Camera`：`Camera` + `AudioListener` + `CinemachineBrain`
- `First` / `Second`：两台 `CinemachineCamera` + `CinemachineImpulseListener`，都看向 `FollowTarget`
- `FollowTarget`：胶囊体；`Spin Target` 打开后持续旋转，便于观察 Follow
- `CameraSample`：把 Brain 与两台 vcam 接进 `CameraManager`，并调用 `SwitchTo("first")`
- `Overlay`：本示例的 IMGUI 操作面板

## 操作与预期

| 按钮 | 预期 |
|---|---|
| **Switch** | 在 first / second 之间切换，0.8 秒 EaseInOut 混合 |
| **Switch Async** | 同一路径的异步版本；面板显示 `Completed` 等结果 |
| **Push Second** | 把 second 压入相机栈（0.2 秒混合），状态行 `depth` 增加 |
| **Pop** | 弹出栈顶，回到 first（0.25 秒混合） |
| **Shake** | 当前活动相机震屏（需要该相机带 `CinemachineImpulseListener`） |
| **Spin Target** | 旋转 FollowTarget，观察 `SetFollow("first", …)` 的跟随效果 |

## 怎么接到自己的项目

`CameraSample` 的方法都是普通公开方法（`SwitchToNext` / `PushSecond` / `PopSecond` / `Shake` /
`SwitchToNextAsync`），直接接到自己的按钮或输入回调即可。`CameraManager` 的 gameplay 面使用
string id 与相机栈；初始化、注册与高级访问使用 Cinemachine 类型。

## 已知限制

- IMGUI 面板只是演示外壳，不是框架提供的 UI 方案。
- `CameraManager` 是静态门面：同一时刻只应有一个示例/游戏初始化它。
- 本示例只使用公开 API；它不随包编译，由仓库的示例编译门禁单独校验。
