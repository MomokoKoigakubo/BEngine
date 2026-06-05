// Resolves runtime asset paths independently of the current working directory: walks up from the
// binary location to find the repo's ModelsForTest folder. Lets `dotnet run` and IDE "F5" work no
// matter where they're launched from. (Global namespace so every file can use it without a using.)
static class AssetPaths
{
    static readonly string Root = FindRoot();

    public static string Model(string file) => Path.Combine(Root, "ModelsForTest", file);

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ModelsForTest")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
