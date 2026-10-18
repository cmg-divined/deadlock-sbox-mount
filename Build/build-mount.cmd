@echo off
rem builds mount at Build\mount\deadlock.
dotnet build "%~dp0MountPackage\deadlock.csproj" -c Release -nologo -v q
if errorlevel 1 exit /b 1
echo.
echo "Built %~dp0mount\deadlock - copy that folder into sbox\mount\"
