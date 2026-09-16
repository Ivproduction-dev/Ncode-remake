@echo off
if exist "%~dp0Ncode\bin\Release\net8.0-windows\Ncode.dll" (
  dotnet "%~dp0Ncode\bin\Release\net8.0-windows\Ncode.dll" %*
) else if exist "%~dp0Ncode\bin\Release\net8.0\Ncode.dll" (
  dotnet "%~dp0Ncode\bin\Release\net8.0\Ncode.dll" %*
) else if exist "%~dp0Ncode\bin\Debug\net8.0-windows\Ncode.dll" (
  dotnet "%~dp0Ncode\bin\Debug\net8.0-windows\Ncode.dll" %*
) else (
  dotnet "%~dp0Ncode\bin\Debug\net8.0\Ncode.dll" %*
)
