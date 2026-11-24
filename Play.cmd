@echo off
rem Fantasy World Simulator launcher: builds the C# projects, imports assets once, then starts the game.
rem Uses the Godot 4.7.2 .NET installed by Scoop. Controls are listed in the game (bottom-left and inspector).
setlocal
set "GODOT=%USERPROFILE%\scoop\apps\godot-mono\current\godot-mono.exe"
if not exist "%GODOT%" (
  echo Godot .NET 4.7.2 not found at "%GODOT%". Install with: scoop install godot-mono
  exit /b 1
)
dotnet build "%~dp0Game\Game.csproj" -nologo -v q || exit /b 1
if not exist "%~dp0Game\.godot\imported" "%GODOT%" --headless --path "%~dp0Game" --import
start "" "%GODOT%" --path "%~dp0Game" %*
