@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul
echo ========================================
echo  Ncode - install and build runtime
echo  Windows + Android ^(future^)
echo ========================================
echo.

where dotnet >nul 2>&1
if !errorlevel! neq 0 goto :install_dotnet

for /f "tokens=1 delims=." %%A in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%A
if not defined DOTNET_MAJOR goto :install_dotnet
if !DOTNET_MAJOR! geq 8 (
  echo [OK] .NET SDK !DOTNET_MAJOR!.x found
  dotnet --version
  goto :build
) else (
  echo [WARN] Found .NET !DOTNET_MAJOR!.x, need 8.x
  goto :install_dotnet
)

:install_dotnet
echo.
echo [.NET 8 SDK not found, trying user-local install without admin...]
echo.

where powershell >nul 2>&1
if !errorlevel! neq 0 (
  echo [WARN] PowerShell not found, skipping user-local install
  goto :need_admin_install
)

set DOTNET_INSTALL_DIR=%LocalAppData%\Microsoft\dotnet
if not exist "%DOTNET_INSTALL_DIR%" mkdir "%DOTNET_INSTALL_DIR%" >nul 2>&1

echo Downloading dotnet-install.ps1 ^(user-local, no admin^)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; try { Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1 -UseBasicParsing; } catch { exit 1 }" >nul 2>&1
if !errorlevel! neq 0 (
  echo [WARN] Failed to download dotnet-install.ps1
  goto :need_admin_install
)

echo Installing .NET 8 SDK to %DOTNET_INSTALL_DIR% ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir $env:LocalAppData\Microsoft\dotnet -Quality GA" >nul 2>&1

set "PATH=%DOTNET_INSTALL_DIR%;%PATH%"
set "DOTNET_ROOT=%DOTNET_INSTALL_DIR%"

where dotnet >nul 2>&1
if !errorlevel! neq 0 goto :need_admin_install
for /f "tokens=1 delims=." %%A in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%A
if !DOTNET_MAJOR! geq 8 (
  echo [OK] .NET 8 installed user-local:
  dotnet --version
  echo Add to PATH manually for new consoles: %DOTNET_INSTALL_DIR%
  goto :build
)

:need_admin_install
echo.
echo Trying system install ^(requires admin^)...
net session >nul 2>&1
if !errorlevel! neq 0 (
  echo.
  echo ========================================
  echo  Administrator rights required
  echo ========================================
  echo  System install of .NET 8 SDK needs admin rights.
  echo  Options:
  echo   1^) Run this file as Administrator ^(Right click - Run as admin^)
  echo   2^) Or install manually: https://dotnet.microsoft.com/download/dotnet/8.0
  echo   3^) Or user-local install above should have worked - check internet
  echo.
  pause
  exit /b 1
)

echo [OK] Admin rights OK, installing via winget / installer...

where winget >nul 2>&1
if !errorlevel! equ 0 (
  echo Installing via winget...
  winget install Microsoft.DotNet.SDK.8 --silent --accept-package-agreements --accept-source-agreements >nul 2>&1
  where dotnet >nul 2>&1
  if !errorlevel! equ 0 (
    for /f "tokens=1 delims=." %%A in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%A
    if !DOTNET_MAJOR! geq 8 goto :build
  )
)

echo Downloading .NET 8 SDK installer...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1 -UseBasicParsing" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -Quality GA" >nul 2>&1

where dotnet >nul 2>&1
if !errorlevel! neq 0 (
  echo [ERROR] Failed to install .NET 8 SDK
  pause
  exit /b 1
)
for /f "tokens=1 delims=." %%A in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%A
if !DOTNET_MAJOR! lss 8 (
  echo [ERROR] Installed .NET !DOTNET_MAJOR!.x, need 8.x
  pause
  exit /b 1
)
echo [OK] .NET 8 installed system-wide
goto :build

:build
echo.
echo ========================================
echo  Building runtime
echo ========================================

echo [1/6] Windows runtime ^(net8.0-windows^)...
dotnet build "%~dp0Ncode\Ncode.csproj" -c Release -f net8.0-windows --nologo
if !errorlevel! neq 0 (
  echo [WARN] Windows build failed, trying net8.0...
  dotnet build "%~dp0Ncode\Ncode.csproj" -c Release -f net8.0 --nologo
  if !errorlevel! neq 0 (
    echo [ERROR] Runtime build failed
    pause
    exit /b 1
  )
) else (
  dotnet build "%~dp0Ncode\Ncode.csproj" -c Release -f net8.0 --nologo >nul 2>&1
)

