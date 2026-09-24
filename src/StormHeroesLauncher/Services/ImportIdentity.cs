using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
namespace StormHeroesLauncher.Services;
public sealed class ImportIdentity : IImportIdentity
{
    public bool Valid(string path,ImportKind kind)
    {
        try
        {
            path=ShortcutImport.LocalPath(path);if(!File.Exists(path))return false;SafeCliPaths.NoReparse(path);
            if(kind==ImportKind.Cli)return new WindowsCliValidation().Cli(path).Valid;
            string name=Path.GetFileName(path);
            if(kind==ImportKind.Uu)return (name.Equals("uu_launcher.exe",StringComparison.OrdinalIgnoreCase)||name.Equals("uu.exe",StringComparison.OrdinalIgnoreCase)) && new[]{"UU加速器","网易UU加速器","UU Game Booster","UU Accelerator"}.Contains(FileVersionInfo.GetVersionInfo(path).ProductName,StringComparer.OrdinalIgnoreCase) && new WindowsCliValidation().SourceExecutable(path).Valid;
            var info=FileVersionInfo.GetVersionInfo(path);
            if(kind==ImportKind.BattleNet && (!(name.Equals("Battle.net.exe",StringComparison.OrdinalIgnoreCase)||name.Equals("Battle.net Launcher.exe",StringComparison.OrdinalIgnoreCase)) || !(info.ProductName?.Contains("Battle.net",StringComparison.OrdinalIgnoreCase) ?? false)))return false;
            if(kind==ImportKind.Heroes && (!name.Equals("HeroesSwitcher_x64.exe",StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileName(Path.GetDirectoryName(path)),"Support64",StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))),"Heroes of the Storm",StringComparison.OrdinalIgnoreCase) || !(info.ProductName?.Contains("HeroesSwitcher",StringComparison.OrdinalIgnoreCase) ?? false)))return false;
            return BlizzardImportSignature.Verify(path)=="Valid";
        }
        catch {return false;}
    }
    public IEnumerable<string> HeroesMetadataRoots()
    {
        var roots=new List<string>();
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        try
        {
            using var basis=RegistryKey.OpenBaseKey(hive,view);
            using var uninstall=basis.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach(string key in (uninstall?.GetSubKeyNames() ?? []).Take(2048))
            {
                using var entry=uninstall!.OpenSubKey(key);string name=entry?.GetValue("DisplayName") as string ?? "";
                if((name.Contains("Heroes of the Storm",StringComparison.OrdinalIgnoreCase)||name.Contains("风暴英雄")) && entry?.GetValue("InstallLocation") is string root && roots.Count<32) roots.Add(root);
            }
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return roots;
    }
}
internal static class BlizzardImportSignature
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
            return certificate.GetNameInfo(X509NameType.SimpleName, false) == "Blizzard Entertainment, Inc." ? "Valid" : "PublisherRejected";
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

