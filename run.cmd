@echo off
setlocal
set "ROOT=%~dp0"
if not defined DNE_MODELS set "DNE_MODELS=%ROOT%models"
if not defined DNE_DEFAULT_BRAIN set "DNE_DEFAULT_BRAIN=%ROOT%DynamicNeuralEngine.Trainer\agi_mind.brain"
if /I "%~1"=="rebuild" (
    call "%ROOT%build.cmd"
    if errorlevel 1 exit /b 1
    exit /b 0
)
set "NEED_BUILD="
if not exist "%ROOT%cmake-build-debug\app\DynamicNeuralEngine.Trainer.dll" set "NEED_BUILD=1"
if not exist "%ROOT%cmake-build-debug\app\DynamicNeuralEngine.dll" set "NEED_BUILD=1"
if not exist "%ROOT%cmake-build-debug\app\DynamicNeuralEngine.Trainer.runtimeconfig.json" set "NEED_BUILD=1"
if defined NEED_BUILD (
    call "%ROOT%build.cmd"
    if errorlevel 1 exit /b 1
)
dotnet "%ROOT%cmake-build-debug\app\DynamicNeuralEngine.Trainer.dll" %*
exit /b %errorlevel%