echo.
echo [2/6] Android workload ^(future^)...
dotnet workload list 2>nul | findstr /i android >nul 2>&1
if !errorlevel! equ 0 (
  echo [OK] Android workload already installed
  goto :jdk_check
)
echo Workload android not found
echo      Installed SDKs ^(workload belongs to one of them^):
dotnet --list-sdks 2>nul
echo      Current: & dotnet --version 2>nul
net session >nul 2>&1
if !errorlevel! neq 0 (
  echo [WARN] Need admin for workload install - skip for now
  echo      Run install.bat as Administrator later or: dotnet workload install android
  goto :jdk_check
)
echo Installing workload android ^(may take 2-5 min, please wait^)...
echo ^> dotnet workload install android
powershell -NoProfile -Command "$p = Start-Process -FilePath 'dotnet' -ArgumentList 'workload install android --skip-manifest-update' -PassThru -NoNewWindow; if (-not $p.WaitForExit(300000)) { try { $p.Kill($true) } catch {}; Write-Host '[TIMEOUT] workload install timed out after 5 min'; exit 1 } else { exit $p.ExitCode }" 
if !errorlevel! equ 0 (
  echo [OK] Android workload installed
) else (
  echo [WARN] Workload install failed or timed out - trying without --skip-manifest-update...
  powershell -NoProfile -Command "$p = Start-Process -FilePath 'dotnet' -ArgumentList 'workload install android' -PassThru -NoNewWindow; if (-not $p.WaitForExit(300000)) { try { $p.Kill($true) } catch {}; exit 1 } else { exit $p.ExitCode }"
  if !errorlevel! equ 0 (
    echo [OK] Android workload installed
  ) else (
    echo [WARN] Android workload not installed - skip ^(run later: dotnet workload install android^)
  )
)

:jdk_check
echo [3/6] Checking JDK 17 for Android signing...
set JDK17_FOUND=0
set JDK17_PATH=
if defined JAVA_HOME if exist "%JAVA_HOME%\bin\java.exe" (
  "%JAVA_HOME%\bin\java.exe" -version 2>&1 | findstr "17." >nul 2>&1
  if !errorlevel! equ 0 (set "JDK17_FOUND=1" & set "JDK17_PATH=%JAVA_HOME%")
)
if !JDK17_FOUND! equ 0 (
  java -version 2>&1 | findstr "17." >nul 2>&1
  if !errorlevel! equ 0 set JDK17_FOUND=1
)
if !JDK17_FOUND! equ 0 (
  for /D %%D in ("%ProgramFiles%\Microsoft\jdk-17*") do if exist "%%D\bin\java.exe" ("%%D\bin\java.exe" -version 2>&1 | findstr "17." >nul 2>&1 && (set "JDK17_FOUND=1" & set "JDK17_PATH=%%D"))
)
if !JDK17_FOUND! equ 0 (
  for /D %%D in ("%ProgramFiles%\Eclipse Adoptium\jdk-17*") do if exist "%%D\bin\java.exe" ("%%D\bin\java.exe" -version 2>&1 | findstr "17." >nul 2>&1 && (set "JDK17_FOUND=1" & set "JDK17_PATH=%%D"))
)
if !JDK17_FOUND! equ 0 (
  for /D %%D in ("%ProgramFiles%\Java\jdk-17*") do if exist "%%D\bin\java.exe" ("%%D\bin\java.exe" -version 2>&1 | findstr "17." >nul 2>&1 && (set "JDK17_FOUND=1" & set "JDK17_PATH=%%D"))
)
if !JDK17_FOUND! equ 1 (
  echo [OK] JDK 17 found at !JDK17_PATH!
  if defined JDK17_PATH set "PATH=!JDK17_PATH!\bin;!PATH!"
) else (
  echo JDK 17 not found in PATH or common locations
  echo      JAVA_HOME=!JAVA_HOME!
  echo      Try: setx JAVA_HOME "C:\Path\To\Your\JDK17" and restart console
  echo      Or add JDK 17 bin to PATH
  where winget >nul 2>&1
  if !errorlevel! equ 0 (
    echo Trying to install via winget ^(may need admin^)...
    powershell -NoProfile -Command "$p = Start-Process -FilePath 'winget' -ArgumentList 'install Microsoft.OpenJDK.17 --silent --accept-package-agreements --accept-source-agreements' -PassThru -NoNewWindow; if (-not $p.WaitForExit(120000)) { try { $p.Kill($true) } catch {}; Write-Host '[TIMEOUT] winget timed out'; exit 1 } else { exit $p.ExitCode }" >nul 2>&1
    java -version 2>&1 | findstr "17." >nul 2>&1
    if !errorlevel! equ 0 echo [OK] JDK 17 installed
  )
  set KEYTOOL_FOUND=0
  where keytool >nul 2>&1 && set KEYTOOL_FOUND=1
  if exist "%ProgramFiles%\Java\jdk*\bin\keytool.exe" set KEYTOOL_FOUND=1
  if exist "%ProgramFiles%\Microsoft\jdk-17*\bin\keytool.exe" set KEYTOOL_FOUND=1
  if exist "%ProgramFiles%\Eclipse Adoptium\jdk-17*\bin\keytool.exe" set KEYTOOL_FOUND=1
  if exist "%ProgramFiles%\Microsoft\jdk-11*\bin\keytool.exe" set KEYTOOL_FOUND=1
  if defined JAVA_HOME if exist "%JAVA_HOME%\bin\keytool.exe" set KEYTOOL_FOUND=1
  if defined JDK17_PATH if exist "!JDK17_PATH!\bin\keytool.exe" set KEYTOOL_FOUND=1
  if !KEYTOOL_FOUND! equ 0 (
    echo [WARN] keytool not found - install JDK 17 manually: https://learn.microsoft.com/java/openjdk/download
    echo       Or set JAVA_HOME to your JDK 17 path
  ) else (
    echo [OK] keytool found
  )
)

