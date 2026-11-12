using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RpBuilder;

/// <summary>
/// RP01 1文字分の配置シミュレーション結果 (HTMLスプライトプレビューと自動検証で共用)
/// </summary>
public sealed record SimulatedGlyphSlot(string Bone, int Row, int AdvanceTexels, double StartUnits, double EndUnits, string DisplayText);

/// <summary>
/// 統合リソースパック 構文・BOMなし・実績ON規約・参照整合性・数式・HTMLセキュリティ自動検証クラス
/// </summary>
public static partial class RpValidator
{
    public static void ValidateAll(string rpRoot, string projectRoot)
    {
        var parsedJson = ValidateFilesAndGuardrails(rpRoot, projectRoot, out int fileCount);
        Console.WriteLine($"[PASS] JSON syntax, UTF-8 (no BOM), .lang %% escaping, and Achievements-ON guardrails verified ({fileCount} JSON/material files).");

        ValidateCrossReferences(rpRoot, parsedJson);
        Console.WriteLine("[PASS] Cross-references & UI viewport scissor intersection verified (root_panel/chat_stack + live_player_renderer).");

        ValidateRp01MathAndBiomes(rpRoot, parsedJson);
        Console.WriteLine($"[PASS] RP01 all {GetRp01TestCases().Count} coordinate test cases, dynamic cursor packing, and 59 client biome queries verified.");

        ValidateRp02ChunkSnap();
        Console.WriteLine("[PASS] RP02 3D chunk cage XZ/Y world-grid snap alignment verified (0.000000 block error).");

        string reportPath = Path.Combine(projectRoot, "dist", "spec_verification_report.html");
        if (File.Exists(reportPath))
        {
            ValidateGeneratedHtmlReport(reportPath, projectRoot);
            Console.WriteLine("[PASS] HTML verification report offline/zero-JS/CSP security verified.");
        }

        Console.WriteLine("\n=== ALL VALIDATION CHECKS PASSED SUCCESSFULLY ===");
    }

