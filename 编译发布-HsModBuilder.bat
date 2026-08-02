@echo off
title HsModBuilder 一键编译
cd /d "%~dp0HsModBuilder"

powershell -NoProfile -Command "if (Get-Process HsModBuilder -ErrorAction SilentlyContinue) { exit 1 }"
if errorlevel 1 (
  echo 检测到 HsModBuilder 正在运行，请先关闭它再编译。
  pause
  exit /b 1
)

echo ==============================================
echo  正在编译（自包含，exe 很小 + 依赖文件夹，请稍候）...
echo ==============================================
if exist "%~dp0HsModBuilder发布" rmdir /s /q "%~dp0HsModBuilder发布"
dotnet publish -c Release -o "%~dp0HsModBuilder发布" -v q
if errorlevel 1 (
  echo.
  echo 编译失败！请确认已安装 .NET SDK 8。
  pause
  exit /b 1
)
if exist "%~dp0HsModBuilder发布\HsModBuilder.pdb" del /q "%~dp0HsModBuilder发布\HsModBuilder.pdb"
rem ===== 清理多余语言包（保留中英文）=====
for %%L in (cs de es fr it ja ko pl pt-BR ru tr) do rd /s /q "%~dp0HsModBuilder发布\%%L" 2>nul
del /q "%~dp0HsModBuilder发布\createdump.exe" "%~dp0HsModBuilder发布\mscordaccore_amd64_*.dll" "%~dp0HsModBuilder发布\mscordbi.dll" "%~dp0HsModBuilder发布\clretwrc.dll" 2>nul
echo.
echo 编译成功！
echo 软件位置：%~dp0HsModBuilder发布\HsModBuilder.exe（整个文件夹一起用）
echo 整个发布文件夹拷到任何 64 位 Windows 免环境运行，无需安装 .NET SDK。
pause