echo [4/6] Checking Android SDK...
set ANDROID_SDK_FOUND=0
if defined ANDROID_HOME if exist "%ANDROID_HOME%\platform-tools\adb.exe" set ANDROID_SDK_FOUND=1
if defined ANDROID_SDK_ROOT if exist "%ANDROID_SDK_ROOT%\platform-tools\adb.exe" set ANDROID_SDK_FOUND=1
if exist "%LocalAppData%\Android\Sdk\platform-tools\adb.exe" set ANDROID_SDK_FOUND=1
if exist "%ProgramFiles%\Android\Android Studio\bin\studio64.exe" set ANDROID_SDK_FOUND=1
for /f "tokens=2*" %%R in ('reg query "HKCU\SOFTWARE\Android SDK Tools" /v Path 2^>nul ^| findstr /i /c:"REG_SZ"') do if exist "%%S\platform-tools\adb.exe" set ANDROID_SDK_FOUND=1
for /f "tokens=2*" %%R in ('reg query "HKLM\SOFTWARE\Android SDK Tools" /v Path 2^>nul ^| findstr /i /c:"REG_SZ"') do if exist "%%S\platform-tools\adb.exe" set ANDROID_SDK_FOUND=1
if !ANDROID_SDK_FOUND! equ 1 (
  echo [OK] Android SDK found
) else (
  echo [WARN] Android SDK not found
  echo      ANDROID_HOME=!ANDROID_HOME!
  echo      ANDROID_SDK_ROOT=!ANDROID_SDK_ROOT!
  echo      Default location checked: %LocalAppData%\Android\Sdk\platform-tools\adb.exe
  echo      Install Android Studio: https://developer.android.com/studio
  echo      Or cmdline-tools only: https://developer.android.com/studio#command-line-tools-only
  echo      Then run: setx ANDROID_HOME "%%LocalAppData%%\Android\Sdk" and restart console
)

echo [5/6] Trying Android runtime ^(optional^)...
set CUR_MAJOR=0
for /f "tokens=1 delims=." %%A in ('dotnet --version 2^>nul') do set CUR_MAJOR=%%A
if !CUR_MAJOR! lss 11 (
  echo [..] Android needs .NET 11 SDK for net11.0-android ^(current: !CUR_MAJOR!.x^)
  echo      Install it: https://dotnet.microsoft.com/download - Windows runtime above is ready
  goto :keystore
)
dotnet build "%~dp0Ncode.Android\Ncode.Android.csproj" -c Release -f net11.0-android --nologo >nul 2>&1
if !errorlevel! equ 0 (
  echo [OK] Android runtime built
) else (
  echo [..] Android runtime not built ^(no workload/SDK^) - ok, Windows runtime ready
)

:keystore
echo.
echo [6/6] Release APK keystore ^(for signed .apk/.aab^)...
set KEYSTORE=%AppData%\Ncode\ncode.keystore
if not exist "%KEYSTORE%" (
  mkdir "%AppData%\Ncode" >nul 2>&1
  where keytool >nul 2>&1
  if !errorlevel! equ 0 (
    echo Generating keystore at %KEYSTORE% ...
    keytool -genkeypair -keystore "%KEYSTORE%" -alias ncode -keyalg RSA -keysize 2048 -validity 10000 -storepass ncode123 -keypass ncode123 -dname "CN=Ncode,O=Ivproduction,C=RU" >nul 2>&1
    if exist "%KEYSTORE%" echo [OK] Keystore created
  ) else (
    echo [WARN] keytool not found ^(install JDK 17+^) - APK will be debug-signed
  )
) else (
  echo [OK] Keystore exists at %KEYSTORE%
)

if exist "%KEYSTORE%" (
  echo Building Android release APK ^(signed^) for verification...
  dotnet publish "%~dp0Ncode.Android\Ncode.Android.csproj" -c Release -f net11.0-android -p:AndroidKeyStore=true -p:AndroidSigningKeyStore="%KEYSTORE%" -p:AndroidSigningKeyAlias=ncode -p:AndroidSigningKeyPass=ncode123 -p:AndroidSigningStorePass=ncode123 -o "%TEMP%\NcodeApkTest" --nologo >nul 2>&1
  if !errorlevel! equ 0 (
    echo [OK] Android release APK can be built - export via editor ^(Project - Export .apk^) will be signed
    rmdir /s /q "%TEMP%\NcodeApkTest" >nul 2>&1
  ) else (
    echo [..] Android APK build skipped ^(no workload/SDK^) - will be built on export
  )
)

echo.
echo ========================================
echo  Done!
echo ========================================
echo  Windows runtime: Ncode\bin\Release\net8.0-windows\Ncode.dll
echo  Headless:        Ncode\bin\Release\net8.0\Ncode.dll
echo  Run: ncode.bat game.ncode  or  editor.bat
echo  Tests: dotnet test
echo.
pause
exit /b 0

