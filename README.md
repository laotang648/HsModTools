# HsMod 工具集

炉石传说 HsMod 插件的两个配套小工具（Windows，免安装）。

## 工具列表

| 工具 | 作用 | 发布文件夹 |
|---|---|---|
| **HsModInstaller** | 一键把 HsMod.dll 配置进炉石（自动检测/安装 BepInEx、健康检查、覆盖安装） | `HsModInstaller发布\` |
| **HsModBuilder** | 把 HsMod 源码编译成 HsMod.dll（内置编译器，无需安装 .NET SDK） | `HsModBuilder发布\` |

## 使用流程

```
HsModBuilder（编译工具）→ 产出 HsMod.dll
        ↓
HsModInstaller（配置工具）→ 把 dll 装进炉石 BepInEx
        ↓
启动游戏 → Esc → Mod Settings
```

## 下载

编译好的成品（免安装文件夹）在 **Releases** 页面下载，解压后整个文件夹拷到任意 64 位 Windows 电脑即可运行。

## 从源码编译

需要 .NET SDK 8：

```bash
# HsModInstaller
cd HsModInstaller
dotnet publish -c Release -o ..\HsModInstaller发布

# HsModBuilder（需要先准备 Frameworks\v48，见下）
cd HsModBuilder
dotnet publish -c Release -o ..\HsModBuilder发布
```

也可以直接双击仓库里的 `编译发布.bat` 和 `编译发布-HsModBuilder.bat`。

> **HsModBuilder 的特殊依赖**：编译引擎内置 Roslyn 编译器，但 .NET Framework 4.8 引用程序集（`Frameworks\v48\`，约 44MB）因体积原因未入库。重新编译 HsModBuilder 前，需要从 `.NET Framework 4.8 Developer Pack` 复制：
> ```
> C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\*.dll
> → HsModBuilder\Frameworks\v48\
> ```
> 一般用户无需重编译，直接用 Releases 里的成品即可。

## 注意事项

- 两个工具的发布文件夹是**自包含**的，必须整个文件夹一起拷贝使用（exe 依赖旁边的 dll）
- 炉石传说为 64 位程序，BepInEx 必须使用 x64 版（5.4.23.x）
- 插件仅供学习研究使用，注意账号安全
