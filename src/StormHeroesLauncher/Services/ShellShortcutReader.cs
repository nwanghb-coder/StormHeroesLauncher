using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace StormHeroesLauncher.Services;
public interface IShortcutReader { string Target(string path); }
public sealed class ShellShortcutReader : IShortcutReader
{
    public string Target(string path)
    {
        path=ShortcutImport.LocalPath(path);
        if(!Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || new FileInfo(path).Length>1024*1024) throw new InvalidDataException("Invalid shortcut");
        SafeCliPaths.NoReparse(path);
        object link=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
        try
        {
            ((IPersistFile)link).Load(path,0); // STGM_READ. Never Resolve, Save, ShellExecute, or read/execute Arguments.
            var target=new StringBuilder(32768);
            ((IShellLinkW)link).GetPath(target,target.Capacity,IntPtr.Zero,0);
            return ShortcutImport.LocalPath(target.ToString());
        }
        finally {Marshal.FinalReleaseComObject(link);}
    }
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,IntPtr findData,uint flags);
    }
}
