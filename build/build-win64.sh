#!/bin/bash
dotnet publish ../src/SheetMan.csproj --output ../bin --self-contained true --runtime win-x64 -p:PublishSingleFile=true -r
