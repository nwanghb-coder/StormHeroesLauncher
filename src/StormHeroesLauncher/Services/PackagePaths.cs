using System.IO;
namespace StormHeroesLauncher.Services;
public static class PackagePaths
{
    public static string WindowHelper=>HelperUnder(AppContext.BaseDirectory);
    public static string HelperUnder(string executableDirectory)
    {
        if(!Path.IsPathFullyQualified(executableDirectory))throw new ArgumentException("Executable base directory must be absolute");
        return Path.Combine(executableDirectory,"app","HOSLauncher.WindowHelper.exe");
    }
}
