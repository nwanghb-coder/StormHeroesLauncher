using System.IO;
using System.Text;
using System.Text.Json;

namespace StormHeroesLauncher.Services;

public sealed class AppLogger
{
    private readonly object gate = new();
    public string LogPath { get; }
    public event Action<string>? MessageLogged;

    public AppLogger(string? logPath = null)
    {
        LogPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StormHeroesLauncher", "Logs", $"launcher-{DateTime.Now:yyyy-MM-dd}.log");
    }

    // Callers supply only allowlisted operation metadata, never raw CLI output.
    public void WriteOperation(string operation, object details) =>
        Write($"{operation}: {JsonSerializer.Serialize(details)}");

    public void Write(string message)
    {
        lock (gate)
        {
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                line += $"{Environment.NewLine}日志文件写入失败：{ex.Message}（本次日志仅显示在界面）";
            }
            MessageLogged?.Invoke(line);
        }
    }
}
