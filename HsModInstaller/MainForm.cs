using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace HsModInstaller;

/// <summary>PE 头位数检测</summary>
public static class PeArch
{
    public static string Get(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        fs.Seek(0x3C, SeekOrigin.Begin);
        int peOffset = br.ReadInt32();
        fs.Seek(peOffset + 4, SeekOrigin.Begin);
        ushort machine = br.ReadUInt16();
        return machine switch
        {
            0x8664 => "x64",
            0x014C => "x86",
            _ => $"0x{machine:X4}"
        };
    }
}

/// <summary>安装核心逻辑</summary>
public static class Installer
{
    /// <summary>健康检查：返回 BepInEx 安装状态描述（OK 开头 = 正常）</summary>
    public static string DescribeBepInEx(string gameDir)
    {
        var bepDir = Path.Combine(gameDir, "BepInEx");
        string core = Path.Combine(bepDir, "core", "BepInEx.dll");
        var parts = new List<string>();
        try
        {
            var v = AssemblyName.GetAssemblyName(core).Version;
            if (v.Major == 6) parts.Add("版本 " + v + "（BepInEx 6 不受支持，建议换 5.x）");
            else if (v >= new Version(5, 4, 23)) parts.Add("版本 " + v);
            else parts.Add("版本 " + v + "（偏旧，建议 5.4.23.x）");
        }
        catch { parts.Add("版本读取失败"); }

        string winhttp = Path.Combine(gameDir, "winhttp.dll");
        string gameArch = PeArch.Get(Path.Combine(gameDir, "Hearthstone.exe"));
        if (File.Exists(winhttp))
        {
            string wa = PeArch.Get(winhttp);
            parts.Add(wa == gameArch ? "注入器位数匹配" : "! 注入器位数不匹配（游戏 " + gameArch + " / winhttp " + wa + "）");
        }
        else parts.Add("! 缺 winhttp.dll（注入文件）");

        string cfg = Path.Combine(gameDir, "doorstop_config.ini");
        if (File.Exists(cfg))
        {
            string txt = File.ReadAllText(cfg);
            parts.Add(txt.Contains("dll_search_path_override") && txt.Contains("unstripped_corlib") ? "doorstop 已配置" : "! doorstop 缺少 dll_search_path_override");
        }
        else parts.Add("! 缺 doorstop_config.ini");

        parts.Add(File.Exists(Path.Combine(bepDir, "unstripped_corlib", "mscorlib.dll")) ? "标准库齐全" : "! 缺 unstripped_corlib 标准库");

        bool healthy = !parts.Any(x => x.Contains("!"));
        return (healthy ? "OK " : "! ") + string.Join(" | ", parts);
    }

    /// <summary>游戏目录是否已安装 BepInEx</summary>
    public static bool HasBepInEx(string gameDir) =>
        !string.IsNullOrWhiteSpace(gameDir) &&
        File.Exists(Path.Combine(gameDir, "BepInEx", "core", "BepInEx.dll"));

