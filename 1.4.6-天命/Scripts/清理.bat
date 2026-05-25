@echo off
setlocal

REM 清理当前开发项目的编译缓存（不会影响代码和配置）
REM 目标：只清理现行项目，不动“天命原版”目录

set ROOT=%~dp0..

set APP_BIN="%ROOT%\Core\App\bin"
set APP_OBJ="%ROOT%\Core\App\obj"

echo 清理编译缓存目录...
if exist %APP_BIN% (
    echo 删除 %APP_BIN%
    rmdir /S /Q %APP_BIN%
) else (
    echo 跳过：未找到 %APP_BIN%
)

if exist %APP_OBJ% (
    echo 删除 %APP_OBJ%
    rmdir /S /Q %APP_OBJ%
) else (
    echo 跳过：未找到 %APP_OBJ%
)

echo.
echo 清理完成。下次编译时會自動重新生成 bin/obj 目录。

endlocal
pause
