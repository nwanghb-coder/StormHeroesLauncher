namespace StormHeroesLauncher;

public static class BuildFeatures
{
#if DEVELOPER_OBSERVER
    public const bool DeveloperObserver = true;
    public const string ObserverNotice = "\n\nDeveloper Observer Build：仅开发者使用。只读采集 UU / Battle.net 相关进程、文件版本及可见窗口类元数据；日志保存在 %LOCALAPPDATA%\\StormHeroesLauncher\\Observer。主流程退出后，独立观察进程最多继续 2 分钟，不占用启动互斥锁；观察总时长最多 15 分钟。不会读取命令行或窗口标题，不会自动上传。另有显式开发者测试模式，可直接启动游戏、隐藏准备窗口并仅清理本次测试进程；报告保存在 Diagnostics\\HeroesPrep。路径可能包含用户名，分享前请检查。";
#else
    public const bool DeveloperObserver = false;
    public const string ObserverNotice = "";
#endif
}