    public static bool IsGameDir(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "Hearthstone.exe"));

    /// <summary>自动检测炉石游戏目录</summary>
    public static string? DetectGameDir()
    {
        // 1. 正在运行的 Hearthstone 进程
        try
        {
            foreach (var p in Process.GetProcessesByName("Hearthstone"))
            {
                try
                {
                    var dir = Path.GetDirectoryName(p.MainModule?.FileName);
                    if (IsGameDir(dir)) return dir;
                }
                catch { }
            }
        }
        catch { }

        // 2. 常见安装路径
        string[] candidates =
        {
            @"C:\Program Files (x86)\Hearthstone",
            @"C:\Program Files\Hearthstone",
            @"D:\Hearthstone",
            @"E:\Hearthstone",
            @"C:\Hearthstone",
        };
        foreach (var c in candidates)
            if (IsGameDir(c)) return c;

        return null;
    }

    /// <summary>查找 UnstrippedCorlib 目录（项目目录 或 HsMod.dll 附近）</summary>
    public static string? FindUnstrippedCorlibDir(string? projectDir, string? hsModDllPath)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(projectDir)) candidates.Add(projectDir);
        if (!string.IsNullOrWhiteSpace(hsModDllPath))
        {
            var dllDir = Path.GetDirectoryName(hsModDllPath);
            if (dllDir != null)
            {
                candidates.Add(dllDir);
                candidates.Add(Path.GetDirectoryName(dllDir)); // Release 的上一级（项目根）
            }
        }
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            try
            {
                var dir = Path.Combine(c, "UnstrippedCorlib");
                if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.dll").Length > 0)
                    return dir;
            }
            catch { }
        }
        return null;
    }

    /// <summary>主流程</summary>
    public static void Run(string gameDir, string hsModDll, string? bepinexSource, string? projectDir, bool forceReinstall, Action<string> log)
    {
        log("==============================================");
        log("HsMod 自动配置开始");
        log($"游戏目录   : {gameDir}");
        log($"HsMod.dll  : {hsModDll}");
        if (!IsGameDir(gameDir)) throw new Exception($"游戏目录无效：{gameDir}（未找到 Hearthstone.exe）");
        if (!File.Exists(hsModDll)) throw new Exception($"HsMod.dll 不存在：{hsModDll}");

        // 游戏运行时 BepInEx 目录会被进程映射锁定，必须关闭游戏再配置（只拦截该游戏目录的进程）
        foreach (var p in Process.GetProcessesByName("Hearthstone"))
        {
            try
            {
                var exePath = p.MainModule?.FileName;
                if (exePath != null && Path.GetDirectoryName(exePath)!.Equals(gameDir, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("检测到 Hearthstone 正在运行！请先关闭游戏再执行配置（BepInEx 文件正被游戏进程占用）。");
            }
            catch { }
        }

        string bepInExDir = Path.Combine(gameDir, "BepInEx");
        bool hasBepInEx = File.Exists(Path.Combine(bepInExDir, "core", "BepInEx.dll"));

        // ---- 1. BepInEx ----
        if (hasBepInEx && !forceReinstall)
        {
            log("[1/6] 检测到游戏目录已有 BepInEx，跳过安装。");
            log("     健康检查：" + DescribeBepInEx(gameDir));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(bepinexSource))
                throw new Exception(hasBepInEx
                    ? "已勾选覆盖安装，请填写 BepInEx 源（zip 压缩包或解压目录）。"
                    : "游戏目录没有 BepInEx，请选择 BepInEx 的 zip 压缩包或已解压目录。");
            if (hasBepInEx)
            {
                string bak = Path.Combine(gameDir, "BepInEx_backup_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                Directory.Move(bepInExDir, bak);
                log("[1/6] 覆盖安装：旧 BepInEx 已备份为 " + Path.GetFileName(bak));
            }
            log("[1/6] 安装 BepInEx ...");
            InstallBepInEx(bepinexSource, gameDir, log);
            if (!File.Exists(Path.Combine(bepInExDir, "core", "BepInEx.dll")))
                throw new Exception("BepInEx 安装后校验失败：未找到 BepInEx\\core\\BepInEx.dll");
        }

        // ---- 2. 位数匹配（64 位炉石必须配 x64 注入器）----
        log("[2/6] 检查程序位数匹配 ...");
        string gameArch = PeArch.Get(Path.Combine(gameDir, "Hearthstone.exe"));
        string winhttp = Path.Combine(gameDir, "winhttp.dll");
        log($"  Hearthstone.exe : {gameArch}");
        if (File.Exists(winhttp))
        {
            string wArch = PeArch.Get(winhttp);
            log($"  winhttp.dll    : {wArch}");
            if (wArch != gameArch)
                throw new Exception($"位数不匹配！游戏是 {gameArch}，但 winhttp.dll 是 {wArch}。请改用 {gameArch} 版 BepInEx。");
            log("  注入器位数与游戏匹配 OK");
        }
        else
        {
            log("  警告：未找到 winhttp.dll（BepInEx 注入文件缺失，插件将无法加载）");
        }

        // ---- 3. doorstop_config.ini ----
        log("[3/6] 配置 doorstop_config.ini ...");
        string cfg = Path.Combine(gameDir, "doorstop_config.ini");
        if (File.Exists(cfg))
        {
            EnsureDoorstopConfig(cfg);
            log("  dll_search_path_override = BepInEx\\unstripped_corlib OK");
        }
        else
        {
            log("  警告：doorstop_config.ini 不存在（BepInEx 未完整安装）");
        }

        // ---- 4. UnstrippedCorlib 标准库 ----
        log("[4/6] 复制 UnstrippedCorlib 标准库 ...");
        string? uc = FindUnstrippedCorlibDir(projectDir, hsModDll);
        if (uc != null)
        {
            var dest = Path.Combine(bepInExDir, "unstripped_corlib");
            Directory.CreateDirectory(dest);
            int n = 0, fail = 0;
            foreach (var f in Directory.GetFiles(uc, "*.dll"))
            {
                try
                {
                    CopyWithRetry(f, Path.Combine(dest, Path.GetFileName(f)));
                    n++;
                }
                catch (Exception ex)
                {
                    fail++;
                    log("    跳过(文件被占用): " + Path.GetFileName(f) + " - " + ex.Message);
                }
            }
            log($"  已复制 {n} 个标准库到 BepInEx\\unstripped_corlib OK" + (fail > 0 ? $"（{fail} 个被占用跳过，文件已存在则无影响）" : ""));
        }
        else
        {
            log("  未找到 UnstrippedCorlib 目录（可选：填写 HsMod 项目目录可自动复制）");
        }

        // ---- 5. HsMod.dll ----
        log("[5/6] 复制 HsMod.dll 到 plugins ...");
        var plugins = Path.Combine(bepInExDir, "plugins");
        Directory.CreateDirectory(plugins);
        try
        {
            CopyWithRetry(hsModDll, Path.Combine(plugins, "HsMod.dll"));
            log("  已复制到 BepInEx\\plugins\\HsMod.dll OK");
        }
        catch (Exception ex)
        {
            throw new Exception("复制 HsMod.dll 失败（文件可能被占用）：" + ex.Message);
        }

        // ---- 6. 完成 ----
        log("[6/6] 配置完成！");
        log("启动游戏验证：首次进入后重启一次游戏，按 Esc 打开 Mod Settings。");
        log("==============================================");
    }

    private static void InstallBepInEx(string source, string gameDir, Action<string> log)
    {
        if (Directory.Exists(source))
        {
            string bepDir = Path.Combine(gameDir, "BepInEx");
            if (Directory.Exists(Path.Combine(source, "BepInEx", "core")))
            {
                // 解压后的 zip 结构：BepInEx/ + winhttp.dll + doorstop_config.ini
                CopyDir(Path.Combine(source, "BepInEx"), bepDir);
                foreach (var n in new[] { "winhttp.dll", "doorstop_config.ini" })
                {
                    var f = Path.Combine(source, n);
                    if (File.Exists(f)) File.Copy(f, Path.Combine(gameDir, n), true);
                }
            }
            else if (Directory.Exists(Path.Combine(source, "core")))
            {
                // BepInEx 本体目录
                CopyDir(source, bepDir);
            }
            else
            {
                throw new Exception($"无法识别 BepInEx 目录结构：{source}");
            }
            log("  已从目录复制 BepInEx OK");
        }
        else if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZipForce(source, gameDir);
            log("  已解压 BepInEx zip OK");
        }
        else
        {
            throw new Exception($"BepInEx 源无效（需 zip 文件或目录）：{source}");
        }
    }

    private static void ExtractZipForce(string zipPath, string destDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            string target = Path.Combine(destDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            if (entry.FullName.EndsWith("/"))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void CopyDir(string src, string dst)
    {
        foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, d)));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, true);
        }
    }

    /// <summary>复制文件，失败时删除目标重试（目标被游戏进程映射时覆盖写会失败）</summary>
    private static void CopyWithRetry(string src, string dst)
    {
        try { File.Copy(src, dst, true); return; }
        catch { }
        try { File.Delete(dst); } catch { }
        File.Copy(src, dst, true);
    }

    private static void EnsureDoorstopConfig(string cfgPath)
    {
        var lines = File.ReadAllLines(cfgPath).ToList();

        // enabled = true
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("enabled") && lines[i].Contains('='))
            {
                lines[i] = "enabled = true";
                break;
            }
        }

        // dll_search_path_override = BepInEx\\unstripped_corlib（[UnityMono] 段）
        const string line = "dll_search_path_override = BepInEx\\unstripped_corlib";
        int overrideIdx = lines.FindIndex(l => l.TrimStart().StartsWith("dll_search_path_override"));
        if (overrideIdx >= 0)
        {
            lines[overrideIdx] = line;
        }
        else
        {
            int unityMono = lines.FindIndex(l => l.Trim().Equals("[UnityMono]", StringComparison.OrdinalIgnoreCase));
            if (unityMono >= 0) lines.Insert(unityMono + 1, line);
            else { lines.Add("[UnityMono]"); lines.Add(line); }
        }

        File.WriteAllLines(cfgPath, lines);
    }
}

