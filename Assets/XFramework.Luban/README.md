# XFramework Luban

XFramework 的可选 Luban 配置扩展包。安装顺序：

1. `com.venusy609.xframework`（核心包）
2. [`com.code-philosophy.luban`](https://github.com/focus-creative-games/luban_unity)（Luban 官方 Unity 运行时）
3. 本包

本包提供：

- 项目内设置（Luban.dll / conf / 输入输出目录 / target / 适配器命名）
- Editor 一键导表（调用 `dotnet Luban.dll`）
- 导表后生成 `IConfigRow<TKey>` 接缝与强类型适配器
- 运行时经 `AssetManager` 加载数据、构造 `Tables`，并把 map / 单行 / 带索引表注册进 `ConfigManager`

核心包不依赖 Luban；不用 Luban 的项目不需要安装本包。

> 本 README 在实现过程中分阶段补齐；完整的安装、快速使用、行为契约、已知限制与设计取舍将在本包首次发布前完成。
