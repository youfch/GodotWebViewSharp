@echo off
REM =============================================
REM GDEWebView - GDExtension Build Script (Windows)
REM =============================================
REM
REM Usage:
REM   build        - Debug build
REM   build debug  - Debug build
REM   build release - Release build
REM   build publish - Publish (AOT native)
REM =============================================

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Debug

set PROJECT_DIR=%~dp0
set EXTENSION_DIR=%PROJECT_DIR%extension
set TEST_DIR=%PROJECT_DIR%test\GDEWebViewGame\lib

echo ========================================
echo GDEWebView Build Script
echo Configuration: %CONFIG%
echo ========================================

if /i "%CONFIG%"=="release" (
    set DOTNET_CONFIG=Release
    goto BUILD
)

if /i "%CONFIG%"=="publish" (
    echo [Build] Publishing AOT native library...
    cd /d "%EXTENSION_DIR%"
    dotnet publish -c Release -r win-x64 --self-contained true
    if errorlevel 1 (
        echo [ERROR] Publish failed
        exit /b 1
    )
    
    echo [Build] Copying to test project...
    if not exist "%TEST_DIR%" mkdir "%TEST_DIR%"
    xcopy /E /Y "%EXTENSION_DIR%bin\Release\net8.0\win-x64\publish\*" "%TEST_DIR%"
    
    echo [Build] Done! GDExtension files copied to: %TEST_DIR%
    goto END
)

:BUILD
set DOTNET_CONFIG=%CONFIG%
if /i "%CONFIG%"=="debug" set DOTNET_CONFIG=Debug

echo [Build] Building %DOTNET_CONFIG%...
cd /d "%EXTENSION_DIR%"
dotnet build -c %DOTNET_CONFIG%

if errorlevel 1 (
    echo [ERROR] Build failed
    exit /b 1
)

echo [Build] Done!
echo [Build] Output: %EXTENSION_DIR%bin\%DOTNET_CONFIG%\net8.0\

:END
cd /d "%PROJECT_DIR%"
echo.
echo ========================================
echo To test: Open test/GDEWebViewGame/ with Godot 4.6+
echo ========================================