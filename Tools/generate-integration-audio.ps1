<#
.SYNOPSIS
    为 Integration 示例合成 4 个 PCM 16-bit mono 44.1kHz WAV。

.DESCRIPTION
    示例不想在仓库里放二进制音频的“来源不明”文件；用这个脚本可重复生成同一批波形。
    默认输出到 `Assets/XFramework/Samples~/Integration/Audio/`。

.EXAMPLE
    pwsh -File Tools/generate-integration-audio.ps1
#>
param(
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) {
    $OutDir = Join-Path $repoRoot "Assets/XFramework/Samples~/Integration/Audio"
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

$sampleRate = 44100

function Write-Wav([string]$path, [int]$sampleCount, [scriptblock]$sampleAt) {
    $dataSize = $sampleCount * 2
    $fs = [IO.File]::Create($path)
    try {
        $bw = New-Object IO.BinaryWriter($fs)
        try {
            $bw.Write([Text.Encoding]::ASCII.GetBytes("RIFF"))
            $bw.Write([int](36 + $dataSize))
            $bw.Write([Text.Encoding]::ASCII.GetBytes("WAVE"))
            $bw.Write([Text.Encoding]::ASCII.GetBytes("fmt "))
            $bw.Write([int]16)
            $bw.Write([int16]1)        # PCM
            $bw.Write([int16]1)        # mono
            $bw.Write([int]$sampleRate)
            $bw.Write([int]($sampleRate * 2))
            $bw.Write([int16]2)
            $bw.Write([int16]16)
            $bw.Write([Text.Encoding]::ASCII.GetBytes("data"))
            $bw.Write([int]$dataSize)

            for ($i = 0; $i -lt $sampleCount; $i++) {
                $t = $i / $sampleRate
                $v = & $sampleAt $t
                if ($v -gt 1.0) { $v = 1.0 }
                if ($v -lt -1.0) { $v = -1.0 }
                $s = [int16]($v * 30000)
                $bw.Write($s)
            }
        }
        finally {
            $bw.Dispose()
        }
    }
    finally {
        $fs.Dispose()
    }
}

# BGM：4 秒循环，220Hz + 330Hz 的柔和和弦
$bgmSamples = 4 * $sampleRate
Write-Wav (Join-Path $OutDir "Integration.Bgm.wav") $bgmSamples {
    param($t)
    $env = 0.55 + 0.45 * [Math]::Sin(2 * [Math]::PI * 0.5 * $t)
    return $env * 0.10 * ([Math]::Sin(2 * [Math]::PI * 220 * $t) + 0.6 * [Math]::Sin(2 * [Math]::PI * 330 * $t))
}

# Click：80ms 短促点击
Write-Wav (Join-Path $OutDir "Integration.Click.wav") ([int](0.08 * $sampleRate)) {
    param($t)
    $env = [Math]::Exp(-$t * 45.0)
    return 0.35 * $env * [Math]::Sin(2 * [Math]::PI * 880 * $t)
}

# Correct：250ms 上扬
Write-Wav (Join-Path $OutDir "Integration.Correct.wav") ([int](0.25 * $sampleRate)) {
    param($t)
    $env = [Math]::Exp(-$t * 10.0)
    $freq = 660.0 + 1320.0 * $t
    return 0.35 * $env * [Math]::Sin(2 * [Math]::PI * $freq * $t)
}

# Wrong：300ms 低频嗡声
Write-Wav (Join-Path $OutDir "Integration.Wrong.wav") ([int](0.3 * $sampleRate)) {
    param($t)
    $env = [Math]::Exp(-$t * 8.0)
    return 0.30 * $env * [Math]::Sign([Math]::Sin(2 * [Math]::PI * 160 * $t))
}

Write-Host "已生成音频：$OutDir"
Get-ChildItem $OutDir -Filter *.wav | Select-Object Name, Length
