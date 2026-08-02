namespace HsModBuilder;

public class MainForm : Form
{
    private readonly TextBox txtProject = new();
    private readonly TextBox txtOutput = new();
    private readonly RichTextBox log = new();
    private readonly Button btnCompile = new();
    private readonly Label lblResult = new();

    public MainForm()
    {
        Text = "HsMod 一键编译工具 v1.0";
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(840, 580);
        MinimumSize = new Size(740, 500);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 行0：源码目录
        table.Controls.Add(new Label
        {
            Text = "HsMod 源码目录:",
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0)
        }, 0, 0);
        txtProject.Dock = DockStyle.Fill; txtProject.Margin = new Padding(0, 4, 8, 4);
        table.Controls.Add(txtProject, 1, 0);
        var flowTop = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var btnBrowse = new Button { Text = "浏览...", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(2, 4, 2, 4) };
        btnBrowse.Click += (s, e) => BrowseFolder();
        flowTop.Controls.Add(btnBrowse);
        table.Controls.Add(flowTop, 2, 0);

        // 行1：输出目录（可选）
        table.Controls.Add(new Label
        {
            Text = "输出目录(可选):",
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0)
        }, 0, 1);
        txtOutput.Dock = DockStyle.Fill; txtOutput.Margin = new Padding(0, 4, 8, 4);
        table.Controls.Add(txtOutput, 1, 1);
        var flowOut = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var btnOut = new Button { Text = "浏览...", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(2, 4, 2, 4) };
        btnOut.Click += (s, e) => BrowseFolder(txtOutput);
        flowOut.Controls.Add(btnOut);
        table.Controls.Add(flowOut, 2, 1);

        // 行2：日志
        log.Dock = DockStyle.Fill; log.ReadOnly = true; log.BackColor = Color.White;
        log.Font = new Font("Consolas", 9F); log.Margin = new Padding(0, 6, 0, 6);
        table.Controls.Add(log, 0, 2);
        table.SetColumnSpan(log, 3);

        // 行2：底部按钮
        var flowBot = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        btnCompile.Text = "开始编译"; btnCompile.Size = new Size(140, 34); btnCompile.Margin = new Padding(0, 6, 8, 0);
        btnCompile.Click += BtnCompile_Click;
        flowBot.Controls.Add(btnCompile);
        lblResult.AutoSize = true; lblResult.Margin = new Padding(10, 14, 0, 0);
        flowBot.Controls.Add(lblResult);
        table.Controls.Add(flowBot, 0, 3);
        table.SetColumnSpan(flowBot, 3);

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(table);

        Log("HsMod 一键编译工具（内置编译器，无需安装 .NET SDK，任意电脑免环境）");
        Log("使用方法：选择 HsMod 源码目录（包含 HsMod.csproj 的文件夹），点击 [开始编译]。");
        Log("产物输出到：<源码目录>\\Release\\HsMod.dll");
        Log("编译完成后，用 HsModInstaller 工具把 HsMod.dll 配置进游戏即可。");
        Log("首次编译会解压内置的 .NET Framework 4.8 引用程序集（约 1-2 秒）。");
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "选择 HsMod 源码目录（包含 HsMod.csproj）", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        txtProject.Text = dlg.SelectedPath;
        if (Directory.GetFiles(dlg.SelectedPath, "*.csproj").Length > 0)
            Log("已选择项目：" + dlg.SelectedPath);
        else
            Log("提示：该目录没有找到 .csproj 文件，请确认选择的是源码目录。");
    }

    private void BrowseFolder(TextBox tb)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择输出目录（编译完成后自动复制 dll 到这里）", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) tb.Text = dlg.SelectedPath;
    }

    private async void BtnCompile_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtProject.Text))
        {
            Log("请先选择 HsMod 源码目录。");
            return;
        }
        btnCompile.Enabled = false;
        try
        {
            string dll = await Task.Run(() => HsCompiler.Compile(txtProject.Text.Trim(), Log));
            string finalDll = dll;
            string outDir = txtOutput.Text.Trim();
            if (outDir.Length > 0)
            {
                Directory.CreateDirectory(outDir);
                finalDll = Path.Combine(outDir, Path.GetFileName(dll));
                File.Copy(dll, finalDll, true);
                Log("已复制到目标目录：" + finalDll);
            }
            lblResult.Text = "完成： " + finalDll;
            lblResult.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            Log("错误：" + ex.Message);
            lblResult.Text = "编译失败";
            lblResult.ForeColor = Color.Red;
        }
        finally
        {
            btnCompile.Enabled = true;
        }
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
