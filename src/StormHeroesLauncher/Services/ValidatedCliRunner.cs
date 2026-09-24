using System.IO;
namespace StormHeroesLauncher.Services;
// Guards execution without changing CLI arguments, protocol or timeout behavior.
public sealed class ValidatedCliRunner(ICliValidation validation, ICliProcessRunner inner) : ICliProcessRunner
{
    public Task<CliProcessOutput> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!validation.Cli(path).Valid) throw new InvalidDataException("网易 UU CLI 签名/产品验证失败，已拒绝执行。");
        return await inner.RunAsync(path, arguments, timeout, token);
    }, token);
}
