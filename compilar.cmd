@echo off
rem Compila salida\Instalar-Home.exe con el compilador de C# que trae Windows (.NET Framework 4.8).
rem No hace falta instalar nada: es el mismo en el PC y en GitHub Actions.
setlocal enabledelayedexpansion
cd /d "%~dp0"

set NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%NET%\csc.exe" (
    echo No se encuentra %NET%\csc.exe
    exit /b 1
)

set RECURSOS=/resource:fuente\Ventana.xaml,Ventana.xaml
for %%f in (recursos\letras\*.ttf) do set RECURSOS=!RECURSOS! /resource:%%f,letras.%%~nxf
for %%f in (recursos\guia\*.png) do set RECURSOS=!RECURSOS! /resource:%%f,guia.%%~nxf

if not exist salida mkdir salida
"%NET%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ^
    /out:salida\Instalar-Home.exe /win32icon:recursos\icono.ico /win32manifest:fuente\app.manifest ^
    /r:"%NET%\WPF\PresentationFramework.dll" /r:"%NET%\WPF\PresentationCore.dll" /r:"%NET%\WPF\WindowsBase.dll" ^
    /r:System.Xaml.dll /r:System.Xml.dll /r:System.Core.dll /r:System.Management.dll /r:System.Web.Extensions.dll ^
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll ^
    !RECURSOS! fuente\*.cs
if errorlevel 1 exit /b 1
echo Hecho: salida\Instalar-Home.exe
