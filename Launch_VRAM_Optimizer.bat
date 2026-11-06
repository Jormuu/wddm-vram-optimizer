@echo off
:: Self-elevation to Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting Administrator privileges...
    powershell -NoProfile -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
if not exist "VRAMOptimizer.exe" (
    echo VRAMOptimizer.exe not found. Building it from Program.cs...
    call build.cmd
    if not exist "VRAMOptimizer.exe" (
        echo Build failed. See the errors above.
        pause
        exit /b 1
    )
)

echo Launching VRAMOptimizer.exe...
start "" "VRAMOptimizer.exe"
