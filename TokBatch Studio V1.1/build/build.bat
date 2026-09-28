@echo off
setlocal
cd /d "%~dp0.."
if not exist build mkdir build
set GOOS=windows
set GOARCH=amd64
set CGO_ENABLED=0
go build -trimpath -ldflags="-H windowsgui -s -w" -o build\TokBatch_Studio_V1_1_rebuilt.exe .\src
if errorlevel 1 exit /b %errorlevel%
echo Build concluido: build\TokBatch_Studio_V1_1_rebuilt.exe
