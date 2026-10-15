@echo off
setlocal
set "ROOT=%~dp0"
set "NEED_BUILD="
if not exist "%ROOT%cmake-build-debug\app\DynamicNeuralEngine.Trainer.dll" set "NEED_BUILD=1"
if not exist "%ROOT%cmake-build-debug\app\DynamicNeuralEngine.dll" set "NEED_BUILD=1"
if not exist "%ROOT%cmake-build-debug\app\DynamicNeuralEngine.Trainer.runtimeconfig.json" set "NEED_BUILD=1"
if defined NEED_BUILD (
    call "%ROOT%build.cmd"
    if errorlevel 1 (
        pause
        exit /b 1
    )
)
set "DOTNET=dotnet"
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
start "DynamicNeuralEngine Web Server" /D "%ROOT%" "%DOTNET%" "%ROOT%cmake-build-debug\app\DynamicNeuralEngine.Trainer.dll" ui current --models "%ROOT%models" --workspace "%ROOT%." --learn-web
exit /b 0
