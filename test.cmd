@echo off
setlocal
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo ERROR: .NET Framework 4.x x64 compiler not found at "%CSC%"
    exit /b 1
)

"%CSC%" /nologo /utf8output /codepage:65001 /optimize+ /warn:4 /target:exe /out:TestRunner.exe src\WddmCalculator.cs src\VramBenchmark.cs tests\WddmTests.cs
if %errorlevel% neq 0 (
    echo FAILED: Test compilation failed with code %errorlevel%
    exit /b %errorlevel%
)

TestRunner.exe
set "TEST_EXIT=%errorlevel%"
del /f /q TestRunner.exe >nul 2>&1
exit /b %TEST_EXIT%
