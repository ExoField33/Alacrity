@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "EXIT_CODE=0"

rem Builds Alacrity directly inside this cloned repository folder.
rem The cloned repository is expected to be directly inside the Terraria folder.

set "REPO_DIR=%~dp0"
for %%I in ("%REPO_DIR%.") do set "REPO_DIR=%%~fI"
if "%ALACRITY_TERRARIA_DIRECTORY%"=="" (
    for %%I in ("%REPO_DIR%\..") do set "TERRARIA_DIR=%%~fI"
) else (
    for %%I in ("%ALACRITY_TERRARIA_DIRECTORY%") do set "TERRARIA_DIR=%%~fI"
)

set "CLIENT_DIR=%REPO_DIR%"

if not exist "%TERRARIA_DIR%\Terraria.exe" (
    echo ERROR: Terraria.exe was not found beside this clone.
    echo Clone the repository directly into a vanilla Terraria folder, or run this script from that layout.
    set "EXIT_CODE=1"
    goto :Finish
)

if not "%~1"=="" (
    echo ERROR: This builder always deploys into the folder containing BuildAlacrityClient.bat.
    set "EXIT_CODE=1"
    goto :Finish
)

set "XNA_DIR=%ALACRITY_XNA_REFERENCE_DIRECTORY%"
if "%XNA_DIR%"=="" set "XNA_DIR=%WINDIR%\Microsoft.NET\assembly\GAC_32"

if not exist "%XNA_DIR%\Microsoft.Xna.Framework\v4.0_4.0.0.0__842cf8be1de50553\Microsoft.Xna.Framework.dll" (
    echo ERROR: Microsoft XNA Framework 4.0 references were not found.
    echo Set ALACRITY_XNA_REFERENCE_DIRECTORY to the directory containing the Microsoft.Xna.Framework GAC folders.
    set "EXIT_CODE=1"
    goto :Finish
)

echo.
echo [1/5] Copying vanilla Terraria files into:
echo         %CLIENT_DIR%
robocopy "%TERRARIA_DIR%" "%CLIENT_DIR%" /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XD "%REPO_DIR%" "%TERRARIA_DIR%\AlacrityClient"
set "ROBOCOPY_EXIT=%ERRORLEVEL%"
if %ROBOCOPY_EXIT% GEQ 8 (
    echo ERROR: Robocopy failed with exit code %ROBOCOPY_EXIT%.
    set "EXIT_CODE=%ROBOCOPY_EXIT%"
    goto :Finish
)

if exist "%REPO_DIR%\assets\Alacrity-Logo.png" (
    if not exist "%CLIENT_DIR%\assets" mkdir "%CLIENT_DIR%\assets"
    copy /Y "%REPO_DIR%\assets\Alacrity-Logo.png" "%CLIENT_DIR%\assets\Alacrity-Logo.png" >nul
)

echo [2/5] Building one coherent Alacrity runtime stage...
dotnet build "%REPO_DIR%\src\Alacrity.TerrariaIntegration\Alacrity.RuntimeStaging.csproj" -c Release -p:AlacrityTerrariaAssemblyPath="%TERRARIA_DIR%\Terraria.exe" -p:AlacrityXnaReferenceDirectory="%XNA_DIR%"
if errorlevel 1 (
    set "EXIT_CODE=%ERRORLEVEL%"
    goto :Finish
)

echo [3/5] Deploying runtime assemblies and bundled plugins...
dotnet msbuild "%REPO_DIR%\src\Alacrity.TerrariaIntegration\Alacrity.RuntimeDeployment.csproj" -t:DeployAlacrityRuntime -p:AlacrityRuntimeDeployDirectory="%CLIENT_DIR%"
if errorlevel 1 (
    set "EXIT_CODE=%ERRORLEVEL%"
    goto :Finish
)

echo [4/5] Building the version-locked Terraria patch tool...
dotnet build "%REPO_DIR%\tools\Alacrity.ClientBuilder\Alacrity.ClientBuilder.csproj" -c Release
if errorlevel 1 (
    set "EXIT_CODE=%ERRORLEVEL%"
    goto :Finish
)

echo [5/5] Creating Alacrity.exe from the copied vanilla executable...
set "ALACRITY_XNA_REFERENCE_DIRECTORY=%XNA_DIR%"
dotnet run --project "%REPO_DIR%\tools\Alacrity.ClientBuilder\Alacrity.ClientBuilder.csproj" -c Release --no-build -- patch-alacrity "%CLIENT_DIR%\Terraria.exe"
if errorlevel 1 (
    set "EXIT_CODE=%ERRORLEVEL%"
    goto :Finish
)

if not exist "%CLIENT_DIR%\Alacrity.exe" (
    echo ERROR: The patch tool completed without creating Alacrity.exe.
    set "EXIT_CODE=1"
    goto :Finish
)

echo.
echo Alacrity client ready:
echo   %CLIENT_DIR%\Alacrity.exe
echo Vanilla Terraria.exe remains unchanged at:
echo   %TERRARIA_DIR%\Terraria.exe

:Finish
echo.
echo Press a Key to close this menu
pause >nul
exit /b %EXIT_CODE%
