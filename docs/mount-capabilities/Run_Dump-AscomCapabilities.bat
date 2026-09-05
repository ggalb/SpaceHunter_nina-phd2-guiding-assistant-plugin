@echo off
REM ===================================================================
REM  Run_Dump-AscomCapabilities.bat
REM
REM  Double-click this to profile your mount's ASCOM driver.
REM  Keep it in the same folder as Dump-AscomCapabilities.ps1.
REM
REM  READ ONLY - it never slews, parks, homes, changes any setting, or
REM  disconnects your mount. Safe to run while the mount is connected
REM  and in use by N.I.N.A., SGP or anything else.
REM
REM  Everything printed is also saved to ascom_dump.txt beside this
REM  file, so you can just send that back.
REM ===================================================================

setlocal

set "SCRIPT=%~dp0Dump-AscomCapabilities.ps1"
set "OUT=%~dp0ascom_dump.txt"

if not exist "%SCRIPT%" (
    echo.
    echo  ERROR: Dump-AscomCapabilities.ps1 was not found next to this file.
    echo  Both files must be in the same folder.
    echo.
    pause
    exit /b 1
)

echo.
echo  ============================================================
echo   ASCOM mount capability dump
echo  ============================================================
echo.
echo   This only READS from your mount driver.
echo   Nothing will move. Nothing will be changed or disconnected.
echo.
echo   Please make sure your mount is POWERED ON and CONNECTED -
echo   drivers report different capabilities when they are idle.
echo.
echo   A driver chooser will open in a moment. Pick your mount.
echo.
pause

REM  6>&1 merges the information stream into the pipeline. The script
REM  writes with Write-Host, which does NOT go to the pipeline in
REM  PowerShell 5.1 - without this, Tee-Object receives nothing and
REM  writes an empty file while the console still looks correct.
REM
REM  The second statement re-encodes the result as UTF-8. Tee-Object
REM  writes UTF-16LE in PowerShell 5.1 and has no -Encoding parameter -
REM  that was only added in PowerShell 6. A UTF-16 file is twice the
REM  size and renders as gibberish anywhere UTF-8 is assumed, which
REM  matters because this file exists to be sent to someone else.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& '%SCRIPT%' 6>&1 | Tee-Object -FilePath '%OUT%'; if (Test-Path -LiteralPath '%OUT%') { (Get-Content -Raw -LiteralPath '%OUT%') | Set-Content -LiteralPath '%OUT%' -Encoding UTF8 }"

echo.
echo  ============================================================
if not exist "%OUT%" goto :nofile
for %%A in ("%OUT%") do if %%~zA LSS 200 goto :nofile

echo   Saved to: %OUT%
echo   Please send that file back. Thank you!
echo  ============================================================
echo.
pause
exit /b 0

:nofile
echo   COULD NOT SAVE the dump to:
echo   %OUT%
echo.
echo   Nothing was written, or the file is empty. Please copy the
echo   text from this window by hand and send that instead:
echo   right-click the title bar, Edit, Select All, then Enter.
echo  ============================================================
echo.
pause
exit /b 1
