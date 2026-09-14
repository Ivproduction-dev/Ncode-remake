@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul
echo ========================================
echo  Ncode - install and build runtime
echo  Windows + Android ^(future^)
echo ========================================
echo.

rem 1. Check dotnet
where dotnet >nul 2>&1
if %errorlevel% neq 0 goto :install_dotnet

for /f "tokens=1 delims=." %%A in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%A
if not defined DOTNET_MAJOR goto :install_dotnet
if !DOTNET_MAJOR! geq 8 (
  echo [OK] .NET SDK !DOTNET_MAJOR!.x found
  dotnet --version
  goto :build
) else (
  echo [!!] Found .NET !DOTNET_MAJOR!.x, need 8.x
  goto :install_dotnet
)

:install_dotnet
echo.
echo [.NET 8 SDK not found, trying user-local install without admin...]
echo.

rem 1.1 Try user-local install via dotnet-install.ps1 - no admin needed
where powershell >nul 2>&1
if %errorlevel% neq 0 (
  echo [!!] PowerShell not found, skipping user-local install
  goto :need_admin_install
)

set DOTNET_INSTALL_DIR=%LocalAppData%\Microsoft\dotnet
if not exist "%DOTNET_INSTALL_DIR%" mkdir "%DOTNET_INSTALL_DIR%" >nul 2>&1

echo Downloading dotnet-install.ps1 ^(user-local, no admin^)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; try { Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1 -UseBasicParsing; } catch { exit 1 }" >nul 2>&1
if %errorlevel% neq 0 (
  echo [!!] Failed to download dotnet-install.ps1
  goto :need_admin_install
)

echo Installing .NET 8 SDK to %DOTNET_INSTALL_DIR% ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir $env:LocalAppData\Microsoft\dotnet -Quality GA" >nul 2>&1

rem Add to PATH for current session
set "PATH=%DOTNET_INSTALL_DIR%;%PATH%"
set "DOTNET_ROOT=%DOTNET_INSTALL_DIR%"

