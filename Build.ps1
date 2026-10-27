$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish SetBuilder.csproj -c Release -r win-x64 --self-contained true -o dist-v1.4.6
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    Write-Host 'Ready: dist-v1.4.6\TunerFest Set Builder.exe'
} finally { Pop-Location }







