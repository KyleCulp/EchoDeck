@echo off
timeout /t 8 /nobreak >nul
cd /d C:\Users\me\Repos\nv-aec-bridge\bin\Release\net8.0\win-x64\publish
set PATH=C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK;%PATH%
nv-aec-bridge.exe >> C:\Users\me\Repos\nv-aec-bridge\bridge.log 2>&1
