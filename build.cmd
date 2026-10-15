@echo off
setlocal
set "ROOT=%~dp0"
if not exist "%ROOT%cmake-build-debug\CMakeCache.txt" (
    echo Configure the CMake project in CLion first, using cmake-build-debug.
    exit /b 1
)
cmake --build "%ROOT%cmake-build-debug" --parallel 4
if errorlevel 1 exit /b 1
dotnet build "%ROOT%DynamicNeuralEngine.Trainer\DynamicNeuralEngine.Trainer.csproj" --output "%ROOT%cmake-build-debug\app"
exit /b %errorlevel%
