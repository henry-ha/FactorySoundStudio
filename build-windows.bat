@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK is required. Download from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
dotnet build src\FactorySoundStudio\FactorySoundStudio.csproj -c Release
if errorlevel 1 (
  echo Build failed. Copy the error text if you need help.
  pause
  exit /b 1
)
echo.
echo Build succeeded. Starting Factory Sound Studio...
dotnet run --project src\FactorySoundStudio\FactorySoundStudio.csproj -c Release --no-build
endlocal
