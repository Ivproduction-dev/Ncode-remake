@echo off
if exist "%~dp0Ncode.Editor\bin\Release\net8.0\Ncode.Editor.dll" (
  dotnet "%~dp0Ncode.Editor\bin\Release\net8.0\Ncode.Editor.dll" "%~dp0" %*
) else (
  dotnet "%~dp0Ncode.Editor\bin\Debug\net8.0\Ncode.Editor.dll" "%~dp0" %*
)
