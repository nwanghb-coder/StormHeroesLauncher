param([Parameter(Mandatory=$true)][string]$Package, [ValidateSet('Normal','Observer')][string]$BuildFlavor)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $Package).Path
$files=@(Get-ChildItem -LiteralPath $root -Recurse -File)
$expected=@('HOSLauncher.exe','app\HOSLauncher.WindowHelper.exe')
$actual=@($files | ForEach-Object { [IO.Path]::GetRelativePath($root,$_.FullName) })
if(@(Compare-Object ($expected|Sort-Object) ($actual|Sort-Object)).Count){throw 'Unexpected portable layout'}
$sdk=Join-Path (Split-Path (Get-Command dotnet).Source) ('sdk\'+(& dotnet --version)+'\Microsoft.NET.HostModel.dll')
$asm=[Reflection.Assembly]::LoadFrom($sdk)
$isBundle=$asm.GetType('Microsoft.NET.HostModel.Bundle.Bundler').GetMethod('IsBundle',[Reflection.BindingFlags]'Static,NonPublic,Public')
foreach($file in $files){
 if($file.VersionInfo.ProductName -ne 'HOSLauncher'){throw ('Unexpected product metadata: '+$file.Name)}
 $arguments=[object[]]@($file.FullName,[long]0)
 if(!$isBundle.Invoke($null,$arguments)){throw 'Not a .NET single-file bundle'}
 $stream=[IO.File]::OpenRead($file.FullName);$reader=[IO.BinaryReader]::new($stream)
 try {
  $stream.Position=[long]$arguments[1]
  $major=$reader.ReadUInt32();$minor=$reader.ReadUInt32();$count=$reader.ReadInt32();$id=$reader.ReadString()
  if($major -ne 6){throw 'Unsupported bundle format; inspect SDK format before adapting verifier'}
  $depsOffset=$reader.ReadInt64();$depsSize=$reader.ReadInt64();$configOffset=$reader.ReadInt64();$configSize=$reader.ReadInt64();$flags=$reader.ReadUInt64()
  $names=@();$mainOffset=0;$mainSize=0
  for($i=0;$i -lt $count;$i++){
   $offset=$reader.ReadInt64();$size=$reader.ReadInt64();$compressed=$reader.ReadInt64();$type=$reader.ReadByte();$name=$reader.ReadString()
   if($compressed -ne 0){throw 'Unexpected bundle compression'}
   $names+=$name
   if($name -eq 'HOSLauncher.dll'){$mainOffset=$offset;$mainSize=$size}
  }
  foreach($required in @('System.Private.CoreLib.dll')){if($required -notin $names){throw ('Missing bundled runtime: '+$required)}}
  if($file.Name -eq 'HOSLauncher.exe' -and 'PresentationFramework.dll' -notin $names){throw 'WPF runtime not bundled'}
  if($names | Where-Object {$_ -match '\.pdb$|\.cs$|OfflineTests|(^|/)Diagnostics/|(^|/)uu-cli\.exe$|(^|/)settings\.json$'}){throw 'Development or user/CLI files in bundle'}
  if($BuildFlavor -and $file.Name -eq 'HOSLauncher.exe'){
   if(!$mainSize){throw 'Main managed assembly missing'}
   $stream.Position=$mainOffset
   $managed=[IO.MemoryStream]::new($reader.ReadBytes([int]$mainSize))
   $peMetadata=[System.Reflection.PortableExecutable.PEReader]::new($managed)
   try {
    $metadata=[System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peMetadata)
    $observerTypes=0
    foreach($handle in $metadata.TypeDefinitions){
     $type=$metadata.GetTypeDefinition($handle)
     if($metadata.GetString($type.Namespace) -eq 'StormHeroesLauncher.Observer'){$observerTypes++}
    }
    if(($observerTypes -gt 0) -ne ($BuildFlavor -eq 'Observer')){throw 'Packaged observer isolation mismatch'}
    Write-Output ('PASS: '+$BuildFlavor+' packaged managed assembly; ObserverTypes='+$observerTypes)
   } finally {$peMetadata.Dispose();$managed.Dispose()}
  }
  $stream.Position=$configOffset;$config=[Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$configSize))|ConvertFrom-Json
  if(!$config.runtimeOptions.includedFrameworks -or $config.runtimeOptions.framework -or $config.runtimeOptions.frameworks){throw 'Runtime configuration is not self-contained'}
  # Modern .NET singlefilehost statically links CoreCLR/JIT/hostpolicy rather than listing these as bundle files.
  $runtimeVersion=($config.runtimeOptions.includedFrameworks | Where-Object name -EQ 'Microsoft.NETCore.App').version
  $hostPath=Join-Path (Split-Path (Get-Command dotnet).Source) ("packs\Microsoft.NETCore.App.Host.win-x64\"+$runtimeVersion+"\runtimes\win-x64\native\singlefilehost.exe")
  function TextHash([string]$path) {
   $f=[IO.File]::OpenRead($path);$peReader=[System.Reflection.PortableExecutable.PEReader]::new($f)
   try { $section=$peReader.PEHeaders.SectionHeaders | Where-Object Name -EQ '.text';$f.Position=$section.PointerToRawData;$r=[IO.BinaryReader]::new($f);return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($r.ReadBytes($section.SizeOfRawData))) }
   finally {$peReader.Dispose();$f.Dispose()}
  }
  if((TextHash $file.FullName) -ne (TextHash $hostPath)){throw 'Native code does not match the Microsoft static-runtime singlefilehost'}
  $stream.Position=0x3c;$pe=$reader.ReadInt32();$stream.Position=$pe+4
  if($reader.ReadUInt16() -ne 0x8664){throw 'Not x64 PE'}
  Write-Output ('PASS: '+$file.Name+' x64 self-contained bundle; entries='+$count+'; runtimes='+($config.runtimeOptions.includedFrameworks.name -join ','))
 } finally {$reader.Dispose();$stream.Dispose()}
}
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Text;
public static class PortablePathScan {
 public static bool HasDeveloperPath(string path) {
  byte[][] patterns={Encoding.UTF8.GetBytes(@"E:\CodexProjects\"),Encoding.Unicode.GetBytes(@"E:\CodexProjects\"),Encoding.UTF8.GetBytes(@"C:\Users\nwang\"),Encoding.Unicode.GetBytes(@"C:\Users\nwang\")};
  using var stream=File.OpenRead(path);byte[] buffer=new byte[1048576+128];int carry=0,read;
  while((read=stream.Read(buffer,carry,1048576))>0){int total=read+carry;foreach(var p in patterns)if(buffer.AsSpan(0,total).IndexOf(p)>=0)return true;carry=Math.Min(128,total);Buffer.BlockCopy(buffer,total-carry,buffer,0,carry);}
  return false;
 }
}
"@
foreach($file in $files){if([PortablePathScan]::HasDeveloperPath($file.FullName)){throw ('Developer absolute path found in '+$file.Name)}}
Write-Output 'PASS: exact two-EXE layout; no PDB/test/source/CLI artifacts; no configured developer-path byte strings.'
$main=(Get-Item (Join-Path $root 'HOSLauncher.exe')).Length
$app=($files | Where-Object FullName -NE (Join-Path $root 'HOSLauncher.exe') | Measure-Object Length -Sum).Sum
Write-Output ('MainBytes='+$main+' AppBytes='+$app+' TotalBytes='+($main+$app))
