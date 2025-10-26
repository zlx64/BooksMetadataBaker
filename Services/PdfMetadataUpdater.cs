using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using System.Diagnostics;
using System.Text.Json;

namespace PrepKavitaPdf.Services;

public record PdfMetadataUpdateResult(string FilePath, bool Success, string? ErrorMessage, IDictionary<string,string> AppliedMetadata, int Attempts, bool GhostscriptRan, bool MetadataApplied);

public class PdfMetadataUpdater
{
    private readonly bool _sidecarEnabled;
    private readonly bool _gsEnabled;
    private readonly string _gsPathCfg;

    public PdfMetadataUpdater(IConfiguration config)
    {
        _sidecarEnabled = bool.TryParse(config["Tools:SidecarMetadataEnabled"], out var sc) ? sc : true;
        _gsEnabled = bool.TryParse(config["Tools:GhostscriptEnabled"], out var gse) ? gse : true;
        _gsPathCfg = config["Tools:GhostscriptPath"] ?? "gs";
    }

    public Task<PdfMetadataUpdateResult> UpdateAsync(string filePath, IDictionary<string,string> metadata, string fallbackTitle, CancellationToken ct)
        => Task.Run(() => Process(filePath, metadata, fallbackTitle, ct), ct);

    private PdfMetadataUpdateResult Process(string filePath, IDictionary<string,string> metadata, string fallbackTitle, CancellationToken ct)
    {
        var applied = new Dictionary<string,string>(metadata);
        int attempts = 0; bool gsRan=false; bool metaApplied=false; string? errors=null;
        if (ct.IsCancellationRequested) return new(filePath,false,"Cancelled",applied,attempts,false,false);

        // 1. Direct metadata attempt with PdfSharp
        attempts++;
        if (TryWriteMetadataInPlace(filePath, metadata, fallbackTitle, out var directErr))
        {
            metaApplied = true;
            return Finalize(filePath, applied, attempts, gsRan, metaApplied, errors, filePath);
        }
        errors = Combine(errors, "Direct metadata failed: "+directErr);

        if (!_gsEnabled)
        {
            return Finalize(filePath, applied, attempts, gsRan, metaApplied, errors, filePath);
        }

        // 2. Ghostscript repair then metadata
        var workDir = Path.Combine(Path.GetTempPath(), "pdf_gs_cond");
        Directory.CreateDirectory(workDir);
        string orig = Path.Combine(workDir, Guid.NewGuid()+".orig.pdf");
        string repaired = Path.Combine(workDir, Guid.NewGuid()+".repaired.pdf");
        try
        {
            attempts++; System.IO.File.Copy(filePath, orig, true);
            attempts++;
            if (RunGhostscriptRepair(orig, repaired, out var gsErr)) gsRan = true; else { errors = Combine(errors, gsErr); repaired = orig; }
            if (ct.IsCancellationRequested) return Finalize(filePath, applied, attempts, gsRan, metaApplied, errors, filePath, orig, repaired);
            // metadata on repaired
            attempts++;
            if (TryWriteMetadataInPlace(repaired, metadata, fallbackTitle, out var gsMetaErr)) metaApplied = true; else errors = Combine(errors, "GS metadata failed: "+gsMetaErr);
            attempts++;
            System.IO.File.Copy(metaApplied? repaired : orig, filePath, true);
            return Finalize(filePath, applied, attempts, gsRan, metaApplied, errors, filePath, orig, repaired);
        }
        catch (Exception ex)
        {
            errors = Combine(errors, ex.Message);
            return Finalize(filePath, applied, attempts, gsRan, metaApplied, errors, filePath, orig, repaired);
        }
    }

// Update Finalize to handle empty temps array safely.
    private PdfMetadataUpdateResult Finalize(string filePath, IDictionary<string,string> applied, int attempts, bool gsRan, bool metaApplied, string? errors, string primary, params string[] temps)
    {
        if (temps.Length > 0)
        {
            foreach (var t in temps)
                if (!string.IsNullOrEmpty(t) && t != primary)
                    SafeDelete(t);
            var dir = Path.GetDirectoryName(temps[0]);
            if (!string.IsNullOrEmpty(dir)) TryDeleteDir(dir);
        }
        bool success = metaApplied || gsRan; applied.TryGetValue("Title", out var tTitle);
        WriteSidecar(filePath, applied, tTitle ?? Path.GetFileNameWithoutExtension(filePath), success, errors, metaApplied, gsRan);
        return new(filePath, success, errors, applied, attempts, gsRan, metaApplied);
    }

