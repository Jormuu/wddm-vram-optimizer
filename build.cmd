@echo off
setlocal
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "WPF=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\WPF"

if not exist "%CSC%" (
    echo ERROR: .NET Framework 4.x x64 compiler not found at "%CSC%"
    exit /b 1
)

"%CSC%" /nologo /utf8output /codepage:65001 /optimize+ /warn:4 /target:winexe /win32manifest:app.manifest /out:VRAMOptimizer.exe /lib:"%WPF%" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.dll /r:System.Xml.dll src\AssemblyInfo.cs src\WddmCalculator.cs src\RegistryManager.cs src\VramBenchmark.cs src\MainWindow.cs src\Program.cs
if %errorlevel% equ 0 (
    echo SUCCESS: VRAMOptimizer.exe compiled cleanly!
    exit /b 0
) else (
    echo FAILED: Compilation returned %errorlevel%
    exit /b %errorlevel%
)
