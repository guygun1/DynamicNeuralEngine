@echo off
if "%~1"=="" (
    call "%~dp0run.cmd" models-help
) else (
    call "%~dp0run.cmd" %*
)
exit /b %errorlevel%
