using System.IO.Compression;
using System.Text.Json.Nodes;

namespace RpBuilder;

/// <summary>
/// .NET 10 標準 System.IO.Compression.ZipArchive を用いて
/// src/resource_pack/ 配下のアセット一式から配布用 .mcpack を生成するクラス
/// </summary>
public static class McpackBuilder
{
    public static void Build(string rpRoot, string projectRoot)
    {
        string manifestPath = Path.Combine(rpRoot, "manifest.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        JsonArray verArr = manifest["header"]!["version"]!.AsArray();
        string versionStr = $"{verArr[0]}.{verArr[1]}.{verArr[2]}";

        string distDir = Path.Combine(projectRoot, "dist");
        Directory.CreateDirectory(distDir);
        foreach (string oldPack in Directory.EnumerateFiles(distDir, "unified_utility_rp_v*.mcpack"))
        {
            File.Delete(oldPack);
        }
        string outMcpack = Path.Combine(distDir, $"unified_utility_rp_v{versionStr}.mcpack");

        var files = Directory.EnumerateFiles(rpRoot, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        using (var fs = new FileStream(outMcpack, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (string file in files)
            {
                string entryName = Path.GetRelativePath(rpRoot, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.SmallestSize);
            }
        }

        Console.WriteLine(
            $"[OK] Packaged {files.Count} files -> {Path.GetRelativePath(projectRoot, outMcpack)} " +
            $"({new FileInfo(outMcpack).Length:N0} bytes)");
    }
}
