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

    **门禁默认覆盖两个平台**：runner 一次只能跑一个平台，而用例分居 Tests/Runtime（PlayMode）
    与 Tests/Editor（EditMode，含 Pool / Config / Pipeline / Localization / Asset / Bootstrap
    各模块）。只跑一个平台时「全量 0 失败」覆盖不到另一半，因此 -Platform 默认为 All，
    顺序跑两个平台、任一有失败即非 0 退出。代价是门禁耗时约为单平台的两倍。

    默认使用仓库旁的测试运行壳（<仓库名>.TestRun），它通过 junction 共享本仓库的
    Assets/Packages/ProjectSettings 而拥有独立 Library——这样跑测试**不需要关闭编辑器**
    （两份额外 Library 不争锁），且 Unity 为新文件生成的 .meta 会直接落在真实仓库里。

    壳不存在时回退到本仓库运行，此时必须先关闭编辑器，否则会争 Library 锁。

.PARAMETER Filter
    测试过滤器，**正则**（Unity -testFilter 的语义，不是子串匹配），如
    XFramework.XSettings.Tests.SettingsDefaultValueTests 或类名的一部分；点号记得转义，
    否则 `.` 会被当成「任意字符」而捞到别的 fixture。
    留空则跑全量，即门禁：应为 0 失败。
    注意过滤器只在自己的平台内匹配：给过滤器的同时未显式指定 -Platform 时只跑 PlayMode
    （保持日常定向跑的耗时与手感）；要跑 EditMode 侧的过滤器请显式 -Platform EditMode。

.PARAMETER Fixture
    按 fixture（测试类）名精确匹配，如 `PoolTests`。内部生成 `\.PoolTests\.`——用**转义的
    点**锚定类名边界，因此只命中 `Namespace.PoolTests.Method` 这一形态。想当然的写法都会误捞
    （`PoolTests`、`PoolTests.`、`.PoolTests.` 实测都会连 `CollectionPoolTests` 一起捞）。
    与 -Filter 互斥；其余行为（未显式指定平台时只跑 PlayMode、0 命中告警）与 -Filter 相同。

.PARAMETER Platform
    All（默认）、PlayMode（Tests/Runtime 下的用例都在这里）或 EditMode。
    All 顺序跑两个平台并汇总退出码。

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
    pwsh -File Tools/run-tests.ps1 -Filter SettingsDirtyTests          # 定向跑（PlayMode）
    pwsh -File Tools/run-tests.ps1 -Fixture PoolTests -Platform EditMode
    pwsh -File Tools/run-tests.ps1                       # 全量双平台（门禁：应为 0 失败）
#>
param(
    [string]$Filter = "",
    [string]$Fixture = "",
    [ValidateSet("PlayMode", "EditMode", "All")]
    [string]$Platform = "All",
    [int]$ShrinkTolerance = 5,
    [string]$UnityPath = "",
    [switch]$UseRepo,
    [switch]$Setup
)

$ErrorActionPreference = "Stop"

# -Fixture 是 -Filter 的语法糖：把类名包成 **转义的点** 锚定的正则。
#
# 前提：Unity 的 -testFilter 是**正则**，不是子串匹配（实测）。于是类名边界必须用 `\.` 锚定，
# 而 `.` 本身是「任意字符」。两种想当然的写法都实测会误捞：
#   `PoolTests`   → 命中 CollectionPoolTests（名字后缀共享）
#   `PoolTests.`  → 同上（尾点解决不了后缀共享）
#   `.PoolTests.` → 同上（未转义的点匹配任意字符）
# 只有 `\.PoolTests\.` 精确命中 Namespace.PoolTests.Method 这一形态（实测 26 vs 40）。
if ($Fixture) {
    if ($Filter) {
        Write-Error "-Filter 与 -Fixture 互斥：前者是任意正则片段，后者按 fixture 精确匹配，请只给一个。"
    }
    $Filter = "\.$($Fixture -replace '\.', '\.')\."
}

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

$resultsDir = Join-Path $projectPath "TestResults"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

