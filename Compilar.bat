@echo off
setlocal
set "PROJECT=%~dp0ControleRemotoLAN.vbproj"
set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

if not exist "%MSBUILD%" (
  echo Visual Studio Installer nao encontrado.
  echo Abra ControleRemotoLAN.vbproj pelo Visual Studio e compile em Release.
  pause
  exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%MSBUILD%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not exist "%MSBUILD%" (
  echo MSBuild nao encontrado. Instale o desenvolvimento para desktop com .NET.
  pause
  exit /b 1
)

"%MSBUILD%" "%PROJECT%" /t:Rebuild /p:Configuration=Release /m
if errorlevel 1 (
  echo.
  echo A compilacao falhou.
  pause
  exit /b 1
)

echo.
echo Compilado com sucesso em bin\Release\ControleRemotoLAN.exe
pause
