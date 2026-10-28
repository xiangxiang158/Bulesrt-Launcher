@echo off
setlocal
set ROOT=%~dp0

rem Publishing the launcher first, then the setup exe: the setup embeds the launcher exe as a resource.
rem Requires .NET 8 SDK (https://dotnet.microsoft.com/download)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [x] dotnet not found - please install the .NET 8 SDK first.
  exit /b 1
)

echo [1/3] Publishing launcher ^(single file, framework-dependent^)...
dotnet publish "%ROOT%src\BulesrtLauncher\BulesrtLauncher.csproj" -c Release -r win-x64 ^
  --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false ^
  -p:DebugType=None -o "%ROOT%dist\launcher"
if errorlevel 1 exit /b 1

echo [2/3] Copying launcher into the setup payload...
copy /y "%ROOT%dist\launcher\BulesrtLauncher.exe" "%ROOT%src\BulesrtSetup\payload\BulesrtLauncher.exe" >nul
if errorlevel 1 exit /b 1

echo [3/3] Packing installers ^(keep-data / fresh^)...
dotnet publish "%ROOT%src\BulesrtSetup\BulesrtSetup.csproj" -c Release -r win-x64 ^
  --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false ^
  -p:DebugType=None -o "%ROOT%dist\setup"
if errorlevel 1 exit /b 1

dotnet publish "%ROOT%src\BulesrtSetup\BulesrtSetup.csproj" -c Release -r win-x64 ^
  --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false ^
  -p:DebugType=None -p:FreshBuild=true -o "%ROOT%dist\setup-fresh"
if errorlevel 1 exit /b 1

echo.
echo Done:
echo   dist\launcher\BulesrtLauncher.exe     portable single-file launcher
echo   dist\setup\BulesrtSetup.exe           installer (keeps existing data\)
echo   dist\setup-fresh\BulesrtSetup.exe     installer (fresh: backs up data\ first)
endlocal