# ---------- 决定跑哪些平台 ----------
#
# 门禁（无过滤器）默认双平台：runner 一次只跑一个平台，而用例分居 Tests/Runtime 与 Tests/Editor，
# 只跑一个平台时「全量 0 失败」覆盖不到另一半——Tests/Editor 下有六个模块的用例。
#
# 但定向跑保持历史手感：给了过滤器又未显式指定平台时只跑 PlayMode，否则 EditMode 那半会用
# 同一个过滤器再跑一遍、多花一倍时间。（过滤器命中 0 个用例时 runner 照样产出结果文件、
# 报「总计 0」，不会失败——所以这里的选择不影响正确性，只影响耗时。）

$platformExplicit = $PSBoundParameters.ContainsKey("Platform")

$platforms = if ($Platform -ne "All") {
    @($Platform)
} elseif ($Filter -and -not $platformExplicit) {
    @("PlayMode")
} else {
    @("PlayMode", "EditMode")
}

# ---------- 逐个平台运行 ----------
#
# 注意：绝不能加 -quit。-quit 的语义是「其他命令行指令执行完即退出」，而 -runTests 是
# 异步启动测试后立即返回，两者组合会让进程在测试跑完前退出——现象是 exit=0 但没有结果文件。
# -runTests 自己会在测试结束后退出，无需 -quit。（历史脚本带了 -quit，是个隐藏缺陷）

$anyFailure = $false
$totalSw = [Diagnostics.Stopwatch]::StartNew()

foreach ($currentPlatform in $platforms) {
    Write-Host ""
    Write-Host "=================== $currentPlatform ===================" -ForegroundColor Cyan

    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $resultsFile = Join-Path $resultsDir "run-$stamp-$currentPlatform.xml"
    $logFile = Join-Path $resultsDir "run-$stamp-$currentPlatform.log"

    $unityArgs = @(
        "-batchmode", "-nographics",
        "-projectPath", $projectPath,
        "-runTests", "-testPlatform", $currentPlatform,
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
        $anyFailure = $true
    }

    if ($Filter -and [int]$run.total -eq 0) {
        Write-Warning "过滤器 '$Filter' 在 $currentPlatform 下命中 0 个用例。过滤器只在自己的平台内匹配：EditMode 侧的用例需显式 -Platform EditMode。"
    }

    Write-Host "结果文件: $resultsFile"

    # ---------- 用例总数基线（防「测试集静默缩水」）----------
    #
    # 「0 失败」不足以说明测试集健康：asmdef 坏了、fixture 没被编进来、或某个 [TestFixture] 被
    # 误删时，runner 报的同样是 0 失败，只是总数变小了。故记住上次的总数，骤降即告警。
    #
    # 三条守卫，缺一条就会天天误报：
    #   1) 只在全量跑时比较——-Filter 的 total 只是一个子集，拿去比全量基线必然「骤降」
    #   2) 总数 > 0 才写基线——编译失败会产出 total=0 的结果文件，写进基线会污染此后所有比较
    #   3) 按 Platform 分开记——EditMode 的用例数远小于 PlayMode
    $total = [int]$run.total
    if (-not $Filter -and $total -gt 0) {
        $baselineFile = Join-Path $resultsDir "last-count-$currentPlatform.txt"
        $previous = 0
        if ((Test-Path $baselineFile) -and
            [int]::TryParse((Get-Content $baselineFile -Raw).Trim(), [ref]$previous) -and
            ($previous - $total) -gt $ShrinkTolerance) {
            Write-Warning "用例总数从 $previous 降到 $total（减少 $($previous - $total)）。若非有意删减，先查测试集是否没被完整编进来：asmdef、编译错误、误删的 [TestFixture]。确认无误后删掉 $baselineFile 即可重置基线。"
        }
        Set-Content -Path $baselineFile -Value $total -Encoding ascii
    }
}

$totalSw.Stop()
if ($platforms.Count -gt 1) {
    Write-Host ""
    Write-Host "合计耗时 $([math]::Round($totalSw.Elapsed.TotalSeconds,1))s（$($platforms -join ' + ')）"
}

exit ($anyFailure ? 1 : 0)
