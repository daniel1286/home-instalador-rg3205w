@echo off
rem Compila salida\Instalar-Home.exe con el compilador de C# que trae Windows (.NET Framework 4.8).
rem No hace falta instalar nada: es el mismo en el PC y en GitHub Actions.
rem
rem Firma opcional: con un certificado para firmar programas, el .exe sale firmado y con sello de tiempo.
rem   FIRMA_PFX     archivo .pfx del certificado        FIRMA_CLAVE  su clave, si la tiene
rem   FIRMA_HUELLA  o bien la huella de un certificado ya instalado en Windows (vale un token USB)
rem   FIRMA_SELLO   servidor de sello de tiempo (por defecto, el de DigiCert)
rem   SIGNTOOL      ruta de signtool.exe si no se encuentra en el SDK de Windows
rem Sin ninguna de las dos primeras, se compila sin firmar.
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
"%NET%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /warnaserror+ ^
    /out:salida\Instalar-Home.exe /win32icon:recursos\icono.ico /win32manifest:fuente\app.manifest ^
    /r:"%NET%\WPF\PresentationFramework.dll" /r:"%NET%\WPF\PresentationCore.dll" /r:"%NET%\WPF\WindowsBase.dll" ^
    /r:System.Xaml.dll /r:System.Xml.dll /r:System.Core.dll /r:System.Management.dll /r:System.Web.Extensions.dll ^
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll ^
    !RECURSOS! fuente\*.cs
if errorlevel 1 exit /b 1
echo Hecho: salida\Instalar-Home.exe

if "%FIRMA_PFX%%FIRMA_HUELLA%"=="" (
    echo Sin firmar: no hay certificado ^(FIRMA_PFX o FIRMA_HUELLA^).
    exit /b 0
)
if "%SIGNTOOL%"=="" for /f "delims=" %%s in ('dir /b /s /o-n "%ProgramFiles(x86)%\Windows Kits\10\bin\*signtool.exe" 2^>nul ^| findstr /i "\\x64\\signtool.exe"') do if "!SIGNTOOL!"=="" set "SIGNTOOL=%%s"
if "%SIGNTOOL%"=="" (
    echo Hay certificado pero no se encuentra signtool.exe: instala el SDK de Windows o pon su ruta en SIGNTOOL.
    exit /b 1
)
if "%FIRMA_SELLO%"=="" set FIRMA_SELLO=http://timestamp.digicert.com
if not "%FIRMA_PFX%"=="" (
    if "%FIRMA_CLAVE%"=="" (
        "%SIGNTOOL%" sign /fd sha256 /tr "%FIRMA_SELLO%" /td sha256 /d "Instalar Home" /f "%FIRMA_PFX%" salida\Instalar-Home.exe
    ) else (
        "%SIGNTOOL%" sign /fd sha256 /tr "%FIRMA_SELLO%" /td sha256 /d "Instalar Home" /f "%FIRMA_PFX%" /p "%FIRMA_CLAVE%" salida\Instalar-Home.exe
    )
) else (
    "%SIGNTOOL%" sign /fd sha256 /tr "%FIRMA_SELLO%" /td sha256 /d "Instalar Home" /sha1 "%FIRMA_HUELLA%" salida\Instalar-Home.exe
)
if errorlevel 1 exit /b 1
"%SIGNTOOL%" verify /pa salida\Instalar-Home.exe
if errorlevel 1 exit /b 1
echo Firmado: salida\Instalar-Home.exe