    private bool TryWriteMetadataInPlace(string path, IDictionary<string,string> metadata, string fallbackTitle, out string? error)
    {
        error=null;
        try
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
            StripInfo(doc);
            ApplyMetadata(doc, metadata, fallbackTitle);
            doc.Save(path);
            return true;
        }
        catch (Exception ex){ error = ex.Message; return false; }
    }

    private bool RunGhostscriptRepair(string input, string output, out string? err)
    {
        err = null; var gsPath = ResolveGhostscript(); if (gsPath == null) { err = "ghostscript not found"; return false; }
        var args = $"-dNOPAUSE -dBATCH -dSAFER -sDEVICE=pdfwrite -dCompatibilityLevel=1.7 -dDetectDuplicateImages=true -dCompressFonts=true -dPDFSETTINGS=/prepress -sOutputFile={Escape(output)} {Escape(input)}";
        var psi = new ProcessStartInfo(gsPath, args){ RedirectStandardError=true, RedirectStandardOutput=true, UseShellExecute=false, CreateNoWindow=true };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null){ err = "failed to start ghostscript"; return false; }
        if (!proc.WaitForExit(120000)) { try { proc.Kill(); } catch {} err = "ghostscript timeout 120s"; return false; }
        var stderr = proc.StandardError.ReadToEnd(); var stdout = proc.StandardOutput.ReadToEnd();
        if (proc.ExitCode != 0){ err = string.IsNullOrWhiteSpace(stderr+stdout)?$"gs exit {proc.ExitCode}" : stderr+stdout; return false; }
        if (!System.IO.File.Exists(output) || new FileInfo(output).Length == 0){ err = "ghostscript produced empty output"; return false; }
        return true;
    }

    private void StripInfo(PdfDocument doc){ try{ var keys=doc.Info.Elements.Keys.ToList(); foreach(var k in keys) doc.Info.Elements.Remove(k);}catch{} }
    private void ApplyMetadata(PdfDocument doc, IDictionary<string,string> metadata,string fallbackTitle){ doc.Info.Title = GetFirst(metadata,fallbackTitle,"Title","TitleEnglish","TitleRomaji","TitleNative") ?? fallbackTitle; doc.Info.Author = metadata.TryGetValue("Authors",out var a)?a:string.Empty; doc.Info.Subject = metadata.TryGetValue("Description",out var d)?Truncate(d,200):string.Empty; var kws=new List<string>(); if(metadata.TryGetValue("Source",out var s)&&!string.IsNullOrWhiteSpace(s))kws.Add(s); if(metadata.TryGetValue("Format",out var f)&&!string.IsNullOrWhiteSpace(f))kws.Add(f); if(metadata.TryGetValue("PublishedDate",out var pd)&&!string.IsNullOrWhiteSpace(pd))kws.Add(pd); if(metadata.TryGetValue("SourceUrl",out var url)&&!string.IsNullOrWhiteSpace(url))kws.Add(url); doc.Info.Keywords = kws.Count>0? string.Join(", ",kws): string.Empty; doc.Info.Creator="PrepKavitaPdf"; }

    private void WriteSidecar(string filePath, IDictionary<string,string> metadata,string fallbackTitle,bool success,string? errors,bool metaApplied,bool gsRan){ if(!_sidecarEnabled)return; try{ var sidecar=filePath+".meta.json"; var obj=new Dictionary<string,object?>{ ["AppliedTitle"]=GetFirst(metadata,fallbackTitle,"Title","TitleEnglish","TitleRomaji","TitleNative") ?? fallbackTitle, ["Success"]=success, ["MetadataApplied"]=metaApplied, ["GhostscriptRan"]=gsRan, ["Errors"]=errors, ["TimestampUtc"]=DateTime.UtcNow }; foreach(var kv in metadata) obj[kv.Key]=kv.Value; var json=JsonSerializer.Serialize(obj,new JsonSerializerOptions{WriteIndented=true}); System.IO.File.WriteAllText(sidecar,json);}catch{} }

    private string? ResolveGhostscript(){ if(!string.IsNullOrWhiteSpace(_gsPathCfg) && ( _gsPathCfg.Contains(Path.DirectorySeparatorChar) || _gsPathCfg.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))) return System.IO.File.Exists(_gsPathCfg)? _gsPathCfg : null; var names = new[]{_gsPathCfg,"gs","gswin64c.exe","gswin32c.exe"}; foreach(var n in names){ var p=Which(n); if(p!=null) return p; } return null; }
    private string? Which(string cmd){ var pathEnv=Environment.GetEnvironmentVariable("PATH")??string.Empty; foreach(var dir in pathEnv.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries)){ var full=Path.Combine(dir,cmd); if(System.IO.File.Exists(full)) return full; } return null; }

    private static string GetFirst(IDictionary<string,string> dict,string fallback,params string[] keys){ foreach(var k in keys) if(dict.TryGetValue(k,out var v) && !string.IsNullOrWhiteSpace(v)) return v; return fallback; }
    private static string Truncate(string v,int m)=>string.IsNullOrEmpty(v)?v:(v.Length<=m?v:v.Substring(0,m));
    private static string Escape(string p)=> p.Contains(' ')?"\""+p+"\"":p;
    private static void SafeDelete(string p){ try{ if(!string.IsNullOrEmpty(p) && System.IO.File.Exists(p)) System.IO.File.Delete(p);}catch{} }
    private static void TryDeleteDir(string d){ try{ if(System.IO.Directory.Exists(d) && !System.IO.Directory.EnumerateFileSystemEntries(d).Any()) System.IO.Directory.Delete(d);}catch{} }
    private static string Combine(string? a,string? b)=> string.IsNullOrWhiteSpace(a)? b ?? string.Empty : (string.IsNullOrWhiteSpace(b)? a : a+"; "+b);
}