/// <summary>配置文件（保存上次填写的路径，方便以后更新修改）</summary>
public class AppSettings
{
    public string? GameDir { get; set; }
    public string? HsModDll { get; set; }
    public string? BepInExSource { get; set; }
    public string? ProjectDir { get; set; }
}

/// <summary>主窗体</summary>
public class MainForm : Form
{
    private readonly TextBox txtGameDir = new();
    private readonly TextBox txtHsMod = new();
    private readonly TextBox txtBepInEx = new();
    private readonly TextBox txtProject = new();
    private readonly RichTextBox log = new();
    private readonly Button btnInstall = new();
    private readonly Button btnLaunch = new();
    private readonly Label lblBepState = new();
    private FlowLayoutPanel flow2 = null!;
    private readonly CheckBox chkForce = new();

    public MainForm()
    {
        Text = "HsMod 自动配置工具 v1.5";
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(880, 660);
        MinimumSize = new Size(760, 540);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            Padding = new Padding(12)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // ---- 行0: 游戏目录 ----
        table.Controls.Add(MakeLabel("游戏目录:"), 0, 0);
        txtGameDir.Dock = DockStyle.Fill; txtGameDir.Margin = new Padding(0, 6, 8, 6);
        table.Controls.Add(txtGameDir, 1, 0);
        var flow0 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        flow0.Controls.Add(MakeButton("自动检测", (s, e) => AutoDetectGame()));
        flow0.Controls.Add(MakeButton("浏览...", (s, e) => BrowseFolder(txtGameDir, "选择炉石传说游戏目录（含 Hearthstone.exe）")));
        table.Controls.Add(flow0, 2, 0);

        // ---- 行1: HsMod.dll ----
        table.Controls.Add(MakeLabel("HsMod.dll:"), 0, 1);
        txtHsMod.Dock = DockStyle.Fill; txtHsMod.Margin = new Padding(0, 6, 8, 6);
        table.Controls.Add(txtHsMod, 1, 1);
        var flow1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        flow1.Controls.Add(MakeButton("浏览...", (s, e) => BrowseFile(txtHsMod, "选择 HsMod.dll（插件网站下载的）", "DLL 文件|*.dll")));
        table.Controls.Add(flow1, 2, 1);

        // ---- 行2: BepInEx 源 ----
        table.Controls.Add(MakeLabel("BepInEx 源:"), 0, 2);
        txtBepInEx.Dock = DockStyle.Fill; txtBepInEx.Margin = new Padding(0, 6, 8, 6);
        table.Controls.Add(txtBepInEx, 1, 2);
        flow2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        flow2.Controls.Add(MakeButton("选 zip...", (s, e) => BrowseFile(txtBepInEx, "选择 BepInEx zip 压缩包（游戏没有 BepInEx 时需要）", "ZIP 文件|*.zip")));
        flow2.Controls.Add(MakeButton("选目录...", (s, e) => BrowseFolder(txtBepInEx, "选择 BepInEx 解压目录或 BepInEx 本体目录")));
        lblBepState.AutoSize = true; lblBepState.Margin = new Padding(8, 12, 0, 0);
        flow2.Controls.Add(lblBepState);
        table.Controls.Add(flow2, 2, 2);

        // ---- 行3: HsMod 项目目录 ----
        table.Controls.Add(MakeLabel("HsMod 项目目录:"), 0, 3);
        txtProject.Dock = DockStyle.Fill; txtProject.Margin = new Padding(0, 6, 8, 6);
        table.Controls.Add(txtProject, 1, 3);
        var flow3 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        flow3.Controls.Add(MakeButton("浏览...", (s, e) => BrowseFolder(txtProject, "选择 HsMod 项目目录（含 UnstrippedCorlib 文件夹，可选）")));
        table.Controls.Add(flow3, 2, 3);

        // ---- 行4: 推荐 BepInEx 提示 ----
        var bepTip = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        bepTip.Controls.Add(new Label { Text = "推荐 BepInEx：", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 4, 4, 0) });
        var lnk = new LinkLabel
        {
            Text = "BepInEx_win_x64_5.4.23.5.zip（x64 版 5.x 系列，点击前往官方下载页）",
            AutoSize = true, LinkColor = Color.FromArgb(0, 90, 158), Margin = new Padding(0, 2, 0, 0)
        };
        lnk.LinkClicked += (s, e) => Process.Start(new ProcessStartInfo("https://github.com/BepInEx/BepInEx/releases") { UseShellExecute = true });
        bepTip.Controls.Add(lnk);
        table.Controls.Add(bepTip, 0, 4);
        table.SetColumnSpan(bepTip, 3);

