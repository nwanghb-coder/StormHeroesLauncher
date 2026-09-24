using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
namespace StormHeroesLauncher.Services;
public sealed record CliValidation(bool Valid, string Reason, Version? Version = null);
public interface ICliValidation
{
    CliValidation Cli(string path);
    CliValidation SourceExecutable(string path);
}
public sealed class WindowsCliValidation : ICliValidation
{
    public CliValidation Cli(string path) => Check(path, true);
    public CliValidation SourceExecutable(string path) => Check(path, false);
    private static CliValidation Check(string path, bool cli)
    {
        try
        {
            if (!File.Exists(path) || !Path.IsPathFullyQualified(path)) return new(false, "Missing");
            if (cli && !Path.GetFileName(path).Equals("uu-cli.exe", StringComparison.OrdinalIgnoreCase)) return new(false, "WrongFileName");
            SafeCliPaths.NoReparse(path);
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var pe = new PEReader(held, PEStreamOptions.LeaveOpen);
            if (pe.PEHeaders.PEHeader == null || (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.ExecutableImage) == 0 ||
                (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0 ||
                pe.PEHeaders.CoffHeader.Machine is not (Machine.Amd64 or Machine.I386)) return new(false, "NotWindowsExecutable");
            var signature = Authenticode.Verify(path);
            if (signature != "Valid") return new(false, signature);
            var info = FileVersionInfo.GetVersionInfo(path);
            if (cli && info.ProductName?.Trim() != "UU CLI") return new(false, "WrongProduct");
            return new(true, "Valid", new Version(Math.Max(0, info.FileMajorPart), Math.Max(0, info.FileMinorPart), Math.Max(0, info.FileBuildPart), Math.Max(0, info.FilePrivatePart)));
        }
        catch (Exception ex) { return new(false, "ValidationUnavailable_" + ex.GetType().Name); }
    }
}
internal static class Authenticode
{
    public static string Verify(string path)
    {
        var file = new FileInfoNative { Size = (uint)Marshal.SizeOf<FileInfoNative>(), Path = path };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
        Marshal.StructureToPtr(file, memory, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UI = 2, Revocation = 1, Choice = 1, File = memory, StateAction = 1, Flags = 0x1000 | 0x80 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result != 0) return $"SignatureInvalidOrUnavailable_0x{result:X8}";
            IntPtr provider = WTHelperProvDataFromStateData(data.State);
            IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            IntPtr cert = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
            if (cert == IntPtr.Zero) return "SignerUnavailable";
            var entry = Marshal.PtrToStructure<ProviderCertificate>(cert);
            using var certificate = new X509Certificate2(entry.Certificate);
            // Exact verified signer identity; permits certificate renewal and new file versions, not arbitrary 'NetEase' substrings.
            return certificate.GetNameInfo(X509NameType.SimpleName, false) == "NetEase (Hangzhou) Network Co., Ltd" ? "Valid" : "PublisherRejected";
        }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfoNative>(memory); Marshal.FreeHGlobal(memory);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileInfoNative { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct ProviderCertificate { public uint Size; public IntPtr Certificate; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData { public uint Size; public IntPtr Policy, Sip; public uint UI, Revocation, Choice; public IntPtr File; public uint StateAction; public IntPtr State, Url; public uint Flags, Context; public IntPtr SignatureSettings; }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counter, uint counterIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint index);
}
public static class SafeCliPaths
{
    public static void NoReparse(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("拒绝重解析点路径。");
    }
    public static string Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("非法压缩包路径。");
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => p is ".." or "." || p.EndsWith(' ') || p.EndsWith('.'))) throw new InvalidDataException("拒绝路径穿越。");
        string basis = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string output = Path.GetFullPath(Path.Combine(basis, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!output.StartsWith(basis, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("输出超出缓存目录。");
        NoReparse(output); return output;
    }
}
