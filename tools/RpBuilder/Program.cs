using System.Text;

namespace RpBuilder;

/// <summary>
/// Minecraft 統合版 実績維持・統合リソースパック ビルド＆検証ツール (.NET 10 / C# 標準ライブラリのみ)
/// 外部ネットワーク通信ゼロ・サードパーティNuGetパッケージゼロで動作します。
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            string projectRoot = ResolveProjectRoot();
            string rpRoot = Path.Combine(projectRoot, "src", "resource_pack");
            string mode = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "all";

            switch (mode)
            {
                case "atlases":
                    AtlasGenerator.GenerateAll(rpRoot, projectRoot);
                    break;

                case "geometries":
                    GeometryGenerator.GenerateAll(rpRoot, projectRoot);
                    break;

                case "validate":
                    RpValidator.ValidateAll(rpRoot, projectRoot);
                    break;

                case "pack":
                    McpackBuilder.Build(rpRoot, projectRoot);
                    break;

                case "report":
                    HtmlReportGenerator.Generate(rpRoot, projectRoot);
                    break;

                case "all":
                    Console.WriteLine("=== [1/5] Generating Texture Atlases & Pack Icon ===");
                    AtlasGenerator.GenerateAll(rpRoot, projectRoot);
                    Console.WriteLine("\n=== [2/5] Generating 3D & HUD Entity Geometries + Animations ===");
                    GeometryGenerator.GenerateAll(rpRoot, projectRoot);
                    Console.WriteLine("\n=== [3/5] Running Integrity, Guardrail & Molang Math Validation ===");
                    RpValidator.ValidateAll(rpRoot, projectRoot);
                    Console.WriteLine("\n=== [4/5] Packaging Distributable .mcpack ===");
                    McpackBuilder.Build(rpRoot, projectRoot);
                    Console.WriteLine("\n=== [5/5] Generating Offline Specification Verification HTML Report ===");
                    HtmlReportGenerator.Generate(rpRoot, projectRoot);
                    break;

                default:
                    Console.Error.WriteLine($"Unknown command '{mode}'. Supported: all | atlases | geometries | validate | pack | report");
                    return 2;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }
    }

    private static string ResolveProjectRoot()
    {
        // カレントディレクトリまたは実行バイナリ親階層から src/resource_pack/ と tools/ を持つルートを特定
        static bool IsProjectRoot(string candidate) =>
            Directory.Exists(Path.Combine(candidate, "src", "resource_pack")) &&
            (Directory.Exists(Path.Combine(candidate, "specs")) || Directory.Exists(Path.Combine(candidate, "tools")));

        string? dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (IsProjectRoot(dir))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (IsProjectRoot(dir))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Project root directory containing 'src/resource_pack' could not be found.");
    }
}
