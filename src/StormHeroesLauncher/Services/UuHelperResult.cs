using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace StormHeroesLauncher.WindowSupport;
// One schema, compiled into the parent and helper. No arbitrary child output is part of this model.
public sealed record UuHelperResult(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Operation,
    [property: JsonRequired] bool Success,
    [property: JsonRequired] bool LauncherValidated,
    [property: JsonRequired] bool UuStarted,
    [property: JsonRequired] uint TargetPid,
    [property: JsonRequired] long TargetHwnd,
    [property: JsonRequired] string Class,
    [property: JsonRequired] bool WmClosePosted,
    [property: JsonRequired] bool? WindowVisibleAfter,
    [property: JsonRequired] bool ProcessRunningAfter,
    [property: JsonRequired] int FailureCode)
{
    public static UuHelperResult From(string operation,UuTrayReport report) => new(1,operation,report.ExitCode == 0,
        report.LauncherValidated,report.UUStarted,report.TargetPID,report.TargetHWND,report.Class,
        report.WM_CLOSE == "Posted",report.WindowVisibleAfter,report.ProcessRunningAfter,report.ExitCode);
    public UuTrayReport ToReport() => new(LauncherValidated,UuStarted,TargetPid,TargetHwnd,Class,
        WmClosePosted ? "Posted" : FailureCode == 23 ? "Failed" : "NotPosted",WindowVisibleAfter,ProcessRunningAfter,FailureCode);
}
[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(UuHelperResult))]
internal partial class UuHelperJsonContext : JsonSerializerContext { }
public sealed record UuParsedResult(bool Valid,UuTrayReport? Report,string Reason);
public static class UuHelperCodec
{
    public const int MaxBytes = 8192;
    public static byte[] Encode(UuHelperResult result) => JsonSerializer.SerializeToUtf8Bytes(result,UuHelperJsonContext.Default.UuHelperResult);
    public static UuParsedResult Parse(byte[] bytes,int exitCode,string operation,Action<string> log)
    {
        string schema = "Unknown", reason = "Unknown"; UuTrayReport? report = null;
        try
        {
            if(bytes.Length > MaxBytes) reason = "Oversized";
            else
            {
                // Explicitly accept UTF-8 BOM and surrounding whitespace; never log raw content.
                string text = new UTF8Encoding(false,true).GetString(bytes).Trim();
                if(text.Length > 0 && text[0] == '\uFEFF') text = text[1..].Trim();
                if(text.Length == 0) reason = "Empty";
                else
                {
                    var result = JsonSerializer.Deserialize(text,UuHelperJsonContext.Default.UuHelperResult);
                    if(result == null) reason = "NullResult";
                    else
                    {
                        schema = result.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if(result.SchemaVersion != 1) reason = "SchemaMismatch";
                        else if(result.Operation != operation) reason = "OperationMismatch";
                        else if(result.FailureCode != exitCode) reason = "ExitCodeMismatch";
                        else if(result.Success != (result.FailureCode == 0)) reason = "InconsistentSuccess";
                        else if(result.Success && (result.TargetPid == 0 || result.TargetHwnd <= 0 || result.Class != "UUMAINFORMV40" ||
                            !result.WmClosePosted || result.WindowVisibleAfter != false || !result.ProcessRunningAfter ||
                            (operation == "StartAndTray" && !result.LauncherValidated))) reason = "InvalidSuccessEvidence";
                        else { report = result.ToReport(); reason = "Valid"; }
                    }
                }
            }
        }
        catch(JsonException) { reason = "MalformedOrIncompleteSchema"; }
        catch(DecoderFallbackException) { reason = "InvalidUtf8"; }
        log($"HelperProcessExitCode={exitCode} ResultTransport=NamedPipe/SHUR-v1 ResultPresent={bytes.Length > 0} ResultLength={bytes.Length} ResultParse={(report != null ? "Success" : "Failed")} ResultSchemaVersion={schema} Reason={reason}");
        return new(report != null,report,reason);
    }
}
public sealed record UuResultFrame(byte[] Payload,string Status,int DeclaredLength);
public static class UuHelperTransport
{
    // Magic + little-endian length, followed by exactly one UTF-8 JSON result. Never wait for inherited-handle EOF.
    public static void Write(Stream stream,UuHelperResult result)
    {
        byte[] payload = UuHelperCodec.Encode(result);
        if(payload.Length > UuHelperCodec.MaxBytes) throw new InvalidDataException("Helper result exceeds limit");
        byte[] header = new byte[8]; "SHUR"u8.CopyTo(header); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4),payload.Length);
        stream.Write(header); stream.Write(payload); stream.Flush();
    }
    public static async Task<UuResultFrame> ReadAsync(Stream stream,CancellationToken token)
    {
        async Task<int> Fill(byte[] buffer)
        {
            int total = 0; while(total < buffer.Length) { int read = await stream.ReadAsync(buffer.AsMemory(total),token); if(read == 0) break; total += read; } return total;
        }
        byte[] header = new byte[8]; int received = await Fill(header);
        if(received != 8) return new([],received == 0 ? "Missing" : "TruncatedHeader",0);
        if(!header.AsSpan(0,4).SequenceEqual("SHUR"u8)) return new([],"BadMagic",0);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if(length <= 0 || length > UuHelperCodec.MaxBytes) return new([],"InvalidLength",length);
        byte[] payload = new byte[length]; received = await Fill(payload);
        return new(payload.AsSpan(0,received).ToArray(),received == length ? "Complete" : "TruncatedPayload",length);
    }
}
