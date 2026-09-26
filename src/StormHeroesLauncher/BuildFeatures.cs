namespace StormHeroesLauncher;

public static class BuildFeatures
{
#if DEVELOPER_OBSERVER
    public const bool DeveloperObserver = true;
    public const string ObserverNotice = "\n\nDeveloper Observer Build：仅开发者使用。只读采集 UU / Battle.net 相关进程、文件版本及可见窗口类元数据；日志保存在 %LOCALAPPDATA%\\StormHeroesLauncher\\Observer。确认游戏运行后最多继续观察 2 分钟，观察总时长最多 15 分钟。不会读取命令行或窗口标题，不会自动上传。路径可能包含用户名，分享前请检查。";
#else
    public const bool DeveloperObserver = false;
    public const string ObserverNotice = "";
#endif
}
