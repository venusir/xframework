<#
.SYNOPSIS
    以 Unity batchmode 定向运行单元测试。

.DESCRIPTION
    两种用法：改完一个模块用 -Filter 定向跑（日常）；阶段收尾跑全量，**全量 0 失败即门禁**。
    全量能当门禁的前提是各 fixture 都复位自己触碰的静态门面——PlayMode 下所有用例共享一个
    player 实例，不复位即互相污染。历史上全量确实是一片红（那批跨 fixture 泄漏、错误期望值、
    漏 `_root.Start()` 等缺陷已于 2026-09-13 前修净），此后稳定 0 失败；若哪天又变红，
    先查是不是新 fixture 漏了复位，而不是把结果当作噪音丢掉。全量跑还会与上次的用例总数
    比较、骤降时告警（见 -ShrinkTolerance）——「0 失败」不足以说明测试集健康。

    默认使用仓库旁的测试运行壳（<仓库名>.TestRun），它通过 junction 共享本仓库的
    Assets/Packages/ProjectSettings 而拥有独立 Library——这样跑测试**不需要关闭编辑器**
    （两份额外 Library 不争锁），且 Unity 为新文件生成的 .meta 会直接落在真实仓库里。

    壳不存在时回退到本仓库运行，此时必须先关闭编辑器，否则会争 Library 锁。

.PARAMETER Filter
    测试过滤器，如 XFramework.XSettings.Tests.SettingsDefaultValueTests 或类名的一部分。
    留空则跑全量，即门禁：应为 0 失败。

.PARAMETER Platform
    PlayMode（默认，Tests/Runtime 下的用例都在这里）或 EditMode。

.PARAMETER ShrinkTolerance
    全量跑时，用例总数比上次下降超过这个数量就告警（默认 5），用于发现「测试集静默缩水」
    ——asmdef 坏了、fixture 没被编进来、或某个 `[TestFixture]` 被误删时，runner 照样报
    「0 失败」，光看失败数发现不了。基线按 Platform 分开记在 TestResults/last-count-<Platform>.txt；
    确实有意删掉一批用例时，删掉该文件即可重置基线（下次跑就是新基线）。

.PARAMETER UnityPath
    显式指定 Unity.exe。留空则按 ProjectSettings/ProjectVersion.txt 的版本自动探测。

.PARAMETER UseRepo
    强制在仓库本体运行而非测试壳（需先关闭编辑器）。

.PARAMETER Setup
    创建测试壳后退出。新机器上跑一次即可；已存在的联接会跳过，不会删除任何目录。

.EXAMPLE
    pwsh -File Tools/run-tests.ps1 -Setup                # 新机器上先建壳
    pwsh -File Tools/run-tests.ps1 -Filter SettingsDirtyTests
    pwsh -File Tools/run-tests.ps1                       # 全量（门禁：应为 0 失败）
#>
param(
    [string]$Filter = "",
    [ValidateSet("PlayMode", "EditMode")]
    [string]$Platform = "PlayMode",
    [int]$ShrinkTolerance = 5,
    [string]$UnityPath = "",
    [switch]$UseRepo,
    [switch]$Setup
)

$ErrorActionPreference = "Stop"

# ---------- 定位仓库与 Unity ----------

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "ProjectSettings\ProjectVersion.txt"))) {
    Write-Error "找不到仓库根（本脚本应位于 <仓库>/Tools/ 下）：$repoRoot"
}

# 版本号从 ProjectVersion.txt 读，避免硬编码（旧脚本硬编码路径，换机器即失效）
$versionLine = Get-Content (Join-Path $repoRoot "ProjectSettings\ProjectVersion.txt") |
    Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
$version = ($versionLine -split ':', 2)[1].Trim()
Write-Host "Unity 版本: $version"

