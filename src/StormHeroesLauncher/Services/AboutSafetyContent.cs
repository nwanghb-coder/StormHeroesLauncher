using System.Diagnostics;
using System.IO;
using System.Reflection;
namespace StormHeroesLauncher.Services;
public static class AboutSafetyContent
{
    public const string Publisher="阿黄";
    public const string Email="441649289@qq.com";
    private static Assembly AppAssembly=>typeof(AboutSafetyContent).Assembly;
    public static string Version=>AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? AppAssembly.GetName().Version?.ToString() ?? "Unknown";
    public static string BuildId=>(AppAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a=>a.Key=="BuildId")?.Value ?? "Unknown")+"-"+AppAssembly.ManifestModule.ModuleVersionId.ToString("N")[..12];
    public static string SupportInfo=>$"StormHeroesLauncher\nVersion: {Version}\nBuild ID: {BuildId}\nArchitecture: x64\nWindows: {Environment.OSVersion.Version}";
    public static string LogsDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StormHeroesLauncher","Logs");
    public static string FeedbackUri=>"mailto:"+Email+"?subject="+Uri.EscapeDataString("StormHeroesLauncher Feedback - "+Version);
    public static IReadOnlyList<(string Heading,string Text)> Sections {get;}=Array.AsReadOnly(new[]{
        ("关于","StormHeroesLauncher\n《风暴英雄》快速启动工具\n\n用于自动完成网易 UU 加速、Battle.net 启动及《风暴英雄》启动流程，减少重复操作。\n\n制作者 / 发布者：阿黄\n开发协助：本启动器在 ChatGPT（含 Codex）协助下开发。"),
        ("主要功能","• 自动启动网易 UU 加速器\n• 自动调用 UU 官方 CLI 为《风暴英雄》启动加速\n• 自动发现并准备 UU CLI 组件\n• 自动启动 Battle.net 和《风暴英雄》\n• 按用户已配置的关闭行为，将 UU 与 Battle.net 收至系统托盘\n• 每次冷启动流程最多请求一次 Windows UAC 确认\n• 支持拖入一个或两个网易 UU / 暴雪游戏平台快捷方式进行自动配置\n• 自动保存本地配置\n• 提供本地运行日志和故障诊断信息"),
        ("安全与隐私","StormHeroesLauncher 的自动化仅限于正常的 Windows 程序启动、窗口管理以及已确认的本地组件调用，不介入《风暴英雄》的游戏运行逻辑。\n\n本程序不会：\n• 主动收集或读取网易 UU / Battle.net 的账号、密码、登录凭据、Cookie、Token 等认证数据\n• 向《风暴英雄》注入 DLL 或使用 Hook（钩子）\n• 读取或修改游戏进程内存\n• 修改《风暴英雄》或 Battle.net 程序文件\n• 抓取、拦截或篡改游戏网络数据包\n• 模拟游戏内键盘或鼠标输入\n• 绕过 Windows UAC\n• 创建常驻高权限服务或隐藏的计划任务\n• 在启动流程结束后长期驻留后台\n\n本程序会：\n• 启动本机已安装的网易 UU、Battle.net 和《风暴英雄》\n• 使用用户本机网易 UU 安装中提供的官方 CLI\n• 使用 Windows 标准 Win32 API 管理相关程序窗口\n• 在 UU 启动或窗口管理确有需要时，通过标准 Windows UAC 请求窄功能辅助程序的管理员权限\n• 在本机保存配置、CLI 缓存和日志\n\n本地数据位置：%LOCALAPPDATA%\\StormHeroesLauncher\n\n日志不主动采集账号密码，但会记录本地路径、配置和诊断信息；自行加入配置文件的额外内容也可能被记录。请勿将凭据写入配置，分享日志前请检查并移除私人信息。上述说明是实现边界，不是安全、反作弊兼容性或账号风险保证。"),
        ("技术实现","• C# / .NET / WPF（Windows 桌面 UI 框架）\n• SharpCompress\n• 随程序附带 .NET 运行库，无需另行安装；运行时可能解压原生组件到 Windows 临时缓存\n• 网易 UU 加速使用用户本机 UU 安装中提供的官方 CLI\n• 游戏通过 Battle.net / HeroesSwitcher 本地启动链启动\n• 窗口处理使用 Windows 标准 Win32 API（Windows 原生接口）\n• 主程序以普通权限运行\n• 仅 UU 所需的窄功能辅助程序在必要时通过标准 UAC 提权"),
        ("反馈与诊断","遇到问题时，可复制版本信息并描述操作步骤、预期结果和实际结果。日志仅保存在本机；本页面不会自动附加文件、上传日志或发送邮件。反馈按钮只请求默认邮件客户端打开邮件草稿。"),
        ("独立第三方工具声明","StormHeroesLauncher 为独立制作的第三方工具，与 Blizzard Entertainment（暴雪娱乐）、NetEase（网易）及 OpenAI 不存在官方隶属、授权或背书关系。")
    }.Select(section => section.Item1 == "安全与隐私" && BuildFeatures.DeveloperObserver
        ? (section.Item1, section.Item2.Replace("在启动流程结束后长期驻留后台", "在启动流程结束后无限期驻留后台") + BuildFeatures.ObserverNotice)
        : section).ToArray());
}
public interface IAboutActions
{
    void Copy(string text);
    void OpenLogs();
    void Feedback();
}
public sealed class WindowsAboutActions:IAboutActions
{
    public void Copy(string text)=>System.Windows.Clipboard.SetText(text);
    public void OpenLogs()
    {
        string path=AboutSafetyContent.LogsDirectory;
        SafeCliPaths.NoReparse(path);Directory.CreateDirectory(path);SafeCliPaths.NoReparse(path);
        using var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    }
    public void Feedback()
    {
        using var process=Process.Start(new ProcessStartInfo(AboutSafetyContent.FeedbackUri){UseShellExecute=true});
    }
}
