using System.Reflection;
using StormHeroesLauncher;
using StormHeroesLauncher.Services;

public static class ObserverBuildTests
{
    public static Task Run(Action<bool, string> check)
    {
        var assembly = typeof(BuildFeatures).Assembly;
        bool compiled = assembly.GetTypes().Any(t => t.Namespace == "StormHeroesLauncher.Observer");
        bool startupCompiled = assembly.GetTypes().Any(t => t.Name == "UuStartupSampler");
        string? metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "DeveloperObserver").Value;
        string safety = AboutSafetyContent.Sections.Single(s => s.Heading == "安全与隐私").Text;
#if DEVELOPER_OBSERVER
        check(BuildFeatures.DeveloperObserver && compiled && metadata == "true", "developer property compiles observer and identifies assembly");
        check(startupCompiled, "UU startup observer is compiled into developer build only");
        check(safety.Contains("Developer Observer Build") && safety.Contains("15 分钟") && !safety.Contains("在启动流程结束后长期驻留后台"), "developer Safety discloses bounded observation and separate logs");
#else
        check(!BuildFeatures.DeveloperObserver && !compiled && metadata == "false", "normal assembly physically excludes all observer implementation types");
        check(!startupCompiled, "normal assembly contains zero UU startup observer types");
        check(!safety.Contains("Developer Observer Build") && safety.Contains("在启动流程结束后长期驻留后台"), "normal Safety remains observer-free");
#endif
        return Task.CompletedTask;
    }
}
