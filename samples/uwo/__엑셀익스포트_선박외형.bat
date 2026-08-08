CHCP 65001
SET CURRDIR=%~dp0
SET TOOLPATH=%CURRDIR%..\..\tools\CMSExportApp\CMSExportAppCli.exe
SET FILEPATH=%CURRDIR%UWO_선박외형.xlsx
SET CONFIGPATH=%CURRDIR%__EXPORT_CONFIG.json

REM --console
REM --export
REM --file
REM --sheetNames
REM --config
REM --valid
REM --tile

CALL %TOOLPATH% --console --export --file %FILEPATH% --config %CONFIGPATH% --sheetNames ShipBodyShape ShipPartShape SailCrest SailPattern SailPatternColor ShipBodyFirstColor ShipBodySecondColor ShipWakeFX
PAUSE