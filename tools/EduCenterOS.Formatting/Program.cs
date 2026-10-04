using EduCenterOS.Formatting;

if (args is ["--self-test"])
{
    FormatterTests.Run();
    return 0;
}

if (args.Length > 1 || args.Length == 1 && args[0] is not ("--check" or "--fix"))
{
    Console.Error.WriteLine("Usage: --check, --fix, or --self-test");
    return 2;
}

var fix = args is ["--fix"];

var root = new DirectoryInfo(AppContext.BaseDirectory);

while (root is not null && !File.Exists(Path.Combine(root.FullName, "EduCenterOS.sln"))) root = root.Parent;

if (root is null) throw new InvalidOperationException("Formatting.RepositoryRootMissing");

var changed = 0;

foreach (var folder in new[] { "src", "tests", "tools" })
{
    foreach (var path in Directory.EnumerateFiles(Path.Combine(root.FullName, folder), "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        if (path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "Migrations")) continue;

        var original = File.ReadAllText(path);
        var formatted = CodeFormatter.Format(original);

        if (original == formatted) continue;

        if (fix)
        {
            if (File.ReadAllText(path) != original) throw new InvalidOperationException("Formatting.ConcurrentEdit");

            File.WriteAllText(path, formatted);
        }

        Console.WriteLine($"{(fix ? "Formatted" : "Needs formatting")}: {Path.GetRelativePath(root.FullName, path)}");
        changed++;
    }
}

Console.WriteLine(changed == 0 ? "Formatting: passed." : $"Formatting: {changed} file(s) {(fix ? "updated" : "need --fix")}.");

return fix || changed == 0 ? 0 : 1;