        // ---- 行5: 日志（占满剩余空间）----
        log.Dock = DockStyle.Fill; log.Margin = new Padding(0, 6, 0, 6);
        log.ReadOnly = true; log.BackColor = Color.White; log.Font = new Font("Consolas", 9F);
        table.Controls.Add(log, 0, 5);
        table.SetColumnSpan(log, 3);

        // ---- 行5: 底部按钮 ----
        var flowBottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        btnInstall.Text = "开始配置"; btnInstall.Size = new Size(140, 34); btnInstall.Margin = new Padding(0, 6, 8, 0);
        btnInstall.Click += BtnInstall_Click;
        flowBottom.Controls.Add(btnInstall);
        btnLaunch.Text = "启动游戏"; btnLaunch.Size = new Size(110, 34); btnLaunch.Margin = new Padding(0, 6, 8, 0);
        btnLaunch.Click += BtnLaunch_Click;
        flowBottom.Controls.Add(btnLaunch);
        chkForce.Text = "覆盖安装 BepInEx（用下方源重装，旧文件自动备份）";
        chkForce.AutoSize = true; chkForce.Margin = new Padding(10, 12, 0, 0);
        chkForce.CheckedChanged += (s, e) => UpdateBepInExState();
        flowBottom.Controls.Add(chkForce);
        flowBottom.Controls.Add(new Label
        {
            Text = "提示：界面语言跟随炉石客户端；改语言在游戏内 Mod Settings → Global → HsMod Language",
            AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(8, 16, 0, 0)
        });
        table.Controls.Add(flowBottom, 0, 6);
        table.SetColumnSpan(flowBottom, 3);

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Controls.Add(table);

