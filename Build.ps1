param([switch]$SkipTests)
# Publishes a self-contained win-x64 build into dist-v<version>, where <version> comes only from SetBuilder.csproj.
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $version = ([xml](Get-Content SetBuilder.csproj -Raw)).Project.PropertyGroup.Version
    if (-not $version) { throw 'Version is missing in SetBuilder.csproj' }
    $output = "dist-v$version"
    if (-not $SkipTests) { & "$PSScriptRoot\Test.ps1" }
    dotnet publish SetBuilder.csproj -c Release -r win-x64 --self-contained true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    # Start.cmd reads this file, so the launcher never needs editing when the version changes.
    Set-Content -Path 'latest-build.txt' -Value $output -NoNewline
    Write-Host "Ready: $output\TunerFest Set Builder.exe"
} finally { Pop-Location }
