#炉石插件齿轮全自动配置工具

# HsMod 工具集

炉石传说 HsMod 插件的两个配套工具（Windows，免安装，无需 .NET 环境）。

- **HsModInstaller**：一键把 HsMod.dll 配置进炉石传说
- **HsModBuilder**：把 HsMod 源码编译成 HsMod.dll（内置编译器，无需安装 .NET SDK）

HsModInstaller 支持：自动检测/安装 BepInEx、位数匹配检查、健康检查、覆盖安装、**一键还原配置**（卸载插件环境，删除前自动备份）、**自定义备份目录**。

---

## 一、下载

在 **Releases** 页面下载两个 zip：

| 文件 | 内容 | 体积 |
|---|---|---|
| `HsModInstaller.zip` | 配置工具（把 dll 装进游戏） | 约 145 MB |
| `HsModBuilder.zip` | 编译工具（源码 → dll） | 约 241 MB |

下载后**解压**（不要直接双击 zip 里的 exe）。解压出的文件夹必须**整个一起用**（exe 依赖旁边的 dll），不能只拷单个 exe。

---

## 二、操作步骤（使用流程）

### 第 1 步：获取 HsMod 源码或 dll

- 已有编译好的 `HsMod.dll` → 直接进第 2 步
- 只有源码 → 先用 **HsModBuilder** 编译（见下方"编译工具用法"）

### 第 2 步：打开 HsModInstaller

解压 `HsModInstaller.zip` 后双击 `HsModInstaller.exe`，界面：

```
游戏目录:        [自动检测/手动选择炉石安装目录]
HsMod.dll:       [选择你的 HsMod.dll]
BepInEx 源:      [游戏没有 BepInEx 时才需要填]
推荐 BepInEx：   BepInEx_win_x64_5.4.23.5.zip（点击去下载）
```

### 第 3 步：确认游戏目录

- 软件启动会自动检测游戏目录（找到 `Hearthstone.exe` 的文件夹），检测不到就手动点"浏览..."选择
- 游戏目录正确时，BepInEx 状态行会显示：
  - 绿色 `OK 已检测到 BepInEx` → 无需管 BepInEx，直接下一步
  - 红色 `! 未检测到 BepInEx` → 需要下载 BepInEx 并在"BepInEx 源"里选 zip 或解压目录

### 第 4 步：选择 HsMod.dll

点"HsMod.dll"行的"浏览..."，选中你的 `HsMod.dll`。

### 第 5 步：开始配置

点 **[开始配置]**，日志区会显示 6 步进度：

```
[1/6] BepInEx 检查/安装
[2/6] 位数匹配检查（游戏 x64 必须配 x64 注入器）
[3/6] doorstop_config.ini 配置
[4/6] UnstrippedCorlib 标准库
[5/6] 复制 HsMod.dll 到 BepInEx\plugins
[6/6] 完成
```

看到"配置完成！"即成功。

### 第 6 步：启动游戏验证

1. 双击 `Hearthstone.exe` 启动游戏
2. **首次进入后退出并重启一次**（插件首次运行会更新配置）
3. 游戏内按 **Esc** → 菜单里出现 **"Mod Settings"** → 所有功能在此配置

---

## 三、编译工具用法（HsModBuilder）

把 HsMod 源码编译成 `HsMod.dll`：

1. 双击 `HsModBuilder.exe`
2. **HsMod 源码目录**：选包含 `HsMod.csproj` 的文件夹（如解压后的 HsMod 源码根目录）
3. **输出目录(可选)**：留空则输出到 `<源码目录>\Release\HsMod.dll`；也可以填一个目录（如游戏 `BepInEx\plugins`），编译完自动复制一份过去
4. 点 **[开始编译]**，等日志出现"编译成功！"
5. 拿到 `HsMod.dll` 后用 **HsModInstaller** 配置进游戏

---

## 四、常见问题

| 问题 | 解决方法 |
|---|---|
| BepInEx 状态显示红色"未检测到" | 下载 `BepInEx_win_x64_5.4.23.5.zip`，在"BepInEx 源"里选择 |
| 提示"位数不匹配" | 炉石是 64 位，必须用 **x64 版** BepInEx |
| 提示"游戏正在运行" | 先关闭炉石再配置（游戏运行时文件被锁定） |
| 配置后游戏里没有 Mod Settings | 看 `BepInEx\LogOutput.log` 有无报错；或勾选"覆盖安装 BepInEx"重装一次 |
| 插件界面是英文 | 游戏内 Mod Settings → Global → HsMod Language 改成 zhCN，重启游戏 |
| 想卸载/还原插件 | 点 **[还原配置]**：移除 BepInEx、winhttp.dll、doorstop_config.ini 等，恢复到未配置状态（删除前自动备份） |
| 备份目录改哪里 | 界面"备份目录"行填任意路径（默认软件目录下 backup 文件夹），还原时自动备份到该目录 |

---

## 五、从源码编译（开发者）

需要 .NET SDK 8：

```bash
# HsModInstaller
cd HsModInstaller
dotnet publish -c Release -o ..\HsModInstaller发布

# HsModBuilder
cd HsModBuilder
dotnet publish -c Release -o ..\HsModBuilder发布
```

也可以直接双击仓库里的 `编译发布.bat` 和 `编译发布-HsModBuilder.bat`。

> **HsModBuilder 的特殊依赖**：编译引擎内置 Roslyn 编译器，但 .NET Framework 4.8 引用程序集（`Frameworks\v48\`，约 44MB）因体积原因未入库。重新编译 HsModBuilder 前需从 `.NET Framework 4.8 Developer Pack` 复制：
> ```
> C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\*.dll
> → HsModBuilder\Frameworks\v48\
> ```
> 一般用户无需重编译，直接用 Releases 成品即可。

---

## 六、注意事项

- 炉石传说是 64 位程序，BepInEx 必须用 **x64 版 5.4.x**（不支持 6.x）
- 插件仅供学习研究使用，注意账号安全
- 修改插件源码后重新编译即可更新，无需重装 BepInEx
