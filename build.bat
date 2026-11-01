@echo off
rem Compila GDDowngrade.exe con el compilador de C# que ya trae Windows (.NET Framework 4.x).
rem WPF UI y sus dependencias (carpeta lib) quedan incrustadas dentro del .exe.
rem Uso: build.bat [ruta del .exe de salida]
setlocal
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0GDDowngrade.exe"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo No se encontro csc.exe de .NET Framework 4.
  exit /b 1
)
for %%i in ("%CSC%") do set "FW=%%~dpi"
set "LIB=%~dp0lib"

"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /out:"%OUT%" ^
  /win32manifest:"%~dp0src\app.manifest" ^
  /win32icon:"%~dp0src\app.ico" ^
  /r:System.dll /r:System.Core.dll /r:"%FW%System.Xaml.dll" ^
  /r:"%FW%WPF\PresentationFramework.dll" /r:"%FW%WPF\PresentationCore.dll" /r:"%FW%WPF\WindowsBase.dll" ^
  /r:"%LIB%\Wpf.Ui.dll" /r:"%LIB%\Wpf.Ui.Abstractions.dll" /r:"%LIB%\System.Memory.dll" ^
  /resource:"%LIB%\Wpf.Ui.dll",lib.Wpf.Ui.dll ^
  /resource:"%LIB%\Wpf.Ui.Abstractions.dll",lib.Wpf.Ui.Abstractions.dll ^
  /resource:"%LIB%\System.Memory.dll",lib.System.Memory.dll ^
  /resource:"%LIB%\System.Buffers.dll",lib.System.Buffers.dll ^
  /resource:"%LIB%\System.Numerics.Vectors.dll",lib.System.Numerics.Vectors.dll ^
  /resource:"%LIB%\System.Runtime.CompilerServices.Unsafe.dll",lib.System.Runtime.CompilerServices.Unsafe.dll ^
  "%~dp0src\*.cs"
if errorlevel 1 exit /b 1
echo Listo: %OUT%
