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
- 包内守卫：README 结构与文件树、`.meta` 完整性、包边界、`AutoInit` 族清单、Runtime 公开 API 基线、
  依赖声明；Luban 示例编译纳入 `check-docs.ps1`（LUB-21/25/27）。

### Fixed

- 设置校验：允许 `Luban.dll` / `luban.conf` 使用绝对路径（LUB-01）；受保护目录精确等于包根也会被拒（LUB-02）；
  code/data 与工具输入目录的重叠检查（LUB-05）；路径解析失败返回可读错误（LUB-16）；
  工具链以项目根定位、batchmode 不弹窗、超时文案跟随常量（LUB-14）。
- 运行时生命周期：并发初始化共享同一次加载/构造/注册，Destroy 或会话复位取消在途且不注册（LUB-03）；
  引导阶段只清理自己初始化的状态（LUB-06）；加载失败保留内层可操作消息（LUB-12）；
  Editor-only 主线程入口守卫（LUB-13）。
- 写盘与生成：写盘先全量 preflight、拒绝时不留半套文件（LUB-08）；stale 生成文件连同 `.meta` 清理（LUB-07）；
  行接口文件名碰撞检测（LUB-09）；loader 类型与数据文件校验（LUB-10）；JSON loader 剥离 UTF-8 BOM（LUB-11）；
  索引表重复键消息带行类型与键（LUB-17）；双平台 asmdef 不再误报（LUB-15）。
- 测试：默认 AssetManager 加载后端的映射/错误覆盖（LUB-04）；消费方 PackageCache 路径定位（LUB-22）；
  两份生成夹具同步守卫（LUB-24）；golden-file 比对（LUB-23）；ConfigChanged 逐表契约锁定（LUB-28）。
- 文档与仓库：README 补 `## 概述`、文件树与已知限制；异常/数据根/Unload 文档按实际行为修正；
  `package.json` 补 `licensesUrl`；`.gitattributes` 显式标注 `*.bytes` 为二进制、`*.xml` /
  `luban.conf` / `.luban-integration-manifest` 为文本。

### Verified

- Luban 5.1.0（`Luban.7z` SHA256 `BAC9A1B8D69CFEAA7EF1D8C67B34A007324D1CD158E0B14294521B3879BA5213`）
  端到端导表：`cs-bin + bin`、`cs-simple-json + json` 均成功；
- 生成物与官方 Unity 运行时 `com.code-philosophy.luban` v1.2.0 兼容（两种 target 均编译并运行通过）；
- `Tables` 在 `namespace cfg`；数据每表一文件；`DataMap` 运行期是 `Dictionary<TKey,T>`；
  带索引 list 表无公开 `DataMap`，已通过 `DataList` + 键表达式重建验证。
