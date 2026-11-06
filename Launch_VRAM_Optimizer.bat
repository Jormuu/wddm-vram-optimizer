@echo off
:: Self-elevation to Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting Administrator privileges...
    powershell -NoProfile -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
if exist "VRAMOptimizer.exe" (
    echo Launching VRAMOptimizer.exe...
    start "" "VRAMOptimizer.exe"
) else (
    echo Launching VRAMOptimizer.ps1...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "VRAMOptimizer.ps1"
)
