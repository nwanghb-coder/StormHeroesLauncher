using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace StormHeroesLauncher.WindowSupport;
public static class UuPipeSecurity
{
    public static string Name(string id) => Guid.TryParseExact(id,"N",out _) ? "StormHeroesLauncher.Uu."+id : throw new ArgumentException("Invalid report ID");
    public static PipeSecurity CreateAcl(SecurityIdentifier user)
    {
        var acl = new PipeSecurity();
        acl.SetAccessRuleProtection(true,false);
        acl.SetOwner(user);
        acl.AddAccessRule(new PipeAccessRule(user,PipeAccessRights.FullControl,AccessControlType.Allow));
        return acl;
    }
    private static SecurityIdentifier User()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new UnauthorizedAccessException("Missing user SID");
    }
    public static NamedPipeServerStream CreateServer(string id) => NamedPipeServerStreamAcl.Create(
        Name(id),PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.FirstPipeInstance,
        0,0,CreateAcl(User()),HandleInheritability.None);
    public static NamedPipeClientStream CreateClient(string id) => new(".",Name(id),
        PipeAccessRights.Write|PipeAccessRights.ReadPermissions,PipeOptions.None,
        TokenImpersonationLevel.Identification,HandleInheritability.None);
    public static void ValidateServer(NamedPipeClientStream pipe)
    {
        // Compare the account SID, not the token's default owner (which changes with elevation).
        if (!User().Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException("Pipe owner account mismatch");
    }
    public static void ValidateClient(NamedPipeServerStream pipe,int expectedPid)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint actual)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (actual != (uint)expectedPid) throw new UnauthorizedAccessException("Unexpected pipe client process");
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
}
