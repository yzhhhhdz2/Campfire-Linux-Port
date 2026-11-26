@echo off
setlocal
rem Campfire Linux port - stage 1 helper. Starts a tiny Java "fake LAN world"
rem so the official Windows Campfire.exe can HOST a room without Minecraft.
rem Pure ASCII on purpose: this folder path contains spaces and Chinese chars.

set "JDK=C:\jdk-17"
set "HERE=%~dp0"

if not exist "%JDK%\bin\java.exe" (
  echo [X] JDK not found: %JDK%
  echo     Edit the JDK= line at the top of this file.
  pause
  exit /b 1
)

"%JDK%\bin\javac.exe" -encoding UTF-8 -d "%HERE%." "%HERE%FakeMcWorld.java"
if errorlevel 1 (
  echo [X] Compile failed. See the message above.
  pause
  exit /b 1
)

echo.
echo [OK] Fake MC world starting on 127.0.0.1:25565
echo      Keep THIS WINDOW OPEN while Campfire hosts the room.
echo      Press Ctrl+C here to stop it.
echo.
"%JDK%\bin\java.exe" -cp "%HERE%." FakeMcWorld 25565
