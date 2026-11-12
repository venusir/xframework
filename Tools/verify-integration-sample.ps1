<#
.SYNOPSIS
    一次性验收 Integration 示例：导入副本 + 临时 YooAsset 采集设置 + 运行导入探针。

.DESCRIPTION
    因为 `Samples~/` 对 Unity 的 AssetDatabase 不可见，示例在仓内不能直接被 YooAsset 采集。
    本脚本把示例复制到 `Assets/Samples/Integration`，用一次性 Editor 方法创建唯一一份
    `Assets/AssetBundleCollectorSetting.asset`，然后以 `XFW_INTEGRATION_PROBE=1` 运行
    `IntegrationProbeTests`，最后在 finally 里清理全部临时物。

    前置：测试壳已创建（`Tools/run-tests.ps1 -Setup`）；仓库没有已存在的采集设置（有则拒绝运行）。

.PARAMETER UnityPath
    显式指定 Unity.exe。留空则按 ProjectSettings/ProjectVersion.txt 的版本自动探测。

.EXAMPLE
    pwsh -File Tools/verify-integration-sample.ps1
#>
param(
    [string]$UnityPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$samples = Join-Path $repoRoot "Assets/XFramework/Samples~/Integration"
$imported = Join-Path $repoRoot "Assets/Samples/Integration"
$samplesDir = Join-Path $repoRoot "Assets/Samples"
$tempEditor = Join-Path $repoRoot "Assets/__XfwIntegrationVerify__"
$setting = Join-Path $repoRoot "Assets/AssetBundleCollectorSetting.asset"
$setupSource = Join-Path $repoRoot "Tools/SampleGeneration/IntegrationVerifySetup.cs"
$logPath = Join-Path $repoRoot "Logs/verify-integration.log"

if (-not (Test-Path $samples)) {
    throw "示例目录不存在：$samples"
}
if (-not (Test-Path $setupSource)) {
    throw "验收设置源码不存在：$setupSource"
}

# Unity 版本与路径
$versionFile = Join-Path $repoRoot "ProjectSettings/ProjectVersion.txt"
if (-not (Test-Path $versionFile)) {
    throw "找不到 ProjectSettings/ProjectVersion.txt。"
}
$versionLine = Get-Content $versionFile | Where-Object { $_ -match "^m_EditorVersion:" } | Select-Object -First 1
$version = ($versionLine -split ":", 2)[1].Trim()

if (-not $UnityPath) {
    $candidates = @(
        "$env:ProgramFiles\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "$env:LOCALAPPDATA\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "D:\Program Files\Unity\$version\Editor\Unity.exe",
        "E:\Program Files\Unity\$version\Editor\Unity.exe"
    )
    $UnityPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $UnityPath) {
        throw "未找到 Unity $version 的编辑器。请用 -UnityPath 显式指定。"
    }
}

$shellPath = Join-Path (Split-Path -Parent $repoRoot) ((Split-Path -Leaf $repoRoot) + ".TestRun")
if (-not (Test-Path (Join-Path $shellPath "ProjectSettings/ProjectVersion.txt"))) {
    throw "测试壳不存在：$shellPath。请先运行 Tools/run-tests.ps1 -Setup。"
}

function Remove-IfExists([string]$path) {
    if (Test-Path $path) {
        Remove-Item -Recurse -Force $path
    }
}

function Cleanup {
    Remove-IfExists $imported
    Remove-IfExists "$imported.meta"
    Remove-IfExists $tempEditor
    Remove-IfExists "$tempEditor.meta"
    Remove-IfExists $setting
    Remove-IfExists "$setting.meta"

    # Samples 目录若已空，连目录与 meta 一起清掉。
    if ((Test-Path $samplesDir) -and -not (Get-ChildItem $samplesDir -Force | Select-Object -First 1)) {
        Remove-IfExists $samplesDir
        Remove-IfExists "$samplesDir.meta"
    }

    Remove-IfExists (Join-Path $repoRoot "Bundles")
    Remove-IfExists (Join-Path $shellPath "Bundles")
}

# 0. 清掉上轮残留（若有）
Cleanup

$exitCode = 1
try {
    # 1. 导入副本
    New-Item -ItemType Directory -Force $imported | Out-Null
    Copy-Item -Path (Join-Path $samples "*") -Destination $imported -Recurse -Force
    Write-Host "已复制示例到：$imported"

    # 2. 临时 Editor 设置脚本
    New-Item -ItemType Directory -Force (Join-Path $tempEditor "Editor") | Out-Null
    Copy-Item -Path $setupSource -Destination (Join-Path $tempEditor "Editor/IntegrationVerifySetup.cs") -Force

    # 3. 创建临时采集设置（在测试壳里跑，Assets 是共享的）
    New-Item -ItemType Directory -Force (Split-Path -Parent $logPath) | Out-Null
    if (Test-Path $logPath) {
        Remove-Item -Force $logPath
    }

    $setupArgs = @(
        "-batchmode",
        "-nographics",
        "-projectPath", $shellPath,
        "-executeMethod", "XFramework.Samples.Integration.Editor.IntegrationVerifySetup.CreateCollectorSetting",
        "-quit",
        "-logFile", $logPath
    )

    Write-Host "创建临时 YooAsset 采集设置..."
    $proc = Start-Process -FilePath $UnityPath -ArgumentList $setupArgs -NoNewWindow -PassThru -Wait
    if ($proc.ExitCode -ne 0) {
        throw "采集设置创建失败（退出码 $($proc.ExitCode)）。日志：$logPath"
    }

    # 4. 运行导入探针
    $env:XFW_INTEGRATION_PROBE = "1"
    Write-Host "运行导入探针 IntegrationProbeTests..."
    & (Join-Path $PSScriptRoot "run-tests.ps1") -Fixture IntegrationProbeTests -Platform PlayMode
    $exitCode = $LASTEXITCODE
}
finally {
    Remove-Item Env:\XFW_INTEGRATION_PROBE -ErrorAction SilentlyContinue
    Cleanup
    Write-Host "临时物已清理。"
}

if ($exitCode -ne 0) {
    throw "Integration 导入验收失败（退出码 $exitCode）。"
}

Write-Host "Integration 导入验收通过。"
