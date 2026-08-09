@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "EXIT_CODE=0"

rem Builds the generated Alacrity client beside this source clone.
rem The cloned repository is expected to be directly inside the Terraria folder.

set "REPO_DIR=%~dp0"
for %%I in ("%REPO_DIR%.") do set "REPO_DIR=%%~fI"
if "%ALACRITY_TERRARIA_DIRECTORY%"=="" (
    for %%I in ("%REPO_DIR%\..") do set "TERRARIA_DIR=%%~fI"
) else (
    for %%I in ("%ALACRITY_TERRARIA_DIRECTORY%") do set "TERRARIA_DIR=%%~fI"
)

set "CLIENT_DIR=%TERRARIA_DIR%\AlacrityClient"

if not exist "%TERRARIA_DIR%\Terraria.exe" (
    echo ERROR: Terraria.exe was not found beside this clone.
    echo Clone the repository directly into a vanilla Terraria folder, or run this script from that layout.
    set "EXIT_CODE=1"
    goto :Finish
)

if not "%~1"=="" (
    echo ERROR: This builder does not accept an output argument. It creates AlacrityClient beside the source clone.
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
if not exist "%CLIENT_DIR%" mkdir "%CLIENT_DIR%"
robocopy "%TERRARIA_DIR%" "%CLIENT_DIR%" /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XD "%REPO_DIR%" "%CLIENT_DIR%"
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
dotnet build "%REPO_DIR%\src\Alacrity.TerrariaIntegration\Alacrity.RuntimeStaging.csproj" -c Release -p:GenerateDocumentationFile=false -p:AlacrityTerrariaAssemblyPath="%TERRARIA_DIR%\Terraria.exe" -p:AlacrityXnaReferenceDirectory="%XNA_DIR%"
if errorlevel 1 (
    set "EXIT_CODE=%ERRORLEVEL%"
    goto :Finish
)

echo [3/5] Building the authoritative version-locked client builder...
dotnet build "%REPO_DIR%\tools\Alacrity.ClientBuilder\Alacrity.ClientBuilder.csproj" -c Release
if errorlevel 1 (
    set "EXIT_CODE=%ERRORLEVEL%"
    goto :Finish
)

echo [4/5] Validating the clean vanilla Terraria executable...
set "ALACRITY_XNA_REFERENCE_DIRECTORY=%XNA_DIR%"
dotnet run --project "%REPO_DIR%\tools\Alacrity.ClientBuilder\Alacrity.ClientBuilder.csproj" -c Release --no-build -- validate --source "%TERRARIA_DIR%\Terraria.exe"
if errorlevel 1 (
    set "EXIT_CODE=%ERRORLEVEL%"
    goto :Finish
)

echo [5/5] Validating the staged runtime and creating Alacrity.exe...
set "ALACRITY_XNA_REFERENCE_DIRECTORY=%XNA_DIR%"
dotnet run --project "%REPO_DIR%\tools\Alacrity.ClientBuilder\Alacrity.ClientBuilder.csproj" -c Release --no-build -- generate --source "%TERRARIA_DIR%\Terraria.exe" --runtime "%REPO_DIR%\artifacts\runtime" --output "%CLIENT_DIR%" --deploy --verbose
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
