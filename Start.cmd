@echo off
cd /d "%~dp0"
if exist "dist-v1.4.6\TunerFest Set Builder.exe" (
 start "" "dist-v1.4.6\TunerFest Set Builder.exe"
) else if exist "dist\TunerFest Set Builder.exe" (
 start "" "dist\TunerFest Set Builder.exe"
) else (
 dotnet run --project SetBuilder.csproj -c Release
)






