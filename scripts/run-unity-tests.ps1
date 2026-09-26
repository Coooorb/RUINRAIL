param(
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$TestPlatform = "EditMode",
    # Optional Unity -testFilter. A filtered run writes <Platform>-targeted-results.xml so it never overwrites the
    # full-suite results that audits read.
    [string]$TestFilter = "",
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$UnityVersion = "6000.3.24f1"

function Resolve-UnityExecutable {
    if ($env:UNITY_PATH -and (Test-Path $env:UNITY_PATH)) {
        return (Resolve-Path $env:UNITY_PATH).Path
    }

    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe",
        "C:\Program Files (x86)\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    throw "Unity $UnityVersion not found. Set UNITY_PATH to the Unity executable."
}

function Invoke-UnityTests([string]$Platform, [string]$UnityExecutable) {
    $resultsDir = Join-Path $ProjectPath "TestResults"
    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

    $suffix = if ($TestFilter) { "-targeted" } else { "" }
    $resultFile = Join-Path $resultsDir "$Platform$suffix-results.xml"
    $logFile = Join-Path $resultsDir "$Platform$suffix-unity.log"
    Remove-Item $resultFile -Force -ErrorAction SilentlyContinue

    Write-Host "Running RUINRAIL $Platform tests with $UnityExecutable"
    $unityArgs = @(
        "-batchmode",
        "-runTests",
        "-projectPath", $ProjectPath,
        "-testPlatform", $Platform,
        "-testResults", $resultFile,
        "-logFile", $logFile
    )
    if ($TestFilter) {
        $unityArgs += @("-testFilter", $TestFilter)
    }

    # EditMode is safe to run headless. PlayMode intentionally keeps graphics enabled
    # because some PlayMode tests may require a graphics device/render loop.
    if ($Platform -eq "EditMode") {
        $unityArgs += "-nographics"
    }

    # Unity.exe is a GUI-subsystem binary, so the call operator returns as soon as it is launched and leaves
    # $LASTEXITCODE unset. Start-Process -Wait -PassThru blocks until the batch-mode run really ends and carries the
    # process exit code back, which is what every check below depends on.
    $process = Start-Process -FilePath $UnityExecutable -ArgumentList $unityArgs -PassThru
    $process.WaitForExit()
    $exit = $process.ExitCode

    if ($null -eq $exit) {
        throw "Unity $Platform produced no exit code; the run cannot be verified. See $logFile."
    }
    if ($exit -ne 0) {
        throw "Unity $Platform tests failed with exit code $exit. See $logFile and $resultFile."
    }

    if (-not (Test-Path $resultFile)) {
        throw "Unity returned success but no test result file was produced: $resultFile. See $logFile."
    }

    try {
        [xml]$results = Get-Content -Raw -Path $resultFile
    } catch {
        throw "Unity returned an unreadable test result XML: $resultFile. $($_.Exception.Message)"
    }

    $testRun = $results.'test-run'
    if ($null -eq $testRun) {
        throw "Test result XML has no <test-run> root: $resultFile"
    }

    $total = 0
    $passed = 0
    $failed = 0
    if (-not [int]::TryParse([string]$testRun.total, [ref]$total)) {
        throw "Could not read the test-run total count from $resultFile"
    }
    [void][int]::TryParse([string]$testRun.passed, [ref]$passed)
    [void][int]::TryParse([string]$testRun.failed, [ref]$failed)

    if ($total -le 0) {
        throw "$Platform reported 0 tests. Check test assembly definitions, references, and test discovery."
    }
    if ($failed -gt 0) {
        throw "$Platform reported $failed failed test(s) out of $total. See $resultFile and $logFile."
    }
    if ($passed -le 0) {
        throw "$Platform discovered $total test(s) but reported 0 passed tests. Check skipped/inconclusive tests in $resultFile."
    }

    Write-Host "PASS: $Platform — $passed passed / $total discovered. Results: $resultFile"
}

$unity = Resolve-UnityExecutable

if ($TestPlatform -eq "All") {
    $failures = @()
    foreach ($platform in @("EditMode", "PlayMode")) {
        try {
            Invoke-UnityTests $platform $unity
        } catch {
            $failures += "${platform}: $($_.Exception.Message)"
            Write-Error -ErrorAction Continue "${platform}: $($_.Exception.Message)"
        }
    }

    if ($failures.Count -gt 0) {
        throw "$($failures.Count) test platform(s) failed. Both EditMode and PlayMode were attempted. Inspect TestResults/."
    }

    Write-Host "PASS: EditMode and PlayMode test suites completed successfully."
} else {
    Invoke-UnityTests $TestPlatform $unity
}