        FormClosing += (s, e) => SaveSettings();

        // 优先读取配置文件（记住上次的选择），其次自动检测
        var detected = Installer.DetectGameDir();
        LoadSettings();
        if (string.IsNullOrWhiteSpace(txtGameDir.Text))
        {
            if (detected != null)
            {
                txtGameDir.Text = detected;
                Log($"已自动检测到游戏目录：{detected}");
            }
            else
            {
                Log("未自动检测到游戏目录，请手动选择。");
            }
        }
        txtGameDir.TextChanged += (s, e) => UpdateBepInExState();
        UpdateBepInExState();

        Log("请填写：游戏目录 + HsMod.dll 路径，然后点击 [开始配置]。");
        Log("若游戏目录没有 BepInEx，请再提供 BepInEx 的 zip 或解压目录。");
    }

    private void LoadSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "HsModInstaller.config.json");
            if (!File.Exists(path)) return;
            var cfg = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            if (cfg == null) return;
            if (!string.IsNullOrWhiteSpace(cfg.GameDir)) txtGameDir.Text = cfg.GameDir.Replace("\\\\", "\\");
            if (!string.IsNullOrWhiteSpace(cfg.HsModDll)) txtHsMod.Text = cfg.HsModDll;
            if (!string.IsNullOrWhiteSpace(cfg.BepInExSource)) txtBepInEx.Text = cfg.BepInExSource;
            if (!string.IsNullOrWhiteSpace(cfg.ProjectDir)) txtProject.Text = cfg.ProjectDir;
            Log("已加载配置文件：HsModInstaller.config.json（下次启动自动填充）");
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            var cfg = new AppSettings
            {
                GameDir = txtGameDir.Text.Trim().Replace("\\\\", "\\"),
                HsModDll = txtHsMod.Text.Trim(),
                BepInExSource = txtBepInEx.Text.Trim(),
                ProjectDir = txtProject.Text.Trim()
            };
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "HsModInstaller.config.json"), json);
        }
        catch { }
    }

    private Label MakeLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 12, 8, 0) };
    }

    private Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(2, 6, 2, 6)
        };
        b.Click += onClick;
        return b;
    }

    private void BrowseFile(TextBox tb, string title, string filter)
    {
        using var dlg = new OpenFileDialog { Title = title, Filter = filter };
        if (dlg.ShowDialog(this) == DialogResult.OK) tb.Text = dlg.FileName;
    }

    private void BrowseFolder(TextBox tb, string title)
    {
        using var dlg = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) tb.Text = dlg.SelectedPath;
    }

    private void AutoDetectGame()
    {
        var d = Installer.DetectGameDir();
        if (d != null)
        {
            txtGameDir.Text = d;
            Log($"自动检测到游戏目录：{d}");
        }
        else
        {
            Log("未自动检测到游戏目录，请手动选择。");
        }
    }

    private void UpdateBepInExState()
    {
        bool has = Installer.HasBepInEx(txtGameDir.Text.Trim());
        bool force = chkForce.Checked;
        if (has)
        {
            bool healthy = Installer.DescribeBepInEx(txtGameDir.Text.Trim()).StartsWith("OK");
            lblBepState.Text = healthy ? "OK 已检测到 BepInEx" : "! 已检测到 BepInEx，但有配置问题（详见日志）";
            lblBepState.ForeColor = healthy ? Color.Green : Color.Red;
        }
        else
        {
            lblBepState.Text = "! 未检测到 BepInEx，请填写 zip 或解压目录";
            lblBepState.ForeColor = Color.Red;
        }
        txtBepInEx.Enabled = !has || force;
        flow2.Enabled = !has || force;
    }

    private async void BtnInstall_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtGameDir.Text) || string.IsNullOrWhiteSpace(txtHsMod.Text))
        {
            Log("请先填写 游戏目录 和 HsMod.dll 路径。");
            return;
        }
        bool hasBep = Installer.HasBepInEx(txtGameDir.Text.Trim());
        if ((!hasBep || chkForce.Checked) && string.IsNullOrWhiteSpace(txtBepInEx.Text))
        {
            Log(hasBep
                ? "已勾选覆盖安装，请先填写 BepInEx 源（zip 压缩包或解压目录）。"
                : "游戏目录未检测到 BepInEx，请先填写 BepInEx 源（zip 压缩包或解压目录）。");
            return;
        }
        SetBusy(true);
        try
        {
            string? bep = string.IsNullOrWhiteSpace(txtBepInEx.Text) ? null : txtBepInEx.Text.Trim();
            string? proj = string.IsNullOrWhiteSpace(txtProject.Text) ? null : txtProject.Text.Trim();
            await Task.Run(() => Installer.Run(txtGameDir.Text.Trim(), txtHsMod.Text.Trim(), bep, proj, chkForce.Checked, Log));
        }
        catch (Exception ex)
        {
            Log("配置失败: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            UpdateBepInExState();
        }
    }

    private void BtnLaunch_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtGameDir.Text)) { Log("请先填写游戏目录。"); return; }
        string exe = Path.Combine(txtGameDir.Text.Trim(), "Hearthstone.exe");
        if (File.Exists(exe))
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe) });
            Log("已启动 Hearthstone.exe");
        }
        else
        {
            Log("未找到 " + exe);
        }
    }

    private void SetBusy(bool busy)
    {
        btnInstall.Enabled = !busy;
        btnLaunch.Enabled = !busy;
        txtGameDir.Enabled = !busy;
        txtHsMod.Enabled = !busy;
        txtBepInEx.Enabled = !busy;
        txtProject.Enabled = !busy;
    }

    private void Log(string msg)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(msg));
            return;
        }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        log.ScrollToCaret();
    }
}
