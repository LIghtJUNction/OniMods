@echo off
setlocal
REM Build/install onim CLI with a full MSVC environment (Git Bash's link.exe is not MSVC).
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" || exit /b 1
where link
cargo install --path "%~dp0.." --force
exit /b %ERRORLEVEL%
