@echo off
setlocal
REM Build/install onim CLI with a full MSVC environment (Git Bash's link.exe is not MSVC).

if defined ONIM_VCVARS64 goto use_override

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto missing_vswhere

for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
if not defined VSINSTALL goto missing_toolchain
set "VCVARS64=%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat"
goto validate_vcvars

:use_override
set "VCVARS64=%ONIM_VCVARS64%"

:validate_vcvars
if not exist "%VCVARS64%" goto missing_vcvars

call "%VCVARS64%" || exit /b 1
where link
cargo install --path "%~dp0.." --force
exit /b %ERRORLEVEL%

:missing_vswhere
echo ERROR: vswhere.exe was not found. Install Visual Studio Build Tools with the Desktop development with C++ workload, or set ONIM_VCVARS64.
exit /b 1

:missing_toolchain
echo ERROR: No Visual Studio instance with Microsoft.VisualStudio.Component.VC.Tools.x86.x64 was found. Install the C++ build tools, or set ONIM_VCVARS64.
exit /b 1

:missing_vcvars
echo ERROR: vcvars64.bat was not found at "%VCVARS64%".
exit /b 1
