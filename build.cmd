@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF

"%CSC%" /nologo /target:winexe /win32manifest:app.manifest /out:VRAMOptimizer.exe /lib:"%WPF%" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.dll /r:System.Xml.dll Program.cs
if %errorlevel% equ 0 (
    echo SUCCESS: VRAMOptimizer.exe compiled successfully!
) else (
    echo FAILED: Compilation returned %errorlevel%
)
