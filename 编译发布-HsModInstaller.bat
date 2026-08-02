@echo off
title HsModInstaller 一键编译
cd /d "%~dp0HsModInstaller"

echo ==============================================
echo  正在编译（自包含，exe 很小 + 依赖文件夹）...
echo ==============================================
del /q "%~dp0HsModInstaller发布\HsModInstaller.exe" "%~dp0HsModInstaller发布\HsModInstaller.dll" "%~dp0HsModInstaller发布\HsModInstaller.deps.json" "%~dp0HsModInstaller发布\HsModInstaller.runtimeconfig.json" "%~dp0HsModInstaller发布\HsModInstaller.pdb" 2>nul
dotnet publish -c Release -o "%~dp0HsModInstaller发布" -v q
if errorlevel 1 (
  echo.
  echo 编译失败！请确认已安装 .NET SDK 8。
  pause
  exit /b 1
)
if exist "%~dp0HsModInstaller发布\HsModInstaller.pdb" del /q "%~dp0HsModInstaller发布\HsModInstaller.pdb"
rem ===== 清理多余语言包（保留中英文）=====
for %%L in (cs de es fr it ja ko pl pt-BR ru tr) do rd /s /q "%~dp0HsModInstaller发布\%%L" 2>nul
del /q "%~dp0HsModInstaller发布\createdump.exe" "%~dp0HsModInstaller发布\mscordaccore_amd64_*.dll" "%~dp0HsModInstaller发布\mscordbi.dll" "%~dp0HsModInstaller发布\clretwrc.dll" 2>nul
echo.
echo 编译成功！
echo 软件位置：%~dp0HsModInstaller发布\HsModInstaller.exe（整个文件夹一起用）
echo 配置文件：首次运行自动生成在 exe 旁边（已保留旧配置）
echo.
echo 提示：若编译失败请先关闭正在运行的软件。
pause
