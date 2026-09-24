param([Parameter(Mandatory=$true)][string]$Exe,[Parameter(Mandatory=$true)][string]$Ico)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class IconResourceCheck {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string p,IntPtr f,uint flags);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr m);
 delegate bool EnumName(IntPtr m,IntPtr t,IntPtr n,IntPtr p);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool EnumResourceNames(IntPtr m,IntPtr type,EnumName cb,IntPtr p);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr FindResource(IntPtr m,IntPtr name,IntPtr type);
 [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr m,IntPtr r);
 [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr m,IntPtr r);
 [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr r);
 static byte[] Read(IntPtr m,IntPtr name,int type) {
  var r=FindResource(m,name,new IntPtr(type)); if(r==IntPtr.Zero)throw new Exception("Resource missing");
  var bytes=new byte[SizeofResource(m,r)];Marshal.Copy(LockResource(LoadResource(m,r)),bytes,0,bytes.Length);return bytes;
 }
 public static byte[][] ReadImages(string path) {
  // DATAFILE | IMAGE_RESOURCE: map resources only; never execute the application entry point.
  var m=LoadLibraryEx(path,IntPtr.Zero,0x22);if(m==IntPtr.Zero)throw new Exception("Resource mapping failed");
  try {
   byte[] group=null;
   EnumResourceNames(m,new IntPtr(14),(a,t,n,p)=>{group=Read(a,n,14);return false;},IntPtr.Zero);
   if(group==null)throw new Exception("No group icon");
   int count=BitConverter.ToUInt16(group,4);var images=new byte[count][];
   for(int i=0;i<count;i++)images[i]=Read(m,new IntPtr(BitConverter.ToUInt16(group,6+i*14+12)),3);
   return images;
  } finally {FreeLibrary(m);}
 }
}
"@
$bytes=[IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Ico))
$count=[BitConverter]::ToUInt16($bytes,4)
$images=[IconResourceCheck]::ReadImages((Resolve-Path -LiteralPath $Exe))
if ($count -ne 6 -or $images.Length -ne $count) { throw 'Wrong ICO/PE frame count' }
$sizes=@()
for($i=0;$i -lt $count;$i++) {
 $entry=6+16*$i
 $width=[int]$bytes[$entry]; if($width -eq 0){$width=256};$sizes+=$width
 $length=[BitConverter]::ToInt32($bytes,$entry+8);$offset=[BitConverter]::ToInt32($bytes,$entry+12)
 $frame=[byte[]]$bytes[$offset..($offset+$length-1)]
 $a=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($frame))
 $b=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($images[$i]))
 if($a -ne $b){throw 'Executable icon frame differs from source ICO'}
}
if(($sizes -join ',') -ne '16,24,32,48,64,256'){throw 'Unexpected sizes'}
Write-Output ('PASS: ICO sizes '+($sizes -join ', ')+'; all six PE RT_ICON resources match source bytes. EXE mapped as data only.')
