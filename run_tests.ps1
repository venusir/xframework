# XFramework 单元测试运行脚本
# 使用方法: powershell -File run_tests.ps1 [-testFilter "TestNameFilter"]
# 示例: powershell -File run_tests.ps1 -testFilter "LockService"

param(
    [string]$testFilter = "",
    [string]$testPlatform = "EditMode"
)

$UnityPath = "D:\Program Files\Unity\6000.4.5f1\Editor\Unity.exe"
$ProjectPath = "E:\UnityProjects\XFramework"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$TestResultsFile = "$ProjectPath\TestResults_$Timestamp.xml"
$LogFile = "$ProjectPath\TestLog_$Timestamp.txt"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  XFramework Unit Test Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Platform: $testPlatform"
Write-Host "Filter: $($testFilter -replace $testFilter, 'All tests')"
Write-Host "Results: $TestResultsFile"
Write-Host "Log: $LogFile"
Write-Host ""

$args = @(
    "-projectPath", $ProjectPath,
    "-batchmode",
    "-nographics",
    "-runTests",
    "-testPlatform", $testPlatform,
    "-testResults", $TestResultsFile,
    "-logFile", $LogFile,
    "-quit"
)

if ($testFilter) {
    $args += "-testFilter"
    $args += $testFilter
}

Write-Host "Starting Unity test run..." -ForegroundColor Yellow
$startTime = Get-Date

# Use Start-Process with -Wait to ensure we wait for Unity to finish
$process = Start-Process -FilePath $UnityPath -ArgumentList $args -NoNewWindow -PassThru -Wait
$exitCode = $process.ExitCode

$endTime = Get-Date
$duration = ($endTime - $startTime).TotalSeconds
Write-Host ""
Write-Host "Unity exit code: $exitCode" -ForegroundColor $(if ($exitCode -eq 0) { "Green" } else { "Red" })

if (Test-Path $TestResultsFile) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Test Results Summary" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Duration: $([math]::Round($duration, 2))s"

    # Parse XML results for summary
    [xml]$xml = Get-Content $TestResultsFile
    $total = $xml.'test-run'.total
    $passed = $xml.'test-run'.passed
    $failed = $xml.'test-run'.failed
    $skipped = $xml.'test-run'.skipped

    Write-Host "Total:  $total"
    Write-Host "Passed: $passed" -ForegroundColor Green
    if ($failed -gt 0) {
        Write-Host "Failed: $failed" -ForegroundColor Red

        # List failed tests
        Write-Host ""
        Write-Host "Failed Tests:" -ForegroundColor Red
        $failures = $xml.SelectNodes("//test-case[@result='Failed']")
        foreach ($fail in $failures) {
            Write-Host "  ✗ $($fail.fullname)" -ForegroundColor Red
            $msg = $fail.SelectSingleNode("failure/message")
            if ($msg) {
                Write-Host "    Message: $($msg.InnerText.Trim())" -ForegroundColor DarkRed
            }
            $stack = $fail.SelectSingleNode("failure/stack-trace")
            if ($stack) {
                Write-Host "    Stack: $($stack.InnerText.Trim())" -ForegroundColor DarkGray
            }
        }
    }
    else {
        Write-Host "Failed: $failed" -ForegroundColor Green
    }
    Write-Host "Skipped: $skipped"
    Write-Host ""
}
else {
    Write-Host "ERROR: Test results file not found!" -ForegroundColor Red
    if (Test-Path $LogFile) {
        Write-Host "Check the log file for details: $LogFile" -ForegroundColor Yellow
        Get-Content $LogFile -Tail 30
    }
}