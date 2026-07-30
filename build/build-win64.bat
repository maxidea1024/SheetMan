@echo off
rem Publishes a self-contained single-file SheetMan for win-x64 into ..\bin.
rem
rem PublishTrimmed is deliberately off: NPOI, Newtonsoft.Json and Google.Apis all
rem resolve types by reflection, and trimming strips members they need at runtime.
pushd "%~dp0"

dotnet publish ..\src\SheetMan.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output ..\bin

popd
