@echo off
rem dotnet publish ..\src\SheetMan.csproj --output ..\bin --self-contained true --runtime win-x64 -p:PublishSingleFile=true -p:PublishTrimmed=true -r -c Release
dotnet publish ..\src\SheetMan.csproj --output ..\bin --self-contained true --runtime win-x64 -p:PublishSingleFile=true -r -c Release
