@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM  link-agents.bat
REM  Creates a directory junction so that the global, user-wide
REM  Claude agents directory points to THIS folder. That way
REM  ALL projects see the same agents.
REM
REM  Username-independent:
REM    Source (junction target) = %~dp0   (folder of this .bat)
REM    Junction                 = %USERPROFILE%\.claude\agents
REM
REM  Equivalent to:
REM    mklink /J "%USERPROFILE%\.claude\agents" "<this folder>"
REM
REM  Junctions do NOT require admin rights.
REM ============================================================

REM Source = folder of this .bat, without trailing backslash
set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"

set "LINK=%USERPROFILE%\.claude\agents"

echo.
echo   Junction : %LINK%
echo   Target   : %SRC%
echo.

REM Ensure the parent .claude directory exists
if not exist "%USERPROFILE%\.claude" mkdir "%USERPROFILE%\.claude"

REM Handle an existing target
if exist "%LINK%" (
    REM Is it already a reparse point (junction/symlink)?
    fsutil reparsepoint query "%LINK%" >nul 2>&1
    if !errorlevel! EQU 0 (
        echo   Existing link found - removing and recreating...
        rmdir "%LINK%"
    ) else (
        echo   [ABORTED] "%LINK%" already exists as a real directory.
        echo             Please back up/remove its contents and run again.
        echo.
        pause
        exit /b 1
    )
)

mklink /J "%LINK%" "%SRC%"
if errorlevel 1 (
    echo.
    echo   [ERROR] Failed to create the junction.
) else (
    echo.
    echo   Done. All projects now use the agents from this folder.
)

echo.
pause
endlocal
