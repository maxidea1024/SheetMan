@echo off
dotnet publish ..\src\SheetMan.csproj --output ..\bin --self-contained true --runtime osx-x64 -p:PublishSingleFile=true -p:PublishTrimmed=true -r -c Release
ren ..\bin\SheetMan ..\bin\SheetMan-linux