rem Check again
where dotnet >nul 2>&1
if %errorlevel% neq 0 goto :need_admin_install
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
if %errorlevel% neq 0 (
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

rem Try winget on Win10/11
where winget >nul 2>&1
if %errorlevel% equ 0 (
  echo Installing via winget...
  winget install Microsoft.DotNet.SDK.8 --silent --accept-package-agreements --accept-source-agreements >nul 2>&1
  where dotnet >nul 2>&1
  if %errorlevel% equ 0 (
    for /f "tokens=1 delims=." %%A in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%A
    if !DOTNET_MAJOR! geq 8 goto :build
  )
)

rem Fallback - official installer via dotnet-install.ps1 system-wide
echo Downloading .NET 8 SDK installer...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1 -UseBasicParsing" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -Quality GA" >nul 2>&1

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
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

rem Build Windows runtime - required
echo [1/6] Windows runtime ^(net8.0-windows^)...
dotnet build Ncode -c Release -f net8.0-windows --nologo
if %errorlevel% neq 0 (
  echo [!!] Windows build failed, trying net8.0...
  dotnet build Ncode -c Release -f net8.0 --nologo
  if %errorlevel% neq 0 (
    echo [ERROR] Runtime build failed
    pause
    exit /b 1
  )
) else (
  rem Also build headless for tests
  dotnet build Ncode -c Release -f net8.0 --nologo >nul 2>&1
)

rem Android workload for future - don't fail whole install if missing
echo.
echo [2/6] Android workload ^(future^)...
dotnet workload list 2>nul | findstr /i android >nul 2>&1
if %errorlevel% neq 0 (
  echo Installing workload android ^(may need admin, skip if fails^)...
  dotnet workload install android --skip-manifest-update >nul 2>&1
  if %errorlevel% neq 0 dotnet workload install android >nul 2>&1
)
if %errorlevel% equ 0 (
  echo [OK] Android workload ready
) else (
  echo [!!] Android workload not installed - skip ^(run later: dotnet workload install android^)
)

rem Check JDK 17 for Android signing
echo [3/6] Checking JDK 17 for Android signing...
java -version 2>&1 | findstr "17\." >nul 2>&1
if %errorlevel% neq 0 (
  where keytool >nul 2>&1
  if %errorlevel% neq 0 (
    echo JDK 17 not found, trying to install via winget...
    where winget >nul 2>&1
    if %errorlevel% equ 0 (
      winget install Microsoft.OpenJDK.17 --silent --accept-package-agreements --accept-source-agreements >nul 2>&1
      if %errorlevel% equ 0 echo [OK] JDK 17 installed
    )
    where keytool >nul 2>&1
    if %errorlevel% neq 0 echo [!!] keytool still not found - install JDK 17 manually: https://learn.microsoft.com/java/openjdk/download
  ) else (
    echo [OK] keytool found
  )
) else (
  echo [OK] JDK 17 found
)

rem Check Android SDK
echo [4/6] Checking Android SDK...
set ANDROID_SDK_FOUND=0
if defined ANDROID_HOME if exist "%ANDROID_HOME%\platform-tools\adb.exe" set ANDROID_SDK_FOUND=1
if defined ANDROID_SDK_ROOT if exist "%ANDROID_SDK_ROOT%\platform-tools\adb.exe" set ANDROID_SDK_FOUND=1
if exist "%LocalAppData%\Android\Sdk\platform-tools\adb.exe" set ANDROID_SDK_FOUND=1
if exist "%ProgramFiles%\Android\Android Studio\bin\studio64.exe" set ANDROID_SDK_FOUND=1
if %ANDROID_SDK_FOUND% equ 1 (
  echo [OK] Android SDK found
) else (
  echo [!!] Android SDK not found - trying to install via winget...
  where winget >nul 2>&1
  if %errorlevel% equ 0 (
    winget install Google.AndroidStudio --silent --accept-package-agreements --accept-source-agreements >nul 2>&1
    if %errorlevel% equ 0 echo [OK] Android Studio installed - launch it once to finish SDK setup
  )
  if %ANDROID_SDK_FOUND% equ 0 echo [!!] Android SDK still not found - install Android Studio manually: https://developer.android.com/studio
)

rem Try Android runtime if workload exists - optional
echo [5/6] Trying Android runtime ^(optional^)...
dotnet build Ncode -c Release -f net8.0-android --nologo >nul 2>&1
if %errorlevel% equ 0 (
  echo [OK] Android runtime built
) else (
  echo [..] Android runtime not built ^(no TFM or workload^) - ok, Windows runtime ready
)

rem Release APK signing - generate keystore if missing
echo.
echo [6/6] Release APK keystore ^(for signed .apk/.aab^)...
set KEYSTORE=%AppData%\Ncode\ncode.keystore
if not exist "%KEYSTORE%" (
  mkdir "%AppData%\Ncode" >nul 2>&1
  where keytool >nul 2>&1
  if %errorlevel% equ 0 (
    echo Generating keystore at %KEYSTORE% ...
    keytool -genkeypair -keystore "%KEYSTORE%" -alias ncode -keyalg RSA -keysize 2048 -validity 10000 -storepass ncode123 -keypass ncode123 -dname "CN=Ncode,O=Ivproduction,C=RU" >nul 2>&1
    if exist "%KEYSTORE%" echo [OK] Keystore created
  ) else (
    echo [!!] keytool not found ^(install JDK 17+^) - APK will be debug-signed
  )
) else (
  echo [OK] Keystore exists at %KEYSTORE%
)

rem Try building Android APK in Release with signing if possible
if exist "%KEYSTORE%" (
  echo Building Android release APK ^(signed^) for verification...
  dotnet publish Ncode.Android\Ncode.Android.csproj -c Release -f net8.0-android -p:AndroidKeyStore=true -p:AndroidSigningKeyStore="%KEYSTORE%" -p:AndroidSigningKeyAlias=ncode -p:AndroidSigningKeyPass=ncode123 -p:AndroidSigningStorePass=ncode123 -o "%TEMP%\NcodeApkTest" --nologo >nul 2>&1
  if %errorlevel% equ 0 (
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
