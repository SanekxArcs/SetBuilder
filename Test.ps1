param(
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)
# Runs every offline verification mode of the app. Nothing touches the game.
# Each mode writes a *-test.txt / *.json report next to the executable and must exit with 0.
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $SkipBuild) {
        dotnet build SetBuilder.csproj -c $Configuration -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    }
    $exe = Get-ChildItem -Path "bin\$Configuration" -Recurse -Filter 'TunerFest Set Builder.exe' | Select-Object -First 1
    if (-not $exe) { throw 'Executable not found; build first.' }
    $modes = @(
        '--self-test', '--verify-workflow', '--verify-focus', '--verify-recovery', '--verify-analytics', '--ui-smoke',
        '--verify-calibration "test-results\calibration"',
        '--verify-stuck "test-results\stuck-clean-driver.png"',
        '--verify-value-crop "test-results\extra-impact-2-percent.png" "Extra Impact 2% / 4%"',
        '--verify-value-crop "test-results\extra-impact-2-percent-b.png" "Extra Impact 2% / 4%"',
        '--verify-value-crop "test-results\extra-impact-2-percent-c.png" "Extra Impact 2% / 4%"',
        '--verify-value-crop "test-results\cluster-1-2-percent.png" "Cluster 1.2% / 2%"',
        '--verify-value-crop "test-results\cluster-2-percent-maxed.png" "Cluster 2% / 2%"',
        '--verify-grid-rarity "test-results\grid-rarity-a.png" "xGPxGPG focus 4"',
        '--verify-grid-rarity "test-results\grid-rarity-b.png" "xGxGGPG focus 3"'
    )
    $failed = @()
    foreach ($mode in $modes) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        # WinExe returns immediately from the shell; Start-Process -Wait collects the real exit code.
        $process = Start-Process -FilePath $exe.FullName -ArgumentList $mode -Wait -PassThru -NoNewWindow
        $code = $process.ExitCode
        $status = if ($code -eq 0) { 'PASS' } else { "FAIL ($code)"; $failed += $mode }
        Write-Host ("{0,-22} {1,-10} {2,6:N1}s" -f $mode, $status, $sw.Elapsed.TotalSeconds)
    }
    $reports = Get-ChildItem -Path $exe.DirectoryName -Filter '*-test.txt' | ForEach-Object { (Get-Content $_.FullName | Where-Object { $_ -like 'PASS *' }).Count }
    Write-Host ("Checks passed: {0}" -f ($reports | Measure-Object -Sum).Sum)
    if ($failed.Count) { throw ('Failed: ' + ($failed -join ', ')) }
    Write-Host 'All verification modes passed.'
}
finally { Pop-Location }
