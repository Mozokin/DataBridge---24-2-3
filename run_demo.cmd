@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo Install .NET 8 SDK first. & pause & exit /b 1)
dotnet build DataBridge.sln
if errorlevel 1 (pause & exit /b 1)
if not exist results mkdir results
dotnet run --project DataBridge.Tests --no-build > results\tests.txt 2>&1
set TESTCODE=%errorlevel%
type results\tests.txt
if not "%TESTCODE%"=="0" (pause & exit /b 1)
dotnet run --project DataBridge --no-build -- convert samples/input.csv --to json --config configs/schema.json --output samples/output.json --report samples/report.html --errors samples/errors.csv
if errorlevel 3 (pause & exit /b 1)
if errorlevel 2 goto success
if errorlevel 1 (pause & exit /b 1)
:success
dotnet run --project DataBridge --no-build -- stats samples/input.xlsx
start "" "%~dp0samples\report.html"
pause
