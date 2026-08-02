using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace HsModBuilder;

/// <summary>旧式 .NET Framework csproj 编译引擎（内置 Roslyn 编译器 + v4.8 引用程序集，无需外部环境）</summary>
public static class HsCompiler
{
    private const string ResPrefix = @"ref48\";

    /// <summary>把内嵌的 v4.8 引用程序集解压到临时目录（只做一次，之后复用）</summary>
    public static string EnsureRefAssemblies(Action<string>? log = null)
    {
        // 1. 优先使用 exe 同目录的外置 Frameworks48 文件夹（多文件发布，免解压）
        string external = Path.Combine(AppContext.BaseDirectory, "Frameworks", "v48");
        if (File.Exists(Path.Combine(external, "mscorlib.dll")))
        {
            log?.Invoke($"使用外置引用程序集 → {external}");
            return external;
        }

        // 2. 回退：解压内嵌资源（兼容单文件模式）
        string dest = Path.Combine(Path.GetTempPath(), "HsModBuilderRef48");
        string sub = Path.Combine(dest, "v48");
        if (File.Exists(Path.Combine(dest, "mscorlib.dll"))) return dest;
        if (File.Exists(Path.Combine(sub, "mscorlib.dll"))) return sub;

        Directory.CreateDirectory(dest);
        var asm = Assembly.GetAssembly(typeof(HsCompiler))!;
        int n = 0;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResPrefix)) continue;
            string rel = name.Substring(ResPrefix.Length);
            string target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var rs = asm.GetManifestResourceStream(name)!;
            using var fs = File.Create(target);
            rs.CopyTo(fs);
            n++;
        }
        log?.Invoke($"已就绪 .NET Framework 4.8 引用程序集（{n} 个文件）→ {dest}");
        return File.Exists(Path.Combine(sub, "mscorlib.dll")) ? sub : dest;
    }

    /// <summary>编译项目，产物输出到项目的 Release 目录，返回 dll 完整路径</summary>
    public static string Compile(string projectDir, Action<string> log)
    {
        projectDir = Path.GetFullPath(projectDir);
        if (!Directory.Exists(projectDir))
            throw new Exception("目录不存在：" + projectDir);
        string csproj = Directory.GetFiles(projectDir, "*.csproj").FirstOrDefault()
            ?? throw new Exception("未找到 .csproj 文件（请选择 HsMod 源码目录）");

        log("解析 " + Path.GetFileName(csproj) + " ...");
        var doc = XDocument.Load(csproj);
        var root = doc.Root!;
        XNamespace ns = root.Name.Namespace;
        bool isSdkStyle = root.Attribute("Sdk") != null;

        // ---- 收集 PropertyGroup（合并无条件组 + Release 条件组）----
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pg in root.Elements(ns + "PropertyGroup"))
        {
            if (!ConditionMatch(pg, "Release")) continue;
            foreach (var e in pg.Elements())
                props[e.Name.LocalName] = e.Value.Trim();
        }
        string assemblyName = props.TryGetValue("AssemblyName", out var an) && an.Length > 0
            ? an : Path.GetFileNameWithoutExtension(csproj);
        string outputPath = props.TryGetValue("OutputPath", out var op) && op.Length > 0 ? op : @"Release\";
        string defineConstants = props.TryGetValue("DefineConstants", out var dc) ? dc : "";
        string langVersion = props.TryGetValue("LangVersion", out var lv) ? lv : "latest";
        bool optimize = props.TryGetValue("Optimize", out var oz) ? oz == "true" : true;

        // ---- 收集源文件 ----
        var compileFiles = new List<string>();
        if (isSdkStyle)
        {
            compileFiles.AddRange(Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(@"\obj\" + Path.DirectorySeparatorChar)
                         && !f.Contains(@"\bin\" + Path.DirectorySeparatorChar)));
        }
        else
        {
            foreach (var c in root.Descendants(ns + "Compile"))
            {
                var inc = c.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(inc)) continue;
                if (!ConditionMatch(c, "Release")) continue;
                if (inc.Contains('*') || inc.Contains('?'))
                {
                    AddGlobFiles(projectDir, inc, c.Attribute("Exclude")?.Value, compileFiles);
                }
                else
                {
                    string p = Path.Combine(projectDir, inc);
                    if (File.Exists(p)) compileFiles.Add(p);
                }
            }
        }
        compileFiles = compileFiles.Distinct().ToList();
        log($"  源文件 {compileFiles.Count} 个");

        // ---- 收集嵌入资源 ----
        var embeddedRes = new List<(string Path, string LogicalName)>();
        string rootNs = props.TryGetValue("RootNamespace", out var rn) ? rn : assemblyName;
        foreach (var er in root.Descendants(ns + "EmbeddedResource"))
        {
            var inc = er.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(inc)) continue;
            if (!ConditionMatch(er, "Release")) continue;
            string p = Path.Combine(projectDir, inc);
            if (!File.Exists(p)) continue;
            string logical = er.Attribute("LogicalName")?.Value
                ?? rootNs + "." + inc.Replace('\\', '.').Replace('/', '.');
            embeddedRes.Add((p, logical));
        }
        log($"  嵌入资源 {embeddedRes.Count} 个");

        // ---- 收集引用 ----
        var localRefs = new List<(string Name, string? Hint)>();
        foreach (var r in root.Descendants(ns + "Reference"))
        {
            var inc = r.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(inc)) continue;
            if (!ConditionMatch(r, "Release")) continue;
            var hint = r.Element(ns + "HintPath")?.Value;
            localRefs.Add((inc, hint));
        }
        log($"  引用 {localRefs.Count} 个");
        log("  AssemblyName=" + assemblyName + " OutputPath=" + outputPath + " Optimize=" + optimize);

        // ---- 构建引用集合（v4.8 BCL + 本地 HintPath）----
        string refDir = EnsureRefAssemblies(log);
        var refs = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.GetFiles(refDir, "*.dll"))
        {
            string fn = Path.GetFileName(dll);
            if (fn.StartsWith("System.EnterpriseServices", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsManagedAssembly(dll)) { log("  跳过非托管文件: " + fn); continue; }
            refs.TryAdd(fn, MetadataReference.CreateFromFile(dll));
        }
        string facades = Path.Combine(refDir, "Facades");
        if (Directory.Exists(facades))
            foreach (var dll in Directory.GetFiles(facades, "*.dll"))
            {
                if (!IsManagedAssembly(dll)) continue;
                refs.TryAdd(Path.GetFileName(dll), MetadataReference.CreateFromFile(dll));
            }

        int localOk = 0;
        foreach (var (name, hint) in localRefs)
        {
            if (string.IsNullOrEmpty(hint)) continue;
            string p = Path.Combine(projectDir, hint);
            if (File.Exists(p))
            {
                refs[Path.GetFileName(p)] = MetadataReference.CreateFromFile(p);
                localOk++;
            }
        }
        foreach (var (name, hint) in localRefs)
        {
            if (!string.IsNullOrEmpty(hint)) continue;
            string simple = name.Split(',')[0] + ".dll";
            if (!refs.ContainsKey(simple))
            {
                var found = Directory.GetFiles(refDir, simple, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) refs[simple] = MetadataReference.CreateFromFile(found);
            }
        }
        log($"  本地引用命中 {localOk} 个");

        // ---- Roslyn 编译 ----
        log("开始编译 ...");
        var preproc = defineConstants.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preproc);
        var trees = compileFiles.Select(f =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(f), parseOptions)).ToList();
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: optimize ? OptimizationLevel.Release : OptimizationLevel.Debug,
            allowUnsafe: true,
            deterministic: true);
        var compilation = CSharpCompilation.Create(assemblyName, trees, refs.Values, options);

        var resources = embeddedRes.Select(r =>
            new ResourceDescription(r.LogicalName, () => File.OpenRead(r.Path), isPublic: true)).ToList();

        string outDir = Path.Combine(projectDir, outputPath);
        Directory.CreateDirectory(outDir);
        string outDll = Path.Combine(outDir, assemblyName + ".dll");
        using (var pe = File.Create(outDll))
        using (var pdb = File.Create(Path.Combine(outDir, assemblyName + ".pdb")))
        {
            EmitResult result = compilation.Emit(pe, pdb, manifestResources: resources);
            if (!result.Success)
            {
                var errs = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(20).ToList();
                throw new Exception("编译失败：" + string.Join("\n", errs.Select(d => "  " + d.ToString())));
            }
        }
        log("编译成功！产物：" + outDll);
        log("提示：用 HsModInstaller 工具把这个 dll 配置进游戏即可。");
        return outDll;
    }

    /// <summary>展开 csproj 通配符 Include（支持 *、? 和 ** 递归），应用 Exclude</summary>
    private static void AddGlobFiles(string projectDir, string include, string? exclude, List<string> files)
    {
        bool recursive = include.Contains("**");
        string norm = include.Replace("**", "*");
        string full = Path.Combine(projectDir, norm);
        string dir = Path.GetDirectoryName(full) ?? projectDir;
        string pattern = Path.GetFileName(full);
        var sep = Path.DirectorySeparatorChar;
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                if (f.Contains(sep + "obj" + sep) || f.Contains(sep + "bin" + sep)) continue;
                files.Add(f);
            }
        }
        if (!string.IsNullOrEmpty(exclude))
        {
            string ef = Path.Combine(projectDir, exclude.Replace("**", "*"));
            string edir = Path.GetDirectoryName(ef) ?? projectDir;
            string epat = Path.GetFileName(ef);
            var excl = Directory.Exists(edir)
                ? Directory.GetFiles(edir, epat, SearchOption.TopDirectoryOnly)
                : new[] { ef };
            foreach (var e in excl) files.Remove(e);
        }
    }

    /// <summary>通过 PE 头判断是否为托管程序集（跳过 System.EnterpriseServices.Thunk/Wrapper 等原生文件）</summary>
    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (fs.Length < 0x40) return false;
            fs.Seek(0x3C, SeekOrigin.Begin);
            int pe = br.ReadInt32();
            if (pe <= 0 || pe + 24 + 128 > fs.Length) return false;
            fs.Seek(pe + 24, SeekOrigin.Begin);
            ushort magic = br.ReadUInt16();
            int ddOffset = pe + 24 + (magic == 0x20B ? 112 : 96);
            fs.Seek(ddOffset + 14 * 8, SeekOrigin.Begin);
            uint clrRva = br.ReadUInt32();
            return clrRva != 0;
        }
        catch { return false; }
    }

    /// <summary>解析 $(Configuration) 条件：提取 == 后的配置名（忽略 Platform 部分），与当前配置比较</summary>
    private static bool ConditionMatch(XElement el, string config)
    {
        var cond = el.Attribute("Condition")?.Value;
        if (string.IsNullOrEmpty(cond)) return true;
        bool negate = cond.Contains("!=");
        string op = negate ? "!=" : "==";
        int idx = cond.IndexOf(op, StringComparison.Ordinal);
        if (idx < 0) return true;
        string after = cond.Substring(idx + op.Length).Trim();
        string expected = after.TrimStart('\'').Split('\'', '|')[0].Trim();
        bool result = string.Equals(expected, config, StringComparison.OrdinalIgnoreCase);
        return negate ? !result : result;
    }
}
