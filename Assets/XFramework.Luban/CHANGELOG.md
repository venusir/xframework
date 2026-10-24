# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-10-24

### Added

- 新增独立可选扩展包 `com.venusy609.xframework.luban`：核心包零改动、零 Luban 依赖。
- Runtime：`LubanConfig` 静态门面、`LubanOptions`、`LubanConfigBridge`、`LubanBootstrapStage`、
  `AssetLubanDataProvider`（TextAsset / RawFile）、会话复位、注册失败回滚。
- Editor：ProjectSettings 设置页、`dotnet Luban.dll` 一键导表、生成式 `IConfigRow<TKey>` 行接口实现与
  强类型适配器、`.luban-integration-manifest`、同 asmdef / 引用校验。
- 支持四种表形状：map、单行、带索引 list（单索引与 `ValueTuple` 联合索引）、纯列表（跳过注册）。
- `Samples~/Luban`：最小可运行示例（JSON target），含 schema、生成物、数据与示例脚本。
- 测试：门面/桥接/写盘/解析器/发射器/校验用例；真实 Luban 5.1.0 生成物的 ConfigManager 端到端用例；
  示例产物防腐测试。

### Verified

- Luban 5.1.0（`Luban.7z` SHA256 `BAC9A1B8D69CFEAA7EF1D8C67B34A007324D1CD158E0B14294521B3879BA5213`）
  端到端导表：`cs-bin + bin`、`cs-simple-json + json` 均成功；
- 生成物与官方 Unity 运行时 `com.code-philosophy.luban` v1.2.0 兼容（两种 target 均编译并运行通过）；
- `Tables` 在 `namespace cfg`；数据每表一文件；`DataMap` 运行期是 `Dictionary<TKey,T>`；
  带索引 list 表无公开 `DataMap`，已通过 `DataList` + 键表达式重建验证。