if (-not $UnityPath) {
    $candidates = @(
        "D:\Program Files\Unity\$version\Editor\Unity.exe",
        "$env:ProgramFiles\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "$env:LOCALAPPDATA\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
    )
    $UnityPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $UnityPath) {
        Write-Error "未找到 Unity $version 的编辑器。请用 -UnityPath 显式指定。`n已探测:`n$($candidates -join "`n")"
    }
}
Write-Host "Unity 路径: $UnityPath"

# ---------- 选择运行位置 ----------

$shellPath = Join-Path (Split-Path -Parent $repoRoot) ((Split-Path -Leaf $repoRoot) + ".TestRun")

if ($Setup) {
    New-Item -ItemType Directory -Force -Path $shellPath | Out-Null
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        $link = Join-Path $shellPath $d
        if (Test-Path $link) { Write-Host "已存在，跳过: $link"; continue }
        New-Item -ItemType Junction -Path $link -Target (Join-Path $repoRoot $d) | Out-Null
        Write-Host "已建立联接: $link -> $(Join-Path $repoRoot $d)"
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $shellPath "TestResults") | Out-Null
    Write-Host ""
    Write-Host "测试壳已就绪: $shellPath" -ForegroundColor Green
    Write-Host "首次运行会做一次完整导入（约 1 分钟），之后为增量。"
    Write-Host "注意：Unity 会提示「Assets is a symbolic link」并关闭目录监控。这是我们有意接受的"
    Write-Host "     ——官方警告针对的是「多项目共享同一资源、递归链接、跨 Unity 版本共享」，本方案三者皆无。"
    exit 0
}

$useShell = (-not $UseRepo) -and (Test-Path (Join-Path $shellPath "Assets"))

if ($useShell) {
    $projectPath = $shellPath
    Write-Host "运行位置: 测试壳 $projectPath（编辑器可保持开启）"
} else {
    $projectPath = $repoRoot
    if (-not $UseRepo) {
        Write-Warning "测试壳不存在（$shellPath），回退到仓库本体运行。"
    }
    Write-Warning "在仓库本体运行需要先关闭 Unity 编辑器，否则会争 Library 锁。"
}

# ---------- 组装参数 ----------

$resultsDir = Join-Path $projectPath "TestResults"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$resultsFile = Join-Path $resultsDir "run-$stamp.xml"
$logFile = Join-Path $resultsDir "run-$stamp.log"

# 注意：绝不能加 -quit。-quit 的语义是「其他命令行指令执行完即退出」，而 -runTests 是
# 异步启动测试后立即返回，两者组合会让进程在测试跑完前退出——现象是 exit=0 但没有结果文件。
# -runTests 自己会在测试结束后退出，无需 -quit。（历史脚本带了 -quit，是个隐藏缺陷）
$unityArgs = @(
    "-batchmode", "-nographics",
    "-projectPath", $projectPath,
    "-runTests", "-testPlatform", $Platform,
    "-testResults", $resultsFile,
    "-logFile", $logFile
)

if ($Filter) {
    Write-Host "过滤器: $Filter"
    $unityArgs += "-testFilter"
    $unityArgs += $Filter
} else {
    Write-Host "未指定 -Filter：跑全量（门禁：应为 0 失败）" -ForegroundColor Cyan
}

# ---------- 运行 ----------

Write-Host "开始运行..." -ForegroundColor Cyan
$sw = [Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -NoNewWindow -PassThru -Wait
$sw.Stop()
Write-Host "退出码 $($proc.ExitCode)，耗时 $([math]::Round($sw.Elapsed.TotalSeconds,1))s"

# ---------- 解析结果 ----------

if (-not (Test-Path $resultsFile)) {
    Write-Host "没有结果文件——测试未执行。常见原因：" -ForegroundColor Red
    Write-Host "  1) 误加了 -quit（本脚本不加，若你手动跑请去掉）"
    Write-Host "  2) 首次导入吃掉了整个进程（Library 冷启动），重跑一次即可"
    Write-Host "  3) 项目编译失败，详见 $logFile"
    if (Test-Path $logFile) {
        Write-Host "`n--- 日志中的编译错误 ---" -ForegroundColor DarkGray
        Select-String -Path $logFile -Pattern "error CS" | Select-Object -First 15 |
            ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor DarkGray }
    }
    exit 2
}

[xml]$xml = Get-Content $resultsFile
$run = $xml.'test-run'
Write-Host ""
Write-Host "================ 结果 ================" -ForegroundColor Cyan
Write-Host "总计 $($run.total)  通过 $($run.passed)  失败 $($run.failed)  跳过 $($run.skipped)"

if ([int]$run.failed -gt 0) {
    Write-Host ""
    Write-Host "失败用例：" -ForegroundColor Red
    $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
        Write-Host "  X $($_.fullname)" -ForegroundColor Red
        $msg = $_.SelectSingleNode("failure/message")
        if ($msg) { Write-Host "      $($msg.InnerText.Trim())" -ForegroundColor DarkRed }
    }
}

Write-Host "结果文件: $resultsFile"

# ---------- 用例总数基线（防「测试集静默缩水」）----------

# 「0 失败」不足以说明测试集健康：asmdef 坏了、fixture 没被编进来、或某个 [TestFixture] 被
# 误删时，runner 报的同样是 0 失败，只是总数变小了。故记住上次的总数，骤降即告警。
#
# 三条守卫，缺一条就会天天误报：
#   1) 只在全量跑时比较——-Filter 的 total 只是一个子集，拿去比全量基线必然「骤降」
#   2) 总数 > 0 才写基线——编译失败会产出 total=0 的结果文件，写进基线会污染此后所有比较
#   3) 按 Platform 分开记——EditMode 的用例数远小于 PlayMode
$total = [int]$run.total
if (-not $Filter -and $total -gt 0) {
    $baselineFile = Join-Path $resultsDir "last-count-$Platform.txt"
    $previous = 0
    if ((Test-Path $baselineFile) -and
        [int]::TryParse((Get-Content $baselineFile -Raw).Trim(), [ref]$previous) -and
        ($previous - $total) -gt $ShrinkTolerance) {
        Write-Warning "用例总数从 $previous 降到 $total（减少 $($previous - $total)）。若非有意删减，先查测试集是否没被完整编进来：asmdef、编译错误、误删的 [TestFixture]。确认无误后删掉 $baselineFile 即可重置基线。"
    }
    Set-Content -Path $baselineFile -Value $total -Encoding ascii
}

exit ([int]$run.failed -eq 0 ? 0 : 1)
