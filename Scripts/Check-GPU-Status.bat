@echo off
echo Checking DirectX and GPU Driver Status...
echo.

echo === DirectX Version ===
dxdiag /t dxdiag_output.txt
timeout /t 5
type dxdiag_output.txt | findstr /C:"DirectX Version"
echo.

echo === GPU Driver Version ===
nvidia-smi --query-gpu=driver_version --format=csv,noheader
echo.

echo === Windows Version ===
ver
echo.

echo === DirectML DLL Check ===
where directml.dll
if errorlevel 1 (
    echo DirectML.dll not found in PATH
) else (
    echo DirectML.dll found
)
echo.

echo.
echo === Recommendations ===
echo 1. Update NVIDIA drivers to latest version ^(560.x or newer^)
echo    Download from: https://www.nvidia.com/download/index.aspx
echo.
echo 2. Run Windows Update to get latest DirectX runtime
echo    Run: winver to check Windows version
echo.
echo 3. Your GPU ^(RTX 4060 Ti^) supports DirectML - just needs updated drivers
echo.
pause
