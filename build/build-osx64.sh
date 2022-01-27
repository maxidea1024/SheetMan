#!/bin/bash
dotnet publish ../src/SheetMan.csproj --output ../bin --self-contained true --runtime osx-x64 -p:PublishSingleFile=true -r
mv ../bin/SheetMan ../bin/SheetMan-osx
