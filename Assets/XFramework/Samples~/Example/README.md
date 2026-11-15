# Example 示例（模块用法演示）

导入本示例后打开 `Example.unity`，按 Play。场景里有一个挂载 Reactive / Message / Timer / Audio
演示组件的物体，以及一块 IMGUI 操作面板；各模块的演示日志输出在 Console。

## 覆盖的模块

- **Reactive**：`ReactiveProperty<int/float>` 的健康值与分数变化日志
- **Message**：类型化发布/订阅、Key 通道、订阅级过滤、缓冲重放、异步订阅、请求-响应与统计
- **Timer**：冷却查询与重开、固定间隔、随对象销毁自动停表、双时间轴；HUD 显示拍数与冷却进度
- **Audio**：通道与三档音量、播放句柄、暂停/恢复；**需要 YooAsset 地址**（见下）
- **Diagnostics**：进 Play 后自动登记「我的游戏」页签（`Tools/XFramework/Diagnostics`）

## 操作

- **Use Skill**：释放技能并重置 8 秒冷却；冷却未好时按钮不生效（Console 有日志）
- **Play UI Click / Pause Audio / Resume Audio**：需要音频就绪
- **Cancel Timers**：停止本模块全部定时器（一次性，之后拍数不再增长）

## 已知限制

- `UISample` **不在场景里**：它需要场景中的 `UIRootNode` 与按 YooAsset 地址加载的面板预制体，
  而本示例故意不附带预制体。它的源码仍是调用方式的参考。
- `AudioSample` 需要先把 `_bgmLocation` / `_hitLocation` / `_uiClickLocation` 三个地址注册进
  YooAsset；未注册时只打一条 Warning 并跳过播放，不会产生红色错误。
- `ExampleSampleHub` 是 IMGUI 演示外壳，不是框架提供的 UI 方案。
- 本示例只使用公开 API；它不随包编译，由仓库的示例编译门禁单独校验。
