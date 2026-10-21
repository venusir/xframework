# Playable 示例（零资产）

导入本示例后打开 `Playable.unity`，按 Play。场景里只有一个挂 `PlayableSample` 的物体和一台相机；
UI 由 IMGUI 绘制，不需要预制体、字体或 YooAsset 采集配置。

## 覆盖的模块

- **Bootstrap**：显式登记 Data(3) → Save(4)，验证相位顺序与逆序清理
- **Data**：`PlayableData` 数据块 + 脏标记
- **Save**：槽位 0 的存 / 读 / 删（快照经 DataManager）
- **Message**：`ScoreChangedMessage` 的发布与订阅
- **Reactive**：`ReactiveProperty<int>` 驱动分值显示
- **Timer**：每秒累加游玩秒数，随对象销毁自动停表
- **File**：写 / 读 `AppData/playable/notes.txt`
- **Log**：自定义分类 `PlayableSample`

## 为什么没有 Asset / UI / Audio

它们需要 YooAsset 的采集配置（`AssetBundleCollectorSetting`）与面板预制体；本示例的目标是
「导入即玩」。模块级用法见 `Example` 示例与各模块 README。

## 操作

- **+1 Score**：Reactive 更新显示，Message 发通知
- **Save / Load**：槽位 0
- **Write Note**：写持久化目录下的文本文件
- **Reset**：清内存并删除槽位

> 示例代码只使用公开 API，可直接作为自己项目的接线起点。
