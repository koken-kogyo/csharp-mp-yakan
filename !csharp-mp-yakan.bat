@echo off
set logDate=%date:~0,4%-%date:~5,2%-%date:~8,2%
set yyyyMMdd=%date:/=%
set logFile=!csharp-mp-yakan_%yyyyMMdd%.log
echo.
echo 切削夜間バッチ [%logDate%]
echo.
echo %DATE% %TIME% 夜間バッチ開始 > %logFile%
csharp-mp-yakan.exe >> %logFile%
echo %DATE% %TIME% 夜間バッチ終了 >> %logFile%
exit 0