    public static void ValidateGeneratedHtmlReport(string htmlPath, string projectRoot)
    {
        Check(File.Exists(htmlPath), $"Verification HTML report not found: {htmlPath}");
        byte[] raw = File.ReadAllBytes(htmlPath);
        Check(!(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF),
            $"UTF-8 BOM is not allowed in {Path.GetRelativePath(projectRoot, htmlPath)}");

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        string html = strictUtf8.GetString(raw);

        Check(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "Prohibited <script> tag detected in generated HTML report!");
        Check(!html.Contains("http://", StringComparison.OrdinalIgnoreCase) &&
              !html.Contains("https://", StringComparison.OrdinalIgnoreCase),
            "Prohibited external http(s):// URL detected in generated HTML report!");
        Check(html.Contains("default-src 'none'", StringComparison.Ordinal),
            "Missing strict offline Content-Security-Policy in generated HTML report!");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static Dictionary<string, JsonNode> ValidateFilesAndGuardrails(string rpRoot, string projectRoot, out int count)
    {
        Check(Directory.Exists(rpRoot), $"src/resource_pack directory missing: {rpRoot}");

        // 禁止ファイル・サプライチェーン規約混入検査 (src および tools 全体)
        string[] forbiddenExts = [".material.bin", ".js", ".mjs", ".cjs", ".ts", ".py", ".pyc", ".java", ".rs"];
        string[] forbiddenNames = ["package.json", "package-lock.json", "pnpm-lock.yaml", "yarn.lock", "Cargo.toml"];
        foreach (string scanRoot in (ReadOnlySpan<string>)[Path.Combine(projectRoot, "src"), Path.Combine(projectRoot, "tools")])
        {
            if (!Directory.Exists(scanRoot)) continue;
            var forbidden = Directory.EnumerateFiles(scanRoot, "*", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                            !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(p => forbiddenExts.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) ||
                            forbiddenNames.Any(fn => string.Equals(Path.GetFileName(p), fn, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            Check(forbidden.Count == 0, $"Forbidden files found in {Path.GetFileName(scanRoot)}: {string.Join(", ", forbidden)}");
        }

        // tools 配下の .csproj にサードパーティ PackageReference が存在しないこと、および C# ソースに外部通信APIが含まれないことを検証
        string toolsDir = Path.Combine(projectRoot, "tools");
        if (Directory.Exists(toolsDir))
        {
            foreach (string csproj in Directory.EnumerateFiles(toolsDir, "*.csproj", SearchOption.AllDirectories))
            {
                string xml = File.ReadAllText(csproj, Encoding.UTF8);
                Check(!xml.Contains("<PackageReference", StringComparison.OrdinalIgnoreCase),
                    $"Third-party NuGet PackageReference is prohibited: {Path.GetRelativePath(projectRoot, csproj)}");
            }

            string[] forbiddenNetTokens = ["System.Net.Http", "System.Net.Sockets", "HttpClient", "WebClient", "WebRequest", "TcpClient", "UdpClient"];
            foreach (string csFile in Directory.EnumerateFiles(toolsDir, "*.cs", SearchOption.AllDirectories))
            {
                if (csFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    csFile.EndsWith("RpValidator.cs", StringComparison.OrdinalIgnoreCase) ||
                    csFile.EndsWith("HtmlReportGenerator.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string src = File.ReadAllText(csFile, Encoding.UTF8);
                foreach (string token in forbiddenNetTokens)
                {
                    Check(!src.Contains(token, StringComparison.Ordinal),
                        $"Prohibited network API '{token}' detected in {Path.GetRelativePath(projectRoot, csFile)}");
                }
            }

            // HtmlReportGenerator.cs についても WebUtility.HtmlEncode (System.Net 名前空間の純粋文字列エスケープ) 以外の通信APIを禁止
            string htmlGenPath = Path.Combine(toolsDir, "RpBuilder", "HtmlReportGenerator.cs");
            if (File.Exists(htmlGenPath))
            {
                string htmlGenSrc = File.ReadAllText(htmlGenPath, Encoding.UTF8);
                foreach (string token in forbiddenNetTokens)
                {
                    Check(!htmlGenSrc.Contains(token, StringComparison.Ordinal),
                        $"Prohibited network API '{token}' detected in HtmlReportGenerator.cs");
                }
            }
        }

        var jsonFiles = Directory.EnumerateFiles(rpRoot, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".material", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Check(jsonFiles.Count > 0, "No JSON files found in src/resource_pack.");
        var parsed = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (string path in jsonFiles)
        {
            byte[] raw = File.ReadAllBytes(path);
            Check(!(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF),
                $"UTF-8 BOM is not allowed in {Path.GetRelativePath(projectRoot, path)}");

            string text = strictUtf8.GetString(raw);
            JsonNode? node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
            Check(node is not null, $"Null JSON root in {Path.GetRelativePath(projectRoot, path)}");
            parsed[Path.GetFullPath(path)] = node!;
        }

        // manifest.json 実績維持ガードレール検査 & min_engine_version >= 1.21.130 検査
        string manifestPath = Path.GetFullPath(Path.Combine(rpRoot, "manifest.json"));
        Check(parsed.TryGetValue(manifestPath, out JsonNode? manifestNode), "manifest.json missing.");
        JsonObject manifest = manifestNode!.AsObject();

        Check(manifest["format_version"]?.GetValue<int>() == 2, "manifest.json format_version must be 2.");
        JsonArray minEng = manifest["header"]?["min_engine_version"]?.AsArray()
            ?? throw new InvalidOperationException("manifest.json min_engine_version missing.");
        int maj = minEng[0]!.GetValue<int>(), min = minEng[1]!.GetValue<int>(), pat = minEng[2]!.GetValue<int>();
        Check((maj > 1) || (maj == 1 && min > 21) || (maj == 1 && min == 21 && pat >= 130),
            $"manifest.json min_engine_version must be >= [1, 21, 130] for native client biome queries (got [{maj}, {min}, {pat}]).");

        JsonArray modules = manifest["modules"]?.AsArray() ?? throw new InvalidOperationException("manifest.json modules missing.");
        Check(modules.Count == 1, "manifest.json must have exactly 1 module.");
        Check(modules[0]?["type"]?.GetValue<string>() == "resources", "manifest.json module type must be 'resources' ONLY.");
        Check(!manifest.ContainsKey("dependencies"), "manifest.json must not depend on behavior/script packs.");
        Check(!manifest.ContainsKey("capabilities"), "manifest.json must not require experimental capabilities.");

        JsonArray subpacks = manifest["subpacks"]?.AsArray() ?? [];
        var subpackFolders = subpacks.Select(s => s!["folder_name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Check(subpackFolders.Contains("full") && subpackFolders.Contains("hud_and_ui_only"), "Required subpacks missing.");
        foreach (string folder in subpackFolders)
        {
            Check(Directory.Exists(Path.Combine(rpRoot, "subpacks", folder)), $"Subpack folder missing: subpacks/{folder}");
        }

        // .lang UTF-8 (BOMなし) および単独 '%' 未エスケープ防止検証
        foreach (string langName in (ReadOnlySpan<string>)["ja_JP.lang", "en_US.lang"])
        {
            string langPath = Path.Combine(rpRoot, "texts", langName);
            Check(File.Exists(langPath), $"Missing language file: texts/{langName}");
            byte[] rawLang = File.ReadAllBytes(langPath);
            Check(!(rawLang.Length >= 3 && rawLang[0] == 0xEF && rawLang[1] == 0xBB && rawLang[2] == 0xBF),
                $"UTF-8 BOM detected in texts/{langName}!");
            string textLang = strictUtf8.GetString(rawLang);
            Check(textLang.Contains("pack.name=", StringComparison.Ordinal) &&
                  textLang.Contains("rp03.trade.locked_label=", StringComparison.Ordinal),
                $"Missing required translation keys in {langName}");
            Check(!UnescapedPercentRegex().IsMatch(textLang),
                $"Unescaped single '%' detected in texts/{langName}; use '%%' in Bedrock .lang files!");
        }

        count = jsonFiles.Count;
        return parsed;
    }

    [GeneratedRegex(@"(?<!%)%(?!%)")]
    private static partial Regex UnescapedPercentRegex();

    private static void ValidateCrossReferences(string rpRoot, Dictionary<string, JsonNode> parsed)
    {
        JsonObject matRoot = parsed[Path.GetFullPath(Path.Combine(rpRoot, "materials", "entity.material"))]["materials"]!.AsObject();
        Check(matRoot.ContainsKey("rp01_hud_static:entity_alphatest") && matRoot.ContainsKey("rp01_hud_sprite:entity_alphatest"),
            "RP01 HUD materials must inherit from ':entity_alphatest' for guaranteed RenderDragon UI alpha-cutout + USE_UV_ANIM support.");
        var matDefs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var kv in matRoot)
        {
            if (kv.Key == "version") continue;
            string shortName = kv.Key.Split(':')[0];
            matDefs[shortName] = kv.Value!.AsObject();
        }

        var geoBones = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var geoBounds = new Dictionary<string, (double W, double H)>(StringComparer.Ordinal);
        int maxGeoVertices = 0;
        string maxGeoId = "";
        foreach (string geoPath in Directory.EnumerateFiles(Path.Combine(rpRoot, "models", "entity"), "*.geo.json"))
        {
            JsonObject geoDoc = parsed[Path.GetFullPath(geoPath)].AsObject();
            string geoFmt = geoDoc["format_version"]?.GetValue<string>() ?? "";
            JsonArray geos = geoDoc["minecraft:geometry"]!.AsArray();
            foreach (JsonNode? g in geos)
            {
                JsonObject gDesc = g!["description"]!.AsObject();
                string gid = gDesc["identifier"]!.GetValue<string>();
                double vbW = gDesc["visible_bounds_width"]?.GetValue<double>() ?? 0.0;
                double vbH = gDesc["visible_bounds_height"]?.GetValue<double>() ?? 0.0;
                geoBounds[gid] = (vbW, vbH);

                JsonArray bonesArr = g["bones"]!.AsArray();
                var bones = bonesArr.Select(b => b!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                geoBones[gid] = bones;

                int geoVertexCount = 0;
                foreach (JsonNode? b in bonesArr)
                {
                    if (b!["cubes"] is not JsonArray cubesArr) continue;
                    foreach (JsonNode? c in cubesArr)
                    {
                        JsonObject cObj = c!.AsObject();
                        if (geoFmt == "1.12.0")
                        {
                            Check(!cObj.ContainsKey("rotation"),
                                $"{gid} (bone '{b["name"]}'): Per-cube 'rotation' is unsupported in format_version 1.12.0! Use axis-aligned cubes or bone rotation.");
                        }

                        int faces = cObj["uv"] is JsonObject uvFaces ? uvFaces.Count : 6;
                        geoVertexCount += faces * 4;
                    }
                }

                Check(geoVertexCount <= 60000,
                    $"{gid} exceeds RenderDragon uint16 vertex safety ceiling ({geoVertexCount:N0} > 60,000; hard limit 65,535)!");
                if (geoVertexCount > maxGeoVertices)
                {
                    maxGeoVertices = geoVertexCount;
                    maxGeoId = gid;
                }
            }
        }
        Console.WriteLine($"[INFO] Geometry vertex safety verified: max={maxGeoVertices:N0} / 65,535 uint16 limit ({maxGeoId}).");

        var animIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string animPath in Directory.EnumerateFiles(Path.Combine(rpRoot, "animations"), "*.animation.json"))
        {
            foreach (var kv in parsed[Path.GetFullPath(animPath)]["animations"]!.AsObject())
            {
                animIds.Add(kv.Key);
            }
        }

        var rcDefs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (string rcPath in Directory.EnumerateFiles(Path.Combine(rpRoot, "render_controllers"), "*.render_controllers.json"))
        {
            foreach (var kv in parsed[Path.GetFullPath(rcPath)]["render_controllers"]!.AsObject())
            {
                rcDefs[kv.Key] = kv.Value!.AsObject();
            }
        }

        HashSet<string> builtinMats = ["entity_alphatest", "player_animated", "player_spectator", "armor_stand"];
        foreach (string entName in (ReadOnlySpan<string>)["player.entity.json", "armor_stand.entity.json"])
        {
            JsonObject entRoot = parsed[Path.GetFullPath(Path.Combine(rpRoot, "entity", entName))].AsObject();
            Check(entRoot["format_version"]?.GetValue<string>() == "1.10.0",
                $"{entName}: format_version must remain '1.10.0' for Bedrock client_entity compatibility.");
            JsonObject desc = entRoot["minecraft:client_entity"]!["description"]!.AsObject();

            var entMats = desc["materials"]!.AsObject().ToDictionary(k => k.Key, v => v.Value!.GetValue<string>(), StringComparer.Ordinal);
            foreach (var (alias, target) in entMats)
            {
                if (!builtinMats.Contains(target))
                {
                    Check(matDefs.ContainsKey(target), $"{entName}: Material '{target}' (alias '{alias}') not defined.");
                }
            }

            var entTexs = desc["textures"]!.AsObject().ToDictionary(k => k.Key, v => v.Value!.GetValue<string>(), StringComparer.Ordinal);
            foreach (var (_, texTarget) in entTexs)
            {
                if (texTarget.StartsWith("textures/rp_custom/", StringComparison.Ordinal))
                {
                    string pngPath = Path.Combine(rpRoot, texTarget.Replace('/', Path.DirectorySeparatorChar) + ".png");
                    Check(File.Exists(pngPath), $"{entName}: Missing custom texture PNG '{pngPath}'.");
                }
            }

            var entGeos = desc["geometry"]!.AsObject().ToDictionary(k => k.Key, v => v.Value!.GetValue<string>(), StringComparer.Ordinal);
            foreach (var (alias, geoTarget) in entGeos)
            {
                if (alias.StartsWith("rp01_", StringComparison.Ordinal) || alias.StartsWith("rp02_", StringComparison.Ordinal))
                {
                    Check(geoBones.ContainsKey(geoTarget), $"{entName}: Missing custom geometry '{geoTarget}'.");
                }
                if (entName == "armor_stand.entity.json")
                {
                    Check(geoBounds.TryGetValue(geoTarget, out var bounds),
                        $"{entName}: Geometry '{geoTarget}' (alias '{alias}') must be overridden locally so visible_bounds is expanded!");
                    Check(bounds.W >= 200.0 && bounds.H >= 200.0,
                        $"{entName}: Geometry '{geoTarget}' visible_bounds [{bounds.W}, {bounds.H}] must be >= 200x200 to prevent off-screen frustum culling of 44m wires!");
                }
            }

            var entAnims = desc["animations"]!.AsObject().ToDictionary(k => k.Key, v => v.Value!.GetValue<string>(), StringComparer.Ordinal);
            foreach (var (alias, animTarget) in entAnims)
            {
                if (alias.StartsWith("rp01_", StringComparison.Ordinal) || alias.StartsWith("rp02_", StringComparison.Ordinal))
                {
                    Check(animIds.Contains(animTarget), $"{entName}: Missing custom animation '{animTarget}'.");
                }
            }

            foreach (JsonNode? rcEntry in desc["render_controllers"]!.AsArray())
            {
                string rcId;
                string rcCond = "";
                if (rcEntry is JsonValue jv)
                {
                    rcId = jv.GetValue<string>();
                }
                else
                {
                    var kv = rcEntry!.AsObject().First();
                    rcId = kv.Key;
                    rcCond = kv.Value!.GetValue<string>();
                }

                if (rcId.StartsWith("controller.render.rp01_", StringComparison.Ordinal))
                {
                    Check(rcCond.Contains("q.is_in_ui", StringComparison.Ordinal),
                        $"{rcId} must check q.is_in_ui so live_player_renderer renders on HUD even when paperdoll is hidden (got '{rcCond}').");
                }

                if (rcId.StartsWith("controller.render.rp01_", StringComparison.Ordinal) ||
                    rcId.StartsWith("controller.render.rp02_", StringComparison.Ordinal))
                {
                    Check(rcDefs.TryGetValue(rcId, out JsonObject? rcBody), $"{entName}: Missing render controller '{rcId}'.");
                    string geoAlias = rcBody!["geometry"]!.GetValue<string>().Split('.', 2)[1];
                    Check(entGeos.TryGetValue(geoAlias, out string? targetGid), $"{rcId}: geometry alias '{geoAlias}' missing.");

                    if (rcBody["part_visibility"] is JsonArray pvArr)
                    {
                        foreach (JsonNode? pvItem in pvArr)
                        {
                            foreach (var bkv in pvItem!.AsObject())
                            {
                                if (bkv.Key != "*")
                                {
                                    Check(geoBones[targetGid!].Contains(bkv.Key),
                                        $"{rcId}: part_visibility bone '{bkv.Key}' missing in {targetGid}.");
                                }
                            }
                        }
                    }

                    foreach (JsonNode? mMap in rcBody["materials"]!.AsArray())
                    {
                        foreach (var mkv in mMap!.AsObject())
                        {
                            string mAlias = mkv.Value!.GetValue<string>().Split('.', 2)[1];
                            Check(entMats.TryGetValue(mAlias, out string? actualMat), $"{rcId}: material alias '{mAlias}' missing.");
                            if (rcBody.ContainsKey("uv_anim"))
                            {
                                var defines = matDefs[actualMat!]["+defines"]?.AsArray().Select(d => d!.GetValue<string>()).ToHashSet() ?? [];
                                Check(defines.Contains("USE_UV_ANIM"),
                                    $"{rcId} uses uv_anim, so material '{actualMat}' MUST define 'USE_UV_ANIM'!");
                            }
                        }
                    }

                    foreach (JsonNode? tRef in rcBody["textures"]!.AsArray())
                    {
                        string tAlias = tRef!.GetValue<string>().Split('.', 2)[1];
                        Check(entTexs.ContainsKey(tAlias), $"{rcId}: texture alias '{tAlias}' missing.");
                    }
                }
            }
        }

        // JSON UI 差分 & RP01 HUD マウントパス・シザー矩形交差検証
        JsonArray uiDefs = parsed[Path.GetFullPath(Path.Combine(rpRoot, "ui", "_ui_defs.json"))]["ui_defs"]!.AsArray();
        foreach (JsonNode? modNode in uiDefs)
        {
            string modRel = modNode!.GetValue<string>();
            Check(File.Exists(Path.Combine(rpRoot, modRel.Replace('/', Path.DirectorySeparatorChar))),
                $"UI module registered in _ui_defs.json missing: {modRel}");
        }

        JsonObject hudScreen = parsed[Path.GetFullPath(Path.Combine(rpRoot, "ui", "hud_screen.json"))].AsObject();
        Check(hudScreen.ContainsKey("root_panel/chat_stack"),
            "ui/hud_screen.json must target 'root_panel/chat_stack' (top-level 'chat_stack' is invalid in Bedrock HUD).");
        JsonObject hudMod = hudScreen["root_panel/chat_stack"]!["modifications"]![0]!.AsObject();
        Check(hudMod["operation"]?.GetValue<string>() == "insert_before" &&
              hudMod["control_name"]?.GetValue<string>() == "chat_panel",
            "ui/hud_screen.json modification must insert_before 'chat_panel' so it sits directly below position & days_played.");

        JsonObject chunkPanelDoc = parsed[Path.GetFullPath(Path.Combine(rpRoot, "ui", "modules", "rp01_hud_chunk_panel.json"))].AsObject();
        JsonArray wrapperControls = chunkPanelDoc["rp01_hud_chunk_wrapper"]!["controls"]!.AsArray();
        JsonObject viewportObj = wrapperControls[1]!["rp01_hud_viewport"]!.AsObject();
        Check(viewportObj["clips_children"]?.GetValue<bool>() == true && viewportObj["allow_clipping"]?.GetValue<bool>() == true,
            "rp01_hud_viewport must enable both clips_children and allow_clipping.");
        double vpW = viewportObj["size"]![0]!.GetValue<double>();
        double vpH = viewportObj["size"]![1]!.GetValue<double>();

        JsonObject projObj = viewportObj["controls"]![0]!["rp01_hud_player_projection"]!.AsObject();
        Check(projObj["renderer"]?.GetValue<string>() == "live_player_renderer",
            "rp01_hud_player_projection must use 'live_player_renderer' (hud_player_renderer hides when standing still).");
        double prW = projObj["size"]![0]!.GetValue<double>();
        double prH = projObj["size"]![1]!.GetValue<double>();
        double prX = projObj["offset"]![0]!.GetValue<double>();
        double prY = projObj["offset"]![1]!.GetValue<double>();

        double overlapW = Math.Min(vpW, prX + prW) - Math.Max(0.0, prX);
        double overlapH = Math.Min(vpH, prY + prH) - Math.Max(0.0, prY);
        Check(overlapW >= 1.0 && overlapH >= 1.0,
            $"rp01_hud_player_projection rect [{prX},{prY},{prW},{prH}] would be scissor-culled by rp01_hud_viewport [0,0,{vpW},{vpH}] (overlap={overlapW}x{overlapH})!");

        // 3D→2D UI投影座標の厳密検算: スティーブ本体が [0..vpW, 0..vpH] に 0px も侵入せず、1段目・2段目テキストが完全にプレート内に収まること
        Check(Math.Abs(prW - prH) < 1e-6, "rp01_hud_player_projection size must be square [S, S].");
        double pxPerUnit = prW / 16.0;
        double xCenter = prX + 0.5 * prW;
        double yFeet = prY + 1.0 * prH;
        double steveBootLeftUi = xCenter - 4.69 * pxPerUnit;
        Check(steveBootLeftUi > vpW,
            $"Steve boots could bleed into HUD viewport: steveBootLeftUi={steveBootLeftUi:F2} <= vpW={vpW:F2}");

        double textLeftUi = xCenter + RpConstants.Rp01PanelRootX * pxPerUnit;
        double line1TopUi = yFeet - (RpConstants.Rp01Line1Y0 + RpConstants.Rp01LineHUnits) * pxPerUnit;
        double line2BottomUi = yFeet - RpConstants.Rp01Line2Y0 * pxPerUnit;
        Check(textLeftUi >= 2.0 && textLeftUi <= 12.0,
            $"RP01 text left margin out of expected HUD plate range: {textLeftUi:F2}px");
        Check(line1TopUi >= 0.5 && line2BottomUi <= vpH - 0.5,
            $"RP01 2-line vertical projection [{line1TopUi:F2}..{line2BottomUi:F2}] exceeds HUD viewport [0..{vpH:F2}]!");

        // インベントリ画面 (S=30, 1.875px/unit) およびポーズ画面 (S=45, 2.8125px/unit) で画面左外 (X<0) に完全退避すること
        double worstLine1RightModel = RpConstants.Rp01PanelRootX + 172 * RpConstants.Rp01UnitsPerTexel;
        Check(172.0 + worstLine1RightModel * (30.0 / 16.0) < 0.0,
            "RP01 text could bleed onto inventory_screen!");
        Check(330.0 + worstLine1RightModel * (45.0 / 16.0) < 0.0,
            "RP01 text could bleed onto pause_screen!");

        // UI空間における rp01_yaw_cancel 回転ゼロ & q.is_in_ui スケール検証
        JsonObject rp01Anim = parsed[Path.GetFullPath(Path.Combine(rpRoot, "animations", "rp01_hud_readout.animation.json"))]
            ["animations"]!["animation.rp01.hud_readout"]!["bones"]!["rp01_yaw_cancel"]!.AsObject();
        JsonArray yawRot = rp01Anim["rotation"]!.AsArray();
        Check(yawRot[0] is JsonValue && yawRot[1] is JsonValue && yawRot[2] is JsonValue &&
              yawRot[0]!.GetValue<double>() == 0.0 && yawRot[1]!.GetValue<double>() == 0.0 && yawRot[2]!.GetValue<double>() == 0.0,
            "rp01_yaw_cancel rotation must be [0, 0, 0] in live_player_renderer UI space!");
        string yawScale = rp01Anim["scale"]!.GetValue<string>();
        Check(yawScale.Contains("q.is_in_ui", StringComparison.Ordinal),
            $"rp01_yaw_cancel scale must check q.is_in_ui (got '{yawScale}').");

        foreach (string screenFile in (ReadOnlySpan<string>)["hud_screen.json", "trade_2_screen.json", "trade_pocket_screen.json", "trade_2_screen_pocket.json"])
        {
            var info = new FileInfo(Path.Combine(rpRoot, "ui", screenFile));
            Check(info.Exists, $"Missing UI patch file: ui/{screenFile}");
            Check(info.Length < 2048, $"ui/{screenFile} is too large ({info.Length}B); full vanilla copies are prohibited!");
        }

        // subpacks/full/entity/armor_stand.entity.json の完全同期 & ポーズ 1..2 限定 (ポーズ3廃止) 検証
        string baseArmorStandPath = Path.Combine(rpRoot, "entity", "armor_stand.entity.json");
        string fullArmorStandPath = Path.Combine(rpRoot, "subpacks", "full", "entity", "armor_stand.entity.json");
        Check(File.Exists(fullArmorStandPath), "Missing subpacks/full/entity/armor_stand.entity.json.");
        string baseArmorStandRaw = File.ReadAllText(baseArmorStandPath, Encoding.UTF8);
        string fullArmorStandRaw = File.ReadAllText(fullArmorStandPath, Encoding.UTF8);
        Check(baseArmorStandRaw == fullArmorStandRaw,
            "entity/armor_stand.entity.json and subpacks/full/entity/armor_stand.entity.json MUST be byte-for-byte identical!");
        Check(baseArmorStandRaw.Contains("pose_index <= 2", StringComparison.Ordinal) &&
              !baseArmorStandRaw.Contains("pose_index <= 3", StringComparison.Ordinal),
            "armor_stand.entity.json pre_animation must activate RP02 only for pose_index 1..2 (Pose 3 must remain disabled).");

        // RP03 村人未解放取引 (Lv2〜Lv5) のホバーエンチャントツールチップ表示保証検証
        foreach (string tradeRel in (ReadOnlySpan<string>)[
            "ui/trade_2_screen.json",
            "ui/trade_2_screen_pocket.json",
            "ui/trade_pocket_screen.json"])
        {
            JsonObject tradeDoc = parsed[Path.GetFullPath(Path.Combine(rpRoot, tradeRel.Replace('/', Path.DirectorySeparatorChar)))].AsObject();
            JsonObject? toggleObj = tradeDoc["trade_toggle"]?.AsObject();
            Check(toggleObj is not null &&
                  toggleObj["$toggle_enabled_binding_type"]?.GetValue<string>() == "none" &&
                  toggleObj["enabled"]?.GetValue<bool>() == true,
                $"{tradeRel}: 'trade_toggle' must set $toggle_enabled_binding_type='none' and enabled=true so locked Lv2-Lv5 rows allow pointer hover hit-testing!");

            Check(tradeDoc["single_item_grid"]?["enabled"]?.GetValue<bool>() == true &&
                  tradeDoc["single_item_slot"]?["enabled"]?.GetValue<bool>() == true,
                $"{tradeRel}: 'single_item_grid' and 'single_item_slot' must both set enabled=true!");

            JsonObject? hoverObj = tradeDoc["hover_button"]?.AsObject();
            Check(hoverObj is not null &&
                  hoverObj["enabled"]?.GetValue<bool>() == true &&
                  hoverObj["visible"]?.GetValue<bool>() == true &&
                  hoverObj["consume_event"]?.GetValue<bool>() == false &&
                  hoverObj["consume_hover_events"]?.GetValue<bool>() == true,
                $"{tradeRel}: 'hover_button' must set enabled=true, visible=true, consume_event=false, and consume_hover_events=true so enchantment hover_text_renderer triggers!");
        }
    }

    internal static (string Text, IReadOnlyList<SimulatedGlyphSlot> Slots) SimulateRp01Slots(double x, double y, double z)
    {
        int bx = (int)Math.Floor(x);
        int bz = (int)Math.Floor(z);
        int cx = (int)Math.Floor(bx / 16.0);
        int cz = (int)Math.Floor(bz / 16.0);
        int rx = (bx - cx * 16) + 1;
        int rz = (bz - cz * 16) + 1;

        Check(rx >= 1 && rx <= 16, $"Rx out of range 1..16: {rx}");
        Check(rz >= 1 && rz <= 16, $"Rz out of range 1..16: {rz}");

        int rxGe10 = rx >= 10 ? 1 : 0;
        int rxD0 = rx % 10;
        int rzGe10 = rz >= 10 ? 1 : 0;
        int rzD0 = rz % 10;

        int cxNeg = cx < 0 ? 1 : 0;
        int acx = Math.Min(99999, Math.Abs(cx));
        int czNeg = cz < 0 ? 1 : 0;
        int acz = Math.Min(99999, Math.Abs(cz));

        int edgeRow = (rx == 1 && rz == 1) ? 17
                    : (rx == 16 && rz == 1) ? 18
                    : (rx == 1 && rz == 16) ? 19
                    : (rx == 16 && rz == 16) ? 20
                    : (rx == 1 || rx == 16 || rz == 1 || rz == 16) ? 16
                    : 0;

        (string Bone, int Row, int Vis)[] rawSlots =
        [
            ("rx_d1", 1, rxGe10),
            ("rx_d0", rxD0, 1),
            ("sep_comma1", 11, 1),
            ("rz_d1", 1, rzGe10),
            ("rz_d0", rzD0, 1),
            ("sep_open", 12, 1),
            ("cx_sign", 10, cxNeg),
            ("cx_d4", acx / 10000, acx >= 10000 ? 1 : 0),
            ("cx_d3", (acx / 1000) % 10, acx >= 1000 ? 1 : 0),
            ("cx_d2", (acx / 100) % 10, acx >= 100 ? 1 : 0),
            ("cx_d1", (acx / 10) % 10, acx >= 10 ? 1 : 0),
            ("cx_d0", acx % 10, 1),
            ("sep_comma2", 11, 1),
            ("cz_sign", 10, czNeg),
            ("cz_d4", acz / 10000, acz >= 10000 ? 1 : 0),
            ("cz_d3", (acz / 1000) % 10, acz >= 1000 ? 1 : 0),
            ("cz_d2", (acz / 100) % 10, acz >= 100 ? 1 : 0),
            ("cz_d1", (acz / 10) % 10, acz >= 10 ? 1 : 0),
            ("cz_d0", acz % 10, 1),
            ("sep_close", 13, 1),
            ("edge_label", edgeRow > 0 ? edgeRow : 16, edgeRow > 0 ? 1 : 0)
        ];

        var sb = new StringBuilder();
        var visibleSlots = new List<SimulatedGlyphSlot>();
        double cursorX = 0.0;

        foreach (var (bone, row, vis) in rawSlots)
        {
            GlyphSpec spec = RpConstants.Rp01GlyphSpecs[row];
            double wUnits = spec.AdvanceTexels * RpConstants.Rp01UnitsPerTexel;
            if (vis != 0)
            {
                string disp = row == 11 ? ", " : spec.Text;
                visibleSlots.Add(new SimulatedGlyphSlot(bone, row, spec.AdvanceTexels, cursorX, cursorX + wUnits, disp));
                cursorX += wUnits;
                sb.Append(disp);
            }
        }

        for (int i = 1; i < visibleSlots.Count; i++)
        {
            Check(Math.Abs(visibleSlots[i - 1].EndUnits - visibleSlots[i].StartUnits) < 1e-6,
                $"Cursor gap/overlap detected between {visibleSlots[i - 1].Bone} and {visibleSlots[i].Bone}!");
        }

        return (sb.ToString(), visibleSlots);
    }

    internal static IReadOnlyList<((double X, double Y, double Z) Pos, string Expected, string Note)> GetRp01TestCases() =>
    [
        ((34, 102, -31),     "3, 2 (2, -2)",               "実機スクリーンショット #2 座標 (正X・負Z・タイガ)"),
        ((91, 70, -15),      "12, 2 (5, -1)",              "実機スクリーンショット #1 座標 (正X・負Z・チャンク内側)"),
        ((25, 77, 132),      "10, 5 (1, 8)",               "仕様書 §2.3 検算ケース #1 (正座標・チャンク内側)"),
        ((31, 37, 132),      "16, 5 (1, 8) : 線",          "仕様書 §2.3 検算ケース #2 (東端 Rx=16 境界線)"),
        ((64, 157, 159),     "1, 16 (4, 9) : 左下",        "仕様書 §2.3 検算ケース #3 (南西角 Rx=1, Rz=16)"),
        ((0, 64, 0),         "1, 1 (0, 0) : 左上",         "仕様書 §2.3 検算ケース #4 (原点・北西角 Rx=1, Rz=1)"),
        ((-1, 64, -16),      "16, 1 (-1, -1) : 右上",      "仕様書 §2.3 検算ケース #5 (負座標・北東角 Rx=16, Rz=1)"),
        ((15, 64, 15),       "16, 16 (0, 0) : 右下",       "四隅網羅検証 (南東角 Rx=16, Rz=16)"),
        ((-0.01, 64, -15.99),"16, 1 (-1, -1) : 右上",      "負の小数座標 math.floor 境界検証"),
        ((-16, 64, -1),      "1, 16 (-1, -1) : 左下",      "負チャンク南西角検証"),
        ((-160000, 64, 159999), "1, 16 (-10000, 9999) : 左下", "±9999チャンク超 大座標桁詰め検証")
    ];

    private static void ValidateRp01MathAndBiomes(string rpRoot, Dictionary<string, JsonNode> parsed)
    {
        foreach (var (pos, expected, _) in GetRp01TestCases())
        {
            var (actual, _) = SimulateRp01Slots(pos.X, pos.Y, pos.Z);
            Check(actual == expected,
                $"RP01 Test Case Mismatch at ({pos.X}, {pos.Y}, {pos.Z}): expected '{expected}', got '{actual}'");
        }

        string playerRaw = File.ReadAllText(Path.Combine(rpRoot, "entity", "player.entity.json"), Encoding.UTF8);
        string animRaw = File.ReadAllText(Path.Combine(rpRoot, "animations", "rp01_hud_readout.animation.json"), Encoding.UTF8);

        Check(!playerRaw.Contains("has_biome_tag", StringComparison.Ordinal) &&
              !animRaw.Contains("has_biome_tag", StringComparison.Ordinal),
            "Server/worldgen query 'has_biome_tag' always returns 0 on client entities; use q.entity_biome_has_any_identifier / _tags!");

        JsonArray preAnimArr = parsed[Path.GetFullPath(Path.Combine(rpRoot, "entity", "player.entity.json"))]
            ["minecraft:client_entity"]!["description"]!["scripts"]!["pre_animation"]!.AsArray();
        string joinedPreAnim = string.Join("\n", preAnimArr.Select(n => n!.GetValue<string>()));

        foreach (BiomeEntry entry in RpConstants.Rp01BiomeEntries)
        {
            if (entry.Row == 0 || entry.MolangCondition is null) continue;
            string expectedFrag = string.Create(CultureInfo.InvariantCulture, $"{entry.MolangCondition}) ? {entry.Row:F1} : v.biome_idx;");
            Check(joinedPreAnim.Contains(expectedFrag, StringComparison.Ordinal),
                $"Biome rule #{entry.Row} ({entry.JapaneseName}) missing or out of sync in player.entity.json pre_animation!");
        }

        // 1) クライアントバイオームID指定検証 + 2) 複合バイオームタグ優先順位検証
        (string[] Ids, string[] Tags, string ExpectedJa)[] priorityChecks =
        [
            (["minecraft:birch_forest"], ["forest", "birch"], "シラカバの森"),
            (["minecraft:old_growth_birch_forest"], ["forest", "birch", "mutated"], "原生林のシラカバの森"),
            ([], ["jungle", "bamboo"], "竹林"),
            ([], ["jungle", "edge"], "ジャングルの端"),
            ([], ["jungle"], "ジャングル"),
            ([], ["birch", "mutated", "forest"], "原生林のシラカバの森"),
            ([], ["roofed", "forest"], "暗い森"),
            (["minecraft:flower_forest"], ["forest"], "花の森"),
            ([], ["deep", "warm", "ocean"], "ぬるい深海"),
            ([], ["ocean"], "海"),
            ([], ["ice_plains", "mutated"], "樹氷"),
            ([], ["extreme_hills", "plus_trees"], "吹きさらしの森"),
            (["minecraft:deep_dark"], [], "ディープダーク"),
            (["minecraft:cherry_grove"], ["mountains"], "桜の林"),
            (["minecraft:pale_garden"], ["forest"], "ペールガーデン"),
            (["minecraft:unknown_future_biome"], ["unknown_future_biome_tag"], "その他のバイオーム")
        ];

        foreach (var (ids, tags, expectedJa) in priorityChecks)
        {
            var idSet = ids.ToHashSet(StringComparer.Ordinal);
            var tagSet = tags.ToHashSet(StringComparer.Ordinal);
            string gotJa = EvaluateBiomePriority(idSet, tagSet);
            Check(gotJa == expectedJa,
                $"Biome priority failed for ids=[{string.Join(",", ids)}] tags=[{string.Join(",", tags)}]: expected '{expectedJa}', got '{gotJa}'");
        }
    }

    internal static string EvaluateBiomePriority(HashSet<string> activeIdentifiers, HashSet<string> activeTags)
    {
        foreach (BiomeEntry entry in RpConstants.Rp01BiomeEntries)
        {
            if (entry.Row == 0 || entry.MolangCondition is null) continue;
            if (EvalBoolExpr(entry.MolangCondition, activeIdentifiers, activeTags))
            {
                return entry.JapaneseName;
            }
        }
        return RpConstants.Rp01BiomeEntries[0].JapaneseName;
    }

    [GeneratedRegex(@"q\.(entity_biome_has_any_identifier|entity_biome_has_any_tags|entity_biome_has_all_tags)\(([^)]+)\)")]
    private static partial Regex ClientBiomeQueryRegex();

    private static bool EvalBoolExpr(string molangCond, HashSet<string> activeIdentifiers, HashSet<string> activeTags)
    {
        string expr = ClientBiomeQueryRegex().Replace(molangCond, m =>
        {
            string fn = m.Groups[1].Value;
            string[] args = m.Groups[2].Value
                .Split(',')
                .Select(a => a.Trim().Trim('\''))
                .Where(a => a.Length > 0)
                .ToArray();

            bool matched = fn switch
            {
                "entity_biome_has_any_identifier" => args.Any(activeIdentifiers.Contains),
                "entity_biome_has_any_tags"       => args.Any(activeTags.Contains),
                "entity_biome_has_all_tags"       => args.All(activeTags.Contains),
                _ => throw new InvalidOperationException($"Unsupported biome query function: {fn}")
            };
            return matched ? "1" : "0";
        });

        int pos = 0;
        bool result = ParseOr(expr, ref pos);
        SkipWs(expr, ref pos);
        Check(pos == expr.Length, $"Unexpected trailing token in biome expression: {molangCond} -> {expr}");
        return result;
    }

    private static void SkipWs(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
    }

    private static bool ParseOr(string s, ref int pos)
    {
        bool left = ParseAnd(s, ref pos);
        while (true)
        {
            SkipWs(s, ref pos);
            if (pos + 1 < s.Length && s[pos] == '|' && s[pos + 1] == '|')
            {
                pos += 2;
                bool right = ParseAnd(s, ref pos);
                left = left || right;
            }
            else break;
        }
        return left;
    }

    private static bool ParseAnd(string s, ref int pos)
    {
        bool left = ParsePrimary(s, ref pos);
        while (true)
        {
            SkipWs(s, ref pos);
            if (pos + 1 < s.Length && s[pos] == '&' && s[pos + 1] == '&')
            {
                pos += 2;
                bool right = ParsePrimary(s, ref pos);
                left = left && right;
            }
            else break;
        }
        return left;
    }

    private static bool ParsePrimary(string s, ref int pos)
    {
        SkipWs(s, ref pos);
        if (pos < s.Length && s[pos] == '(')
        {
            pos++;
            bool val = ParseOr(s, ref pos);
            SkipWs(s, ref pos);
            Check(pos < s.Length && s[pos] == ')', "Missing closing ')'");
            pos++;
            return val;
        }
        Check(pos < s.Length && (s[pos] == '0' || s[pos] == '1'), $"Unexpected char at {pos} in '{s}'");
        return s[pos++] == '1';
    }

    internal static IReadOnlyList<(double Wx, double Wy, double Wz, double WorldWest, double WorldEast, double WorldNorth, double WorldSouth, double WorldSubY, double MaxErr)> GetRp02SnapResults()
    {
        (double Wx, double Wy, double Wz)[] samples =
        [
            (91.5, 70.0, -14.5),
            (0.5, 64.0, 0.5),
            (25.5, 77.0, 132.5),
            (31.5, 37.0, 132.5),
            (-0.5, 64.0, -15.5),
            (-15.5, -58.0, -0.5),
            (-123.3, 120.75, 456.8)
        ];

        var list = new List<(double, double, double, double, double, double, double, double, double)>();
        foreach (var (wx, wy, wz) in samples)
        {
            double cx = Math.Floor(wx / 16.0);
            double cz = Math.Floor(wz / 16.0);
            double cy = Math.Floor(wy / 16.0);

            double posXu = -(wx - Math.Floor(wx / 16.0) * 16.0) * 16.0;
            double posZu = (wz - Math.Floor(wz / 16.0) * 16.0) * 16.0;
            double subYu = -(wy - Math.Floor(wy / 16.0) * 16.0) * 16.0;

            double worldWest = wx + (0.0 + posXu) / 16.0;
            double worldEast = wx + (256.0 + posXu) / 16.0;
            double worldNorth = wz - (0.0 + posZu) / 16.0;
            double worldSouth = wz - (-256.0 + posZu) / 16.0;
            double worldSubY = wy + (0.0 + subYu) / 16.0;

            // player_spawn_sphere_group の1ブロック整数境界スナップ検証 (24m安全圏: ±23ブロック外縁, 44m湧き圏: ±44ブロック外縁)
            double bx = Math.Floor(wx);
            double by = Math.Floor(wy);
            double bz = Math.Floor(wz);
            double sphPosXu = -(wx - (bx + 0.5)) * 16.0;
            double sphPosYu = -(wy - by) * 16.0;
            double sphPosZu = (wz - (bz + 0.5)) * 16.0;

            double safeWest = wx + (-376.0 + sphPosXu) / 16.0;
            double safeEast = wx + (376.0 + sphPosXu) / 16.0;
            double spawnWest = wx + (-712.0 + sphPosXu) / 16.0;
            double spawnEast = wx + (712.0 + sphPosXu) / 16.0;
            double spawnNorth = wz - (712.0 + sphPosZu) / 16.0;
            double spawnSouth = wz - (-712.0 + sphPosZu) / 16.0;
            double sphereFootY = wy + (0.0 + sphPosYu) / 16.0;

            double err = new[]
            {
                Math.Abs(worldWest - cx * 16.0),
                Math.Abs(worldEast - (cx + 1) * 16.0),
                Math.Abs(worldNorth - cz * 16.0),
                Math.Abs(worldSouth - (cz + 1) * 16.0),
                Math.Abs(worldSubY - cy * 16.0),
                Math.Abs(safeWest - (bx - 23.0)),
                Math.Abs(safeEast - (bx + 24.0)),
                Math.Abs(spawnWest - (bx - 44.0)),
                Math.Abs(spawnEast - (bx + 45.0)),
                Math.Abs(spawnNorth - (bz - 44.0)),
                Math.Abs(spawnSouth - (bz + 45.0)),
                Math.Abs(sphereFootY - by)
            }.Max();

            list.Add((wx, wy, wz, worldWest, worldEast, worldNorth, worldSouth, worldSubY, err));
        }
        return list;
    }

    private static void ValidateRp02ChunkSnap()
    {
        foreach (var row in GetRp02SnapResults())
        {
            Check(row.MaxErr < 1e-9, $"RP02 Snap error too large at ({row.Wx}, {row.Wy}, {row.Wz}): {row.MaxErr}");
        }
    }
}
