@echo off
cd /d "%~dp0"
set "latest="
if exist "latest-build.txt" set /p latest=<latest-build.txt
if defined latest if exist "%latest%\TunerFest Set Builder.exe" (
 start "" "%latest%\TunerFest Set Builder.exe"
 exit /b
)
if exist "dist\TunerFest Set Builder.exe" (
 start "" "dist\TunerFest Set Builder.exe"
) else (
 dotnet run --project SetBuilder.csproj -c Release
)
