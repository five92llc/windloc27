@echo off
REM Builds a single-file WinLoc27.exe into windows-lock\dist (needs the .NET 8 SDK on Windows).
dotnet publish WinLoc27\WinLoc27.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
if errorlevel 1 exit /b 1
echo.
echo Built dist\WinLoc27.exe
