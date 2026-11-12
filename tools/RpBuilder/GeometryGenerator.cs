using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RpBuilder;

/// <summary>
/// .NET 10 標準 System.Text.Json.Nodes を用いて
/// rp01_hud_readout.geo.json および rp02_chunk_visualizer.geo.json を確定出力するクラス
/// </summary>
public static class GeometryGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void GenerateAll(string rpRoot, string projectRoot)
    {
        string modelsDir = Path.Combine(rpRoot, "models", "entity");
        Directory.CreateDirectory(modelsDir);

        JsonObject rp01Doc = BuildRp01HudGeometries();
        string rp01Path = Path.Combine(modelsDir, "rp01_hud_readout.geo.json");
        var prettyOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(rp01Path, rp01Doc.ToJsonString(prettyOptions) + "\n", Utf8NoBom);

        JsonObject rp02Doc = BuildRp02VisualizerGeometries(out int boneCount, out int cubeCount);
        string rp02Path = Path.Combine(modelsDir, "rp02_chunk_visualizer.geo.json");
        var compactOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(rp02Path, rp02Doc.ToJsonString(compactOptions) + "\n", Utf8NoBom);

        // Bedrock は client_entity の先頭/default ジオメトリ (geometry.armor_stand) の visible_bounds のみで
        // エンティティ全体の視錐台カリング (Frustum Culling) を行うため、256x256 拡張版を生成する
        JsonObject armorStandFullGeo = BuildArmorStandBaseGeometry(expandFrustumBounds: true);
        string armorStandGeoPath = Path.Combine(modelsDir, "armor_stand.geo.json");
        File.WriteAllText(armorStandGeoPath, armorStandFullGeo.ToJsonString(prettyOptions) + "\n", Utf8NoBom);

        string hudOnlyModelsDir = Path.Combine(rpRoot, "subpacks", "hud_and_ui_only", "models", "entity");
        Directory.CreateDirectory(hudOnlyModelsDir);
        JsonObject armorStandLiteGeo = BuildArmorStandBaseGeometry(expandFrustumBounds: false);
        string armorStandLiteGeoPath = Path.Combine(hudOnlyModelsDir, "armor_stand.geo.json");
        File.WriteAllText(armorStandLiteGeoPath, armorStandLiteGeo.ToJsonString(prettyOptions) + "\n", Utf8NoBom);

        JsonObject rp01AnimDoc = BuildRp01HudAnimation();
        string rp01AnimPath = Path.Combine(rpRoot, "animations", "rp01_hud_readout.animation.json");
        File.WriteAllText(rp01AnimPath, rp01AnimDoc.ToJsonString(prettyOptions) + "\n", Utf8NoBom);

        string playerEntityPath = Path.Combine(rpRoot, "entity", "player.entity.json");
        SyncPlayerEntityRp01Scripts(playerEntityPath, prettyOptions);

        Console.WriteLine($"[OK] Generated: {Path.GetRelativePath(projectRoot, rp01Path)} ({new FileInfo(rp01Path).Length:N0} bytes)");
        Console.WriteLine($"[OK] Generated: {Path.GetRelativePath(projectRoot, rp01AnimPath)} ({new FileInfo(rp01AnimPath).Length:N0} bytes)");
        Console.WriteLine($"[OK] Synced RP01 pre_animation: {Path.GetRelativePath(projectRoot, playerEntityPath)} ({new FileInfo(playerEntityPath).Length:N0} bytes)");
        Console.WriteLine($"[OK] Generated: {Path.GetRelativePath(projectRoot, armorStandGeoPath)} ({new FileInfo(armorStandGeoPath).Length:N0} bytes, visible_bounds=256x256)");
        Console.WriteLine($"[OK] Generated: {Path.GetRelativePath(projectRoot, rp02Path)} ({new FileInfo(rp02Path).Length:N0} bytes, total bones={boneCount}, total cubes={cubeCount})");
    }

    private static double R4(double val)
    {
        double r = Math.Round(val, 4);
        return Math.Abs(r) < 1e-6 ? 0.0 : r;
    }

    private static JsonArray Vec3(double x, double y, double z) => [R4(x), R4(y), R4(z)];
    private static JsonArray Vec2Int(int u, int v) => [u, v];

    private static JsonObject SwatchWireUv(string swatchName, string? omitAxis = null)
    {
        SwatchSpec sw = RpConstants.Rp02Swatches[swatchName];
        string[] allFaces = ["north", "south", "east", "west", "up", "down"];
        HashSet<string> skip = omitAxis switch
        {
            "x" => ["east", "west"],
            "y" => ["up", "down"],
            "z" => ["north", "south"],
            _   => []
        };

        var uvObj = new JsonObject();
        foreach (string face in allFaces)
        {
            if (skip.Contains(face)) continue;
            uvObj[face] = new JsonObject
            {
                ["uv"] = Vec2Int(sw.U + 4, sw.V + 4),
                ["uv_size"] = Vec2Int(8, 8)
            };
        }
        return uvObj;
    }

    /// <summary>
    /// 両面描画マテリアル (DisableCulling) 向けに直交2面クロスリボン (8頂点/cube) のUVを生成し、
    /// 全360度からの視認性を維持しつつ RenderDragon uint16 頂点上限 (65,535) に対して3倍の余裕を確保する
    /// </summary>
    private static JsonObject SwatchRibbonUv(string swatchName, string axis)
    {
        SwatchSpec sw = RpConstants.Rp02Swatches[swatchName];
        ReadOnlySpan<string> faces = axis switch
        {
            "x" => ["up", "north"],
            "z" => ["up", "east"],
            "y" => ["north", "east"],
            _   => ["up", "north"]
        };

        var uvObj = new JsonObject();
        foreach (string face in faces)
        {
            uvObj[face] = new JsonObject
            {
                ["uv"] = Vec2Int(sw.U + 4, sw.V + 4),
                ["uv_size"] = Vec2Int(8, 8)
            };
        }
        return uvObj;
    }

    private static JsonArray CreateRp01BaseBones() =>
    [
        new JsonObject
        {
            ["name"] = "rp01_yaw_cancel",
            ["pivot"] = Vec3(0, 0, 0)
        },
        new JsonObject
        {
            ["name"] = "rp01_hud_panel_root",
            ["parent"] = "rp01_yaw_cancel",
            ["pivot"] = Vec3(0, RpConstants.Rp01PanelBaseY, 0)
        }
    ];

    private static JsonObject BuildRp01HudGeometries()
    {
        // A. 固定記号・十の位 '1' ボーン群
        (string BoneName, int RowIdx)[] staticSlots =
        [
            ("rx_d1", 1),
            ("sep_comma1", 11),
            ("rz_d1", 1),
            ("sep_open", 12),
            ("cx_sign", 10),
            ("sep_comma2", 11),
            ("cz_sign", 10),
            ("sep_close", 13)
        ];

        JsonArray staticBones = CreateRp01BaseBones();
        foreach (var (boneName, rowIdx) in staticSlots)
        {
            int advTex = RpConstants.Rp01GlyphSpecs[rowIdx].AdvanceTexels;
            int wUnits = advTex * RpConstants.Rp01UnitsPerTexel;
            int vPx = rowIdx * RpConstants.Rp01GlyphRowH;

            staticBones.Add(new JsonObject
            {
                ["name"] = boneName,
                ["parent"] = "rp01_hud_panel_root",
                ["pivot"] = Vec3(0, RpConstants.Rp01Line1Y0, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["origin"] = Vec3(0, RpConstants.Rp01Line1Y0, -1.0),
                        ["size"] = Vec3(wUnits, RpConstants.Rp01LineHUnits, 0),
                        ["uv"] = new JsonObject
                        {
                            ["north"] = new JsonObject
                            {
                                ["uv"] = Vec2Int(0, vPx),
                                ["uv_size"] = Vec2Int(advTex, RpConstants.Rp01GlyphRowH)
                            }
                        }
                    }
                }
            });
        }

        // B. 動的グリフボーン群 (uv_anim 対応)
        string[] dynamicDigitBones =
        [
            "rx_d0", "rz_d0",
            "cx_d4", "cx_d3", "cx_d2", "cx_d1", "cx_d0",
            "cz_d4", "cz_d3", "cz_d2", "cz_d1", "cz_d0"
        ];
        int digitAdv = RpConstants.Rp01GlyphSpecs[0].AdvanceTexels;
        int digitWUnits = digitAdv * RpConstants.Rp01UnitsPerTexel;

        JsonArray dynamicBones = CreateRp01BaseBones();
        foreach (string boneName in dynamicDigitBones)
        {
            dynamicBones.Add(new JsonObject
            {
                ["name"] = boneName,
                ["parent"] = "rp01_hud_panel_root",
                ["pivot"] = Vec3(0, RpConstants.Rp01Line1Y0, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["origin"] = Vec3(0, RpConstants.Rp01Line1Y0, -1.0),
                        ["size"] = Vec3(digitWUnits, RpConstants.Rp01LineHUnits, 0),
                        ["uv"] = new JsonObject
                        {
                            ["north"] = new JsonObject
                            {
                                ["uv"] = Vec2Int(0, 0),
                                ["uv_size"] = Vec2Int(digitAdv, RpConstants.Rp01GlyphTexH)
                            }
                        }
                    }
                }
            });
        }

        int edgeAdv = RpConstants.Rp01GlyphSpecs[16].AdvanceTexels;
        int edgeWUnits = edgeAdv * RpConstants.Rp01UnitsPerTexel;
        dynamicBones.Add(new JsonObject
        {
            ["name"] = "edge_label",
            ["parent"] = "rp01_hud_panel_root",
            ["pivot"] = Vec3(0, RpConstants.Rp01Line1Y0, 0),
            ["cubes"] = new JsonArray
            {
                new JsonObject
                {
                    ["origin"] = Vec3(0, RpConstants.Rp01Line1Y0, -1.0),
                    ["size"] = Vec3(edgeWUnits, RpConstants.Rp01LineHUnits, 0),
                    ["uv"] = new JsonObject
                    {
                        ["north"] = new JsonObject
                        {
                            ["uv"] = Vec2Int(0, 0),
                            ["uv_size"] = Vec2Int(edgeAdv, RpConstants.Rp01GlyphTexH)
                        }
                    }
                }
            }
        });

        // C. 2段目 日本語バイオーム名ボーン
        int biomeWUnits = RpConstants.Rp01BiomeVisibleTexW * RpConstants.Rp01UnitsPerTexel;
        JsonArray biomeBones = CreateRp01BaseBones();
        biomeBones.Add(new JsonObject
        {
            ["name"] = "biome_label",
            ["parent"] = "rp01_hud_panel_root",
            ["pivot"] = Vec3(0, RpConstants.Rp01Line2Y0, 0),
            ["cubes"] = new JsonArray
            {
                new JsonObject
                {
                    ["origin"] = Vec3(0, RpConstants.Rp01Line2Y0, -1.0),
                    ["size"] = Vec3(biomeWUnits, RpConstants.Rp01LineHUnits, 0),
                    ["uv"] = new JsonObject
                    {
                        ["north"] = new JsonObject
                        {
                            ["uv"] = Vec2Int(0, 0),
                            ["uv_size"] = Vec2Int(RpConstants.Rp01BiomeVisibleTexW, RpConstants.Rp01BiomeTexH)
                        }
                    }
                }
            }
        });

        static JsonObject MakeRp01Desc(string id, int texW, int texH) => new()
        {
            ["identifier"] = id,
            ["texture_width"] = texW,
            ["texture_height"] = texH,
            ["visible_bounds_width"] = 160.0,
            ["visible_bounds_height"] = 160.0,
            ["visible_bounds_offset"] = Vec3(0.0, 0.0, 0.0)
        };

        return new JsonObject
        {
            ["format_version"] = "1.12.0",
            ["minecraft:geometry"] = new JsonArray
            {
                new JsonObject { ["description"] = MakeRp01Desc("geometry.rp01_hud_static", RpConstants.Rp01GlyphTexW, RpConstants.Rp01GlyphTexH), ["bones"] = staticBones },
                new JsonObject { ["description"] = MakeRp01Desc("geometry.rp01_hud_readout", RpConstants.Rp01GlyphTexW, RpConstants.Rp01GlyphTexH), ["bones"] = dynamicBones },
                new JsonObject { ["description"] = MakeRp01Desc("geometry.rp01_hud_biome", RpConstants.Rp01BiomeTexW, RpConstants.Rp01BiomeTexH), ["bones"] = biomeBones }
            }
        };
    }

    private static JsonObject BuildRp01HudAnimation()
    {
        (string Bone, string PosVar, object ScaleVal)[] slotDefs =
        [
            ("rx_d1",      "v.rp01_x_rx_d1",   "v.rp01_rx_ge10"),
            ("rx_d0",      "v.rp01_x_rx_d0",   1.0),
            ("sep_comma1", "v.rp01_x_comma1",  1.0),
            ("rz_d1",      "v.rp01_x_rz_d1",   "v.rp01_rz_ge10"),
            ("rz_d0",      "v.rp01_x_rz_d0",   1.0),
            ("sep_open",   "v.rp01_x_open",    1.0),
            ("cx_sign",    "v.rp01_x_cx_sign", "v.rp01_cx_neg"),
            ("cx_d4",      "v.rp01_x_cx_d4",   "v.rp01_cx_s4"),
            ("cx_d3",      "v.rp01_x_cx_d3",   "v.rp01_cx_s3"),
            ("cx_d2",      "v.rp01_x_cx_d2",   "v.rp01_cx_s2"),
            ("cx_d1",      "v.rp01_x_cx_d1",   "v.rp01_cx_s1"),
            ("cx_d0",      "v.rp01_x_cx_d0",   1.0),
            ("sep_comma2", "v.rp01_x_comma2",  1.0),
            ("cz_sign",    "v.rp01_x_cz_sign", "v.rp01_cz_neg"),
            ("cz_d4",      "v.rp01_x_cz_d4",   "v.rp01_cz_s4"),
            ("cz_d3",      "v.rp01_x_cz_d3",   "v.rp01_cz_s3"),
            ("cz_d2",      "v.rp01_x_cz_d2",   "v.rp01_cz_s2"),
            ("cz_d1",      "v.rp01_x_cz_d1",   "v.rp01_cz_s1"),
            ("cz_d0",      "v.rp01_x_cz_d0",   1.0),
            ("sep_close",  "v.rp01_x_close",   1.0),
            ("edge_label", "v.rp01_x_edge",    "v.rp01_edge_vis")
        ];

        var bones = new JsonObject
        {
            ["rp01_yaw_cancel"] = new JsonObject
            {
                ["rotation"] = Vec3(0.0, 0.0, 0.0),
                ["position"] = Vec3(0.0, 0.0, 0.0),
                ["scale"] = "(q.is_in_ui || v.is_paperdoll) ? (16.0 / 15.0) : 0.0"
            },
            ["rp01_hud_panel_root"] = new JsonObject
            {
                ["position"] = Vec3(RpConstants.Rp01PanelRootX, 0.0, 0.0)
            }
        };

        foreach (var (bone, posVar, scaleVal) in slotDefs)
        {
            bones[bone] = new JsonObject
            {
                ["position"] = new JsonArray(posVar, 0.0, 0.0),
                ["scale"] = scaleVal is string s ? JsonValue.Create(s) : JsonValue.Create((double)scaleVal)
            };
        }

        bones["biome_label"] = new JsonObject
        {
            ["position"] = Vec3(0.0, 0.0, 0.0),
            ["scale"] = 1.0
        };

        return new JsonObject
        {
            ["format_version"] = "1.8.0",
            ["animations"] = new JsonObject
            {
                ["animation.rp01.hud_readout"] = new JsonObject
                {
                    ["loop"] = true,
                    ["bones"] = bones
                }
            }
        };
    }

    private static void SyncPlayerEntityRp01Scripts(string playerEntityPath, JsonSerializerOptions options)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(playerEntityPath, Utf8NoBom))!;
        JsonObject scripts = root["minecraft:client_entity"]!["description"]!["scripts"]!.AsObject();

        // バニラ標準 initialize / pre_animation 行のみ残し、RP01 行を確定再生成して追記する
        static bool IsRp01ScriptLine(string line) =>
            line.Contains("rp01_", StringComparison.Ordinal) ||
            line.Contains("v.cursor_x", StringComparison.Ordinal) ||
            line.Contains("v.biome_idx", StringComparison.Ordinal);

        var initArr = scripts["initialize"]!.AsArray();
        var cleanInit = initArr.Select(n => n!.GetValue<string>()).Where(s => !IsRp01ScriptLine(s)).ToList();

        string[] rp01InitVars =
        [
            "v.rp01_in_ui = 0.0;",
            "v.rp01_rx_ge10 = 0.0;", "v.rp01_rx_d0 = 1.0;",
            "v.rp01_rz_ge10 = 0.0;", "v.rp01_rz_d0 = 1.0;",
            "v.rp01_cx_neg = 0.0;",
            "v.rp01_cx_s4 = 0.0;", "v.rp01_cx_s3 = 0.0;", "v.rp01_cx_s2 = 0.0;", "v.rp01_cx_s1 = 0.0;",
            "v.rp01_cx_d4 = 0.0;", "v.rp01_cx_d3 = 0.0;", "v.rp01_cx_d2 = 0.0;", "v.rp01_cx_d1 = 0.0;", "v.rp01_cx_d0 = 0.0;",
            "v.rp01_cz_neg = 0.0;",
            "v.rp01_cz_s4 = 0.0;", "v.rp01_cz_s3 = 0.0;", "v.rp01_cz_s2 = 0.0;", "v.rp01_cz_s1 = 0.0;",
            "v.rp01_cz_d4 = 0.0;", "v.rp01_cz_d3 = 0.0;", "v.rp01_cz_d2 = 0.0;", "v.rp01_cz_d1 = 0.0;", "v.rp01_cz_d0 = 0.0;",
            "v.rp01_edge_row = 0.0;", "v.rp01_edge_vis = 0.0;", "v.rp01_biome_idx = 0.0;",
            "v.cursor_x = 0.0;", "v.biome_idx = 0.0;",
            "v.rp01_x_rx_d1 = 0.0;", "v.rp01_x_rx_d0 = 0.0;", "v.rp01_x_comma1 = 0.0;",
            "v.rp01_x_rz_d1 = 0.0;", "v.rp01_x_rz_d0 = 0.0;", "v.rp01_x_open = 0.0;",
            "v.rp01_x_cx_sign = 0.0;", "v.rp01_x_cx_d4 = 0.0;", "v.rp01_x_cx_d3 = 0.0;", "v.rp01_x_cx_d2 = 0.0;", "v.rp01_x_cx_d1 = 0.0;", "v.rp01_x_cx_d0 = 0.0;",
            "v.rp01_x_comma2 = 0.0;",
            "v.rp01_x_cz_sign = 0.0;", "v.rp01_x_cz_d4 = 0.0;", "v.rp01_x_cz_d3 = 0.0;", "v.rp01_x_cz_d2 = 0.0;", "v.rp01_x_cz_d1 = 0.0;", "v.rp01_x_cz_d0 = 0.0;",
            "v.rp01_x_close = 0.0;", "v.rp01_x_edge = 0.0;"
        ];
        cleanInit.AddRange(rp01InitVars);
        scripts["initialize"] = new JsonArray(cleanInit.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());

        var preArr = scripts["pre_animation"]!.AsArray();
        var cleanPre = preArr.Select(n => n!.GetValue<string>()).Where(s => !IsRp01ScriptLine(s)).ToList();

        double wDig = RpConstants.Rp01GlyphSpecs[0].AdvanceTexels * RpConstants.Rp01UnitsPerTexel;   // 14.0
        double wCom = RpConstants.Rp01GlyphSpecs[11].AdvanceTexels * RpConstants.Rp01UnitsPerTexel;  // 18.0
        double wOpn = RpConstants.Rp01GlyphSpecs[12].AdvanceTexels * RpConstants.Rp01UnitsPerTexel;  // 22.0
        double wCls = RpConstants.Rp01GlyphSpecs[13].AdvanceTexels * RpConstants.Rp01UnitsPerTexel;  // 12.0

        static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

        string[] coordLines =
        [
            "v.rp01_in_ui = (q.is_in_ui || v.is_paperdoll);",
            "v.rp01_bx = v.rp01_in_ui ? math.floor(q.position(0)) : 0.0;",
            "v.rp01_bz = v.rp01_in_ui ? math.floor(q.position(2)) : 0.0;",
            "v.rp01_cx = v.rp01_in_ui ? math.floor(v.rp01_bx / 16.0) : 0.0;",
            "v.rp01_cz = v.rp01_in_ui ? math.floor(v.rp01_bz / 16.0) : 0.0;",
            "v.rp01_rx = v.rp01_in_ui ? ((v.rp01_bx - v.rp01_cx * 16.0) + 1.0) : 1.0;",
            "v.rp01_rz = v.rp01_in_ui ? ((v.rp01_bz - v.rp01_cz * 16.0) + 1.0) : 1.0;",
            "v.rp01_rx_ge10 = (v.rp01_rx >= 10.0) ? 1.0 : 0.0;",
            "v.rp01_rx_d0 = math.mod(v.rp01_rx, 10.0);",
            "v.rp01_rz_ge10 = (v.rp01_rz >= 10.0) ? 1.0 : 0.0;",
            "v.rp01_rz_d0 = math.mod(v.rp01_rz, 10.0);",
            "v.rp01_cx_neg = (v.rp01_cx < 0.0) ? 1.0 : 0.0;",
            "v.rp01_acx = math.min(99999.0, math.abs(v.rp01_cx));",
            "v.rp01_cx_d4 = math.floor(v.rp01_acx / 10000.0);",
            "v.rp01_cx_d3 = math.mod(math.floor(v.rp01_acx / 1000.0), 10.0);",
            "v.rp01_cx_d2 = math.mod(math.floor(v.rp01_acx / 100.0), 10.0);",
            "v.rp01_cx_d1 = math.mod(math.floor(v.rp01_acx / 10.0), 10.0);",
            "v.rp01_cx_d0 = math.mod(v.rp01_acx, 10.0);",
            "v.rp01_cx_s4 = (v.rp01_acx >= 10000.0) ? 1.0 : 0.0;",
            "v.rp01_cx_s3 = (v.rp01_acx >= 1000.0) ? 1.0 : 0.0;",
            "v.rp01_cx_s2 = (v.rp01_acx >= 100.0) ? 1.0 : 0.0;",
            "v.rp01_cx_s1 = (v.rp01_acx >= 10.0) ? 1.0 : 0.0;",
            "v.rp01_cz_neg = (v.rp01_cz < 0.0) ? 1.0 : 0.0;",
            "v.rp01_acz = math.min(99999.0, math.abs(v.rp01_cz));",
            "v.rp01_cz_d4 = math.floor(v.rp01_acz / 10000.0);",
            "v.rp01_cz_d3 = math.mod(math.floor(v.rp01_acz / 1000.0), 10.0);",
            "v.rp01_cz_d2 = math.mod(math.floor(v.rp01_acz / 100.0), 10.0);",
            "v.rp01_cz_d1 = math.mod(math.floor(v.rp01_acz / 10.0), 10.0);",
            "v.rp01_cz_d0 = math.mod(v.rp01_acz, 10.0);",
            "v.rp01_cz_s4 = (v.rp01_acz >= 10000.0) ? 1.0 : 0.0;",
            "v.rp01_cz_s3 = (v.rp01_acz >= 1000.0) ? 1.0 : 0.0;",
            "v.rp01_cz_s2 = (v.rp01_acz >= 100.0) ? 1.0 : 0.0;",
            "v.rp01_cz_s1 = (v.rp01_acz >= 10.0) ? 1.0 : 0.0;",
            "v.rp01_edge_row = (v.rp01_rx == 1.0 && v.rp01_rz == 1.0) ? 17.0 : ((v.rp01_rx == 16.0 && v.rp01_rz == 1.0) ? 18.0 : ((v.rp01_rx == 1.0 && v.rp01_rz == 16.0) ? 19.0 : ((v.rp01_rx == 16.0 && v.rp01_rz == 16.0) ? 20.0 : ((v.rp01_rx == 1.0 || v.rp01_rx == 16.0 || v.rp01_rz == 1.0 || v.rp01_rz == 16.0) ? 16.0 : 0.0))));",
            "v.rp01_edge_vis = (v.rp01_edge_row > 0.0) ? 1.0 : 0.0;",
            "v.cursor_x = 0.0;",
            $"v.rp01_x_rx_d1 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_rx_ge10 * {F1(wDig)};",
            $"v.rp01_x_rx_d0 = v.cursor_x; v.cursor_x = v.cursor_x + {F1(wDig)};",
            $"v.rp01_x_comma1 = v.cursor_x; v.cursor_x = v.cursor_x + {F1(wCom)};",
            $"v.rp01_x_rz_d1 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_rz_ge10 * {F1(wDig)};",
            $"v.rp01_x_rz_d0 = v.cursor_x; v.cursor_x = v.cursor_x + {F1(wDig)};",
            $"v.rp01_x_open = v.cursor_x; v.cursor_x = v.cursor_x + {F1(wOpn)};",
            $"v.rp01_x_cx_sign = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cx_neg * {F1(wDig)};",
            $"v.rp01_x_cx_d4 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cx_s4 * {F1(wDig)};",
            $"v.rp01_x_cx_d3 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cx_s3 * {F1(wDig)};",
            $"v.rp01_x_cx_d2 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cx_s2 * {F1(wDig)};",
            $"v.rp01_x_cx_d1 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cx_s1 * {F1(wDig)};",
            $"v.rp01_x_cx_d0 = v.cursor_x; v.cursor_x = v.cursor_x + {F1(wDig)};",
            $"v.rp01_x_comma2 = v.cursor_x; v.cursor_x = v.cursor_x + {F1(wCom)};",
            $"v.rp01_x_cz_sign = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cz_neg * {F1(wDig)};",
            $"v.rp01_x_cz_d4 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cz_s4 * {F1(wDig)};",
            $"v.rp01_x_cz_d3 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cz_s3 * {F1(wDig)};",
            $"v.rp01_x_cz_d2 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cz_s2 * {F1(wDig)};",
            $"v.rp01_x_cz_d1 = v.cursor_x; v.cursor_x = v.cursor_x + v.rp01_cz_s1 * {F1(wDig)};",
            $"v.rp01_x_cz_d0 = v.cursor_x; v.cursor_x = v.cursor_x + {F1(wDig)};",
            $"v.rp01_x_close = v.cursor_x; v.cursor_x = v.cursor_x + {F1(wCls)};",
            "v.rp01_x_edge = v.cursor_x;"
        ];
        cleanPre.AddRange(coordLines);

        // v.rp01_in_ui == 0 (一人称・三人称・マルチ他者視点のワールド描画時) は v.biome_idx = -1.0 で全59行が即座に短絡スキップされる
        cleanPre.Add("v.biome_idx = v.rp01_in_ui ? 0.0 : -1.0;");
        foreach (BiomeEntry entry in RpConstants.Rp01BiomeEntries)
        {
            if (entry.Row == 0 || entry.MolangCondition is null) continue;
            cleanPre.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"v.biome_idx = (v.biome_idx == 0.0 && {entry.MolangCondition}) ? {entry.Row:F1} : v.biome_idx;"));
        }
        cleanPre.Add("v.rp01_biome_idx = math.max(0.0, v.biome_idx);");

        scripts["pre_animation"] = new JsonArray(cleanPre.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());

        // animate および render_controllers の RP01 発動条件も live_player_renderer (q.is_in_ui) 対応式に確定同期する
        const string uiGate = "(q.is_in_ui || v.is_paperdoll)";
        scripts["animate"] = new JsonArray(
            JsonValue.Create("root"),
            new JsonObject { ["rp01_hud_anim"] = uiGate }
        );

        JsonObject desc = root["minecraft:client_entity"]!["description"]!.AsObject();
        var cleanRcs = desc["render_controllers"]!.AsArray()
            .Where(n => !(n is JsonObject jo && jo.First().Key.StartsWith("controller.render.rp01_", StringComparison.Ordinal)))
            .Select(n => n!.DeepClone())
            .ToList();

        (string RcId, string? ExtraFlag)[] rp01Rcs =
        [
            ("controller.render.rp01_hud_static",  null),
            ("controller.render.rp01_hud_readout", null),
            ("controller.render.rp01_hud_rz_d0",   null),
            ("controller.render.rp01_hud_cx_d4",   "v.rp01_cx_s4"),
            ("controller.render.rp01_hud_cx_d3",   "v.rp01_cx_s3"),
            ("controller.render.rp01_hud_cx_d2",   "v.rp01_cx_s2"),
            ("controller.render.rp01_hud_cx_d1",   "v.rp01_cx_s1"),
            ("controller.render.rp01_hud_cx_d0",   null),
            ("controller.render.rp01_hud_cz_d4",   "v.rp01_cz_s4"),
            ("controller.render.rp01_hud_cz_d3",   "v.rp01_cz_s3"),
            ("controller.render.rp01_hud_cz_d2",   "v.rp01_cz_s2"),
            ("controller.render.rp01_hud_cz_d1",   "v.rp01_cz_s1"),
            ("controller.render.rp01_hud_cz_d0",   null),
            ("controller.render.rp01_hud_edge",    "v.rp01_edge_vis"),
            ("controller.render.rp01_hud_biome",   null)
        ];

        foreach (var (rcId, extra) in rp01Rcs)
        {
            string cond = extra is null ? uiGate : $"{uiGate} && {extra}";
            cleanRcs.Add(new JsonObject { [rcId] = cond });
        }
        desc["render_controllers"] = new JsonArray(cleanRcs.ToArray());

        File.WriteAllText(playerEntityPath, root.ToJsonString(options) + "\n", Utf8NoBom);
    }

    private static JsonObject BuildRp02VisualizerGeometries(out int totalBones, out int totalCubes)
    {
        var wireBones = new JsonArray
        {
            MakeCullAnchorBone(),
            new JsonObject
            {
                ["name"] = "viz_world_aligned_root",
                ["pivot"] = Vec3(0, 0, 0)
            }
        };

        // A. 16x16 チャンク境界ケージ
        var cageCubes = new JsonArray();
        const double pillarT = 1.4;
        const double pillarHalf = pillarT * 0.5;
        foreach (double px in (ReadOnlySpan<double>)[0.0, 256.0])
        {
            foreach (double pz in (ReadOnlySpan<double>)[0.0, -256.0])
            {
                cageCubes.Add(new JsonObject
                {
                    ["origin"] = Vec3(px - pillarHalf, -768.0, pz - pillarHalf),
                    ["size"] = Vec3(pillarT, 1536.0, pillarT),
                    ["uv"] = SwatchWireUv("chunk_corner", "y")
                });
            }
        }

        const double wallT = 0.65;
        const double wallHalf = wallT * 0.5;
        foreach (int stepB in (ReadOnlySpan<int>)[4, 8, 12])
        {
            double offsetU = stepB * 16.0;
            foreach (double pz in (ReadOnlySpan<double>)[0.0, -256.0])
            {
                cageCubes.Add(new JsonObject
                {
                    ["origin"] = Vec3(offsetU - wallHalf, -512.0, pz - wallHalf),
                    ["size"] = Vec3(wallT, 1024.0, wallT),
                    ["uv"] = SwatchWireUv("chunk_wall", "y")
                });
            }
            foreach (double px in (ReadOnlySpan<double>)[0.0, 256.0])
            {
                cageCubes.Add(new JsonObject
                {
                    ["origin"] = Vec3(px - wallHalf, -512.0, -offsetU - wallHalf),
                    ["size"] = Vec3(wallT, 1024.0, wallT),
                    ["uv"] = SwatchWireUv("chunk_wall", "y")
                });
            }
        }

        AppendHorizontalFrameCubes(cageCubes, 0.0, 256.0, -256.0, 0.0, 0.5, 1.3, "chunk_corner");
        AppendNorthMarkerCubes(cageCubes, 22.0);
        AppendNorthMarkerCubes(cageCubes, 266.0);

        wireBones.Add(new JsonObject
        {
            ["name"] = "chunk_cage_group",
            ["parent"] = "viz_world_aligned_root",
            ["pivot"] = Vec3(0, 0, 0),
            ["cubes"] = cageCubes
        });

        // サブチャンク水平枠 (-48 .. +48 blocks)
        var subchunkCubes = new JsonArray();
        for (int subIdx = -3; subIdx <= 3; subIdx++)
        {
            double yU = subIdx * 256.0;
            double ringT = subIdx == 0 ? 1.25 : 0.85;
            string swatch = subIdx == 0 ? "chunk_wall" : "subchunk_ring";
            AppendHorizontalFrameCubes(subchunkCubes, 0.0, 256.0, -256.0, 0.0, yU, ringT, swatch);

            if (subIdx == 0)
            {
                double cu = 8 * 16.0;
                subchunkCubes.Add(new JsonObject
                {
                    ["origin"] = Vec3(cu - 0.35, yU - 0.35, -256.0),
                    ["size"] = Vec3(0.7, 0.7, 256.0),
                    ["uv"] = SwatchWireUv("subchunk_ring", "z")
                });
                subchunkCubes.Add(new JsonObject
                {
                    ["origin"] = Vec3(0.0, yU - 0.35, -cu - 0.35),
                    ["size"] = Vec3(256.0, 0.7, 0.7),
                    ["uv"] = SwatchWireUv("subchunk_ring", "x")
                });
            }
        }

        wireBones.Add(new JsonObject
        {
            ["name"] = "subchunk_rings",
            ["parent"] = "chunk_cage_group",
            ["pivot"] = Vec3(0, 0, 0),
            ["cubes"] = subchunkCubes
        });

        // B-1. 半径24m 安全領域: 1ブロック単位ジグザグ球体 (全段差 dy=-23..+23) + 地球儀回転経線ボーン
        var safeSphereCubes = new JsonArray();
        int maxSafeDy = (int)Math.Floor(Math.Sqrt(RpConstants.Rp02SafeDistSqMax)); // 23
        for (int dy = -maxSafeDy; dy <= maxSafeDy; dy++)
        {
            bool isMajor = dy == 0 || (dy % 6 == 0) || Math.Abs(dy) == maxSafeDy;
            double thick = isMajor ? 1.15 : 0.55;
            string swatch = isMajor ? "safe_24_wire" : "safe_24_step";
            AppendVoxelHorizontalSliceCubes(safeSphereCubes, dy, RpConstants.Rp02SafeDistSqMax, thick, swatch);
        }

        var safeSphereBones = new JsonArray
        {
            MakeCullAnchorBone(),
            new JsonObject { ["name"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject { ["name"] = "player_spawn_sphere_group", ["parent"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject
            {
                ["name"] = "sphere_24_safe",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = safeSphereCubes
            }
        };

        // B-2. 半径44m MOB出現範囲 — RenderDragon uint16 (65,535頂点) 安全マージン確保のため 上/中/下 3分割
        int maxSpawnDy = (int)Math.Floor(Math.Sqrt(RpConstants.Rp02SpawnDistSqMax)); // 44
        var spawnUpperCubes = new JsonArray();
        for (int dy = 13; dy <= maxSpawnDy; dy++)
        {
            bool isMajor = (dy % 8 == 0) || dy == maxSpawnDy;
            double thick = isMajor ? 1.35 : 0.58;
            string swatch = isMajor ? "spawn_44_wire" : "spawn_44_step";
            AppendVoxelHorizontalSliceCubes(spawnUpperCubes, dy, RpConstants.Rp02SpawnDistSqMax, thick, swatch);
        }

        var spawnUpperBones = new JsonArray
        {
            MakeCullAnchorBone(),
            new JsonObject { ["name"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject { ["name"] = "player_spawn_sphere_group", ["parent"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject
            {
                ["name"] = "sphere_44_spawn_upper",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = spawnUpperCubes
            }
        };

        // B-3. 半径44m MOB出現範囲 (中央赤道帯 dy=-12..+12)
        var spawnMidCubes = new JsonArray();
        for (int dy = -12; dy <= 12; dy++)
        {
            bool isMajor = dy == 0 || (dy % 8 == 0);
            double thick = isMajor ? 1.35 : 0.58;
            string swatch = isMajor ? "spawn_44_wire" : "spawn_44_step";
            AppendVoxelHorizontalSliceCubes(spawnMidCubes, dy, RpConstants.Rp02SpawnDistSqMax, thick, swatch);
        }

        var spawnMidBones = new JsonArray
        {
            MakeCullAnchorBone(),
            new JsonObject { ["name"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject { ["name"] = "player_spawn_sphere_group", ["parent"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject
            {
                ["name"] = "sphere_44_spawn_mid",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = spawnMidCubes
            }
        };

        // B-4. 半径44m MOB出現範囲 (下層 dy=-44..-13: 谷・斜面下の全1ブロック段差を完全網羅)
        var spawnLowerCubes = new JsonArray();
        for (int dy = -maxSpawnDy; dy <= -13; dy++)
        {
            bool isMajor = (dy % 8 == 0) || dy == -maxSpawnDy;
            double thick = isMajor ? 1.35 : 0.58;
            string swatch = isMajor ? "spawn_44_wire" : "spawn_44_step";
            AppendVoxelHorizontalSliceCubes(spawnLowerCubes, dy, RpConstants.Rp02SpawnDistSqMax, thick, swatch);
        }

        var spawnLowerBones = new JsonArray
        {
            MakeCullAnchorBone(),
            new JsonObject { ["name"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject { ["name"] = "player_spawn_sphere_group", ["parent"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject
            {
                ["name"] = "sphere_44_spawn_lower",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = spawnLowerCubes
            }
        };

        // B-5. 24m(緑) ＆ 44m(赤) 固定縦経線 ＋ グルグル回転する地球儀ワイヤー経線ボーン群
        var meridianStaticCubes = new JsonArray();
        AppendVoxelVerticalMeridianCubes(meridianStaticCubes, RpConstants.Rp02SafeDistSqMax, 1.1, "safe_24_wire", yzPlane: false);
        AppendVoxelVerticalMeridianCubes(meridianStaticCubes, RpConstants.Rp02SafeDistSqMax, 1.1, "safe_24_wire", yzPlane: true);
        AppendVoxelVerticalMeridianCubes(meridianStaticCubes, RpConstants.Rp02SpawnDistSqMax, 1.2, "spawn_44_wire", yzPlane: false);
        AppendVoxelVerticalMeridianCubes(meridianStaticCubes, RpConstants.Rp02SpawnDistSqMax, 1.2, "spawn_44_wire", yzPlane: true);

        var safeSpinACubes = new JsonArray();
        AppendVoxelVerticalMeridianCubes(safeSpinACubes, RpConstants.Rp02SafeDistSqMax, 1.55, "safe_24_wire", yzPlane: false);
        AppendVoxelVerticalMeridianCubes(safeSpinACubes, RpConstants.Rp02SafeDistSqMax, 1.55, "safe_24_wire", yzPlane: true);

        var safeSpinBCubes = new JsonArray();
        AppendVoxelVerticalMeridianCubes(safeSpinBCubes, RpConstants.Rp02SafeDistSqMax, 1.35, "safe_24_wire", yzPlane: false);
        AppendVoxelVerticalMeridianCubes(safeSpinBCubes, RpConstants.Rp02SafeDistSqMax, 1.35, "safe_24_wire", yzPlane: true);

        var spawnSpinACubes = new JsonArray();
        AppendVoxelVerticalMeridianCubes(spawnSpinACubes, RpConstants.Rp02SpawnDistSqMax, 1.8, "spawn_44_wire", yzPlane: false);
        AppendVoxelVerticalMeridianCubes(spawnSpinACubes, RpConstants.Rp02SpawnDistSqMax, 1.8, "spawn_44_wire", yzPlane: true);

        var spawnSpinBCubes = new JsonArray();
        AppendVoxelVerticalMeridianCubes(spawnSpinBCubes, RpConstants.Rp02SpawnDistSqMax, 1.55, "spawn_44_wire", yzPlane: false);
        AppendVoxelVerticalMeridianCubes(spawnSpinBCubes, RpConstants.Rp02SpawnDistSqMax, 1.55, "spawn_44_wire", yzPlane: true);

        var globeMeridianBones = new JsonArray
        {
            MakeCullAnchorBone(),
            new JsonObject { ["name"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject { ["name"] = "player_spawn_sphere_group", ["parent"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject
            {
                ["name"] = "globe_static_meridians",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = meridianStaticCubes
            },
            new JsonObject
            {
                ["name"] = "globe_spin_24_a",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = safeSpinACubes
            },
            new JsonObject
            {
                ["name"] = "globe_spin_24_b",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = safeSpinBCubes
            },
            new JsonObject
            {
                ["name"] = "globe_spin_44_a",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = spawnSpinACubes
            },
            new JsonObject
            {
                ["name"] = "globe_spin_44_b",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = spawnSpinBCubes
            }
        };

        // C. 足元カラーオーバーレイ水平基準面 (Y = +0.02 blocks = +0.32 units, -45m..+45m = -720..+720 units)
        double halfFloorUnits = (RpConstants.Rp02FloorDiameterBlocks * 16.0) * 0.5; // 720.0
        var floorBones = new JsonArray
        {
            MakeCullAnchorBone(),
            new JsonObject { ["name"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject { ["name"] = "player_spawn_sphere_group", ["parent"] = "viz_world_aligned_root", ["pivot"] = Vec3(0, 0, 0) },
            new JsonObject
            {
                ["name"] = "floor_zones_group",
                ["parent"] = "player_spawn_sphere_group",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["origin"] = Vec3(-halfFloorUnits, 0.32, -halfFloorUnits),
                        ["size"] = Vec3(halfFloorUnits * 2.0, 0.0, halfFloorUnits * 2.0),
                        ["uv"] = new JsonObject
                        {
                            ["up"] = new JsonObject
                            {
                                ["uv"] = Vec2Int(0, 0),
                                ["uv_size"] = Vec2Int(RpConstants.Rp02QuadrantPx, RpConstants.Rp02QuadrantPx)
                            }
                        }
                    }
                }
            }
        };

        // D. 頭上日本語モード表示ネームプレート
        var labelBones = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "mode_label_bone",
                ["pivot"] = Vec3(0, 42, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["origin"] = Vec3(-28.0, 39.0, 0.0),
                        ["size"] = Vec3(56.0, 6.0, 0.0),
                        ["uv"] = new JsonObject
                        {
                            ["north"] = new JsonObject
                            {
                                ["uv"] = Vec2Int(0, 0),
                                ["uv_size"] = Vec2Int(RpConstants.Rp02LabelTexW, RpConstants.Rp02LabelTexH)
                            }
                        }
                    }
                }
            }
        };

        static JsonObject MakeRp02GeoDesc(string id, int texW, int texH, double boundsW, double boundsH, double offY = 0.0) => new()
        {
            ["identifier"] = id,
            ["texture_width"] = texW,
            ["texture_height"] = texH,
            ["visible_bounds_width"] = boundsW,
            ["visible_bounds_height"] = boundsH,
            ["visible_bounds_offset"] = Vec3(0.0, offY, 0.0)
        };

        var geoArray = new JsonArray
        {
            new JsonObject
            {
                ["description"] = MakeRp02GeoDesc("geometry.rp02_chunk_visualizer", RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, 256.0, 256.0),
                ["bones"] = wireBones
            },
            new JsonObject
            {
                ["description"] = MakeRp02GeoDesc("geometry.rp02_sphere_safe", RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, 256.0, 256.0),
                ["bones"] = safeSphereBones
            },
            new JsonObject
            {
                ["description"] = MakeRp02GeoDesc("geometry.rp02_sphere_spawn_upper", RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, 256.0, 256.0),
                ["bones"] = spawnUpperBones
            },
            new JsonObject
            {
                ["description"] = MakeRp02GeoDesc("geometry.rp02_sphere_spawn_mid", RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, 256.0, 256.0),
                ["bones"] = spawnMidBones
            },
            new JsonObject
            {
                ["description"] = MakeRp02GeoDesc("geometry.rp02_sphere_spawn_lower", RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, 256.0, 256.0),
                ["bones"] = spawnLowerBones
            },
            new JsonObject
            {
                ["description"] = MakeRp02GeoDesc("geometry.rp02_globe_meridians", RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, 256.0, 256.0),
                ["bones"] = globeMeridianBones
            },
            new JsonObject
            {
                ["description"] = MakeRp02GeoDesc("geometry.rp02_floor_zones", RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, 256.0, 256.0),
                ["bones"] = floorBones
            },
            new JsonObject
            {
                ["description"] = MakeRp02GeoDesc("geometry.rp02_mode_label", RpConstants.Rp02LabelTexW, RpConstants.Rp02LabelTexH, 256.0, 256.0),
                ["bones"] = labelBones
            }
        };

        totalBones = 0;
        totalCubes = 0;
        foreach (JsonNode? gNode in geoArray)
        {
            JsonArray bones = gNode!["bones"]!.AsArray();
            totalBones += bones.Count;
            foreach (JsonNode? bNode in bones)
            {
                if (bNode!["cubes"] is JsonArray cArr)
                {
                    totalCubes += cArr.Count;
                }
            }
        }

        return new JsonObject
        {
            ["format_version"] = "1.12.0",
            ["minecraft:geometry"] = geoArray
        };
    }

    /// <summary>
    /// バニラ互換のボーン・ピボット・UV構造を100%維持したまま、
    /// エンティティ全体の視錐台カリング枠 (visible_bounds) を 256x256m に拡張した geometry.armor_stand を生成する。
    /// Bedrock は client_entity の先頭/default ジオメトリの visible_bounds のみを参照するため、
    /// 防具立て本体が画面外に出ても 44m 湧き範囲ワイヤーが消えないようにする必須定義。
    /// </summary>
    private static JsonObject BuildArmorStandBaseGeometry(bool expandFrustumBounds)
    {
        var bones = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "baseplate",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject { ["origin"] = Vec3(-6, 0, -6), ["size"] = Vec3(12, 1, 12), ["uv"] = Vec2Int(0, 32) }
                }
            },
            new JsonObject
            {
                ["name"] = "waist",
                ["pivot"] = Vec3(0, 12, 0)
            },
            new JsonObject
            {
                ["name"] = "body",
                ["parent"] = "waist",
                ["pivot"] = Vec3(0, 24, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject { ["origin"] = Vec3(-6, 21, -1.5), ["size"] = Vec3(12, 3, 3), ["uv"] = Vec2Int(0, 26) },
                    new JsonObject { ["origin"] = Vec3(-3, 14, -1),   ["size"] = Vec3(2, 7, 2),   ["uv"] = Vec2Int(16, 0) },
                    new JsonObject { ["origin"] = Vec3(1, 14, -1),    ["size"] = Vec3(2, 7, 2),   ["uv"] = Vec2Int(48, 16) },
                    new JsonObject { ["origin"] = Vec3(-4, 12, -1),   ["size"] = Vec3(8, 2, 2),   ["uv"] = Vec2Int(0, 48) }
                }
            },
            new JsonObject
            {
                ["name"] = "head",
                ["parent"] = "body",
                ["pivot"] = Vec3(0, 24, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject { ["origin"] = Vec3(-1, 24, -1), ["size"] = Vec3(2, 7, 2), ["uv"] = Vec2Int(0, 0) }
                }
            },
            new JsonObject
            {
                ["name"] = "hat",
                ["parent"] = "head",
                ["pivot"] = Vec3(0, 24, 0)
            },
            new JsonObject
            {
                ["name"] = "leftarm",
                ["parent"] = "body",
                ["pivot"] = Vec3(5, 22, 0),
                ["mirror"] = true,
                ["cubes"] = new JsonArray
                {
                    new JsonObject { ["origin"] = Vec3(4, 12, -1), ["size"] = Vec3(2, 12, 2), ["uv"] = Vec2Int(32, 16) }
                }
            },
            new JsonObject
            {
                ["name"] = "leftitem",
                ["parent"] = "leftarm",
                ["pivot"] = Vec3(6, 15, 1)
            },
            new JsonObject
            {
                ["name"] = "leftleg",
                ["parent"] = "body",
                ["pivot"] = Vec3(1.9, 12, 0),
                ["mirror"] = true,
                ["cubes"] = new JsonArray
                {
                    new JsonObject { ["origin"] = Vec3(0.9, 1, -1), ["size"] = Vec3(2, 11, 2), ["uv"] = Vec2Int(40, 16) }
                }
            },
            new JsonObject
            {
                ["name"] = "rightarm",
                ["parent"] = "body",
                ["pivot"] = Vec3(-5, 22, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject { ["origin"] = Vec3(-6, 12, -1), ["size"] = Vec3(2, 12, 2), ["uv"] = Vec2Int(24, 0) }
                }
            },
            new JsonObject
            {
                ["name"] = "rightitem",
                ["parent"] = "rightarm",
                ["pivot"] = Vec3(-6, 15, 1)
            },
            new JsonObject
            {
                ["name"] = "rightleg",
                ["parent"] = "body",
                ["pivot"] = Vec3(-1.9, 12, 0),
                ["cubes"] = new JsonArray
                {
                    new JsonObject { ["origin"] = Vec3(-2.9, 1, -1), ["size"] = Vec3(2, 11, 2), ["uv"] = Vec2Int(8, 0) }
                }
            }
        };

        if (expandFrustumBounds)
        {
            var anchorCubes = new JsonArray();
            const double boundXz = 1600.0;
            const double boundY = 1024.0;
            foreach (double sx in (ReadOnlySpan<double>)[-boundXz, boundXz])
            {
                foreach (double sy in (ReadOnlySpan<double>)[-boundY, boundY])
                {
                    foreach (double sz in (ReadOnlySpan<double>)[-boundXz, boundXz])
                    {
                        // 面積ゼロ (size=[0,0,0]) の縮退頂点によりピクセル描画ゼロのまま頂点AABBのみ ±100m へ拡張
                        anchorCubes.Add(new JsonObject
                        {
                            ["origin"] = Vec3(sx, sy, sz),
                            ["size"] = Vec3(0.0, 0.0, 0.0),
                            ["uv"] = Vec2Int(0, 0)
                        });
                    }
                }
            }

            bones.Add(new JsonObject
            {
                ["name"] = "cull_bounds_anchor",
                ["pivot"] = Vec3(0, 0, 0),
                ["cubes"] = anchorCubes
            });
        }

        return new JsonObject
        {
            ["format_version"] = "1.12.0",
            ["minecraft:geometry"] = new JsonArray
            {
                new JsonObject
                {
                    ["description"] = new JsonObject
                    {
                        ["identifier"] = "geometry.armor_stand",
                        ["texture_width"] = 64,
                        ["texture_height"] = 64,
                        ["visible_bounds_width"] = expandFrustumBounds ? 256.0 : 1.5,
                        ["visible_bounds_height"] = expandFrustumBounds ? 256.0 : 2.5,
                        ["visible_bounds_offset"] = expandFrustumBounds ? Vec3(0.0, 0.0, 0.0) : Vec3(0.0, 1.25, 0.0)
                    },
                    ["bones"] = bones
                }
            }
        };
    }

    private static JsonObject MakeCullAnchorBone()
    {
        var anchorCubes = new JsonArray();
        const double boundXz = 1600.0;
        const double boundY = 1024.0;
        foreach (double sx in (ReadOnlySpan<double>)[-boundXz, boundXz])
        {
            foreach (double sy in (ReadOnlySpan<double>)[-boundY, boundY])
            {
                foreach (double sz in (ReadOnlySpan<double>)[-boundXz, boundXz])
                {
                    anchorCubes.Add(new JsonObject
                    {
                        ["origin"] = Vec3(sx, sy, sz),
                        ["size"] = Vec3(0.01, 0.01, 0.01),
                        ["uv"] = SwatchRibbonUv("transparent", "x")
                    });
                }
            }
        }

        return new JsonObject
        {
            ["name"] = "cull_bounds_anchor",
            ["pivot"] = Vec3(0, 0, 0),
            ["cubes"] = anchorCubes
        };
    }

    private static void AppendHorizontalFrameCubes(
        JsonArray target, double x0, double x1, double z0, double z1, double yCenter, double thickness, string swatch)
    {
        double half = thickness * 0.5;
        double lx = x1 - x0;
        double lz = z1 - z0;

        target.Add(new JsonObject
        {
            ["origin"] = Vec3(x0 - half, yCenter - half, z1 - half),
            ["size"] = Vec3(lx + thickness, thickness, thickness),
            ["uv"] = SwatchWireUv(swatch, "x")
        });
        target.Add(new JsonObject
        {
            ["origin"] = Vec3(x0 - half, yCenter - half, z0 - half),
            ["size"] = Vec3(lx + thickness, thickness, thickness),
            ["uv"] = SwatchWireUv(swatch, "x")
        });
        target.Add(new JsonObject
        {
            ["origin"] = Vec3(x0 - half, yCenter - half, z0 + half),
            ["size"] = Vec3(thickness, thickness, Math.Max(0.01, lz - thickness)),
            ["uv"] = SwatchWireUv(swatch, "z")
        });
        target.Add(new JsonObject
        {
            ["origin"] = Vec3(x1 - half, yCenter - half, z0 + half),
            ["size"] = Vec3(thickness, thickness, Math.Max(0.01, lz - thickness)),
            ["uv"] = SwatchWireUv(swatch, "z")
        });
    }

    private static void AppendNorthMarkerCubes(JsonArray target, double yBase)
    {
        const double cx = 128.0;
        const double cz = 0.0;
        const double t = 1.6;

        target.Add(new JsonObject
        {
            ["origin"] = Vec3(cx - 9.0, yBase, cz - t * 0.5),
            ["size"] = Vec3(2.2, 20.0, t),
            ["uv"] = SwatchWireUv("north_marker")
        });
        target.Add(new JsonObject
        {
            ["origin"] = Vec3(cx + 6.8, yBase, cz - t * 0.5),
            ["size"] = Vec3(2.2, 20.0, t),
            ["uv"] = SwatchWireUv("north_marker")
        });

        const int steps = 7;
        for (int i = 0; i < steps; i++)
        {
            double sx = (cx - 6.8) + i * (13.6 / steps);
            double sy = (yBase + 16.5) - i * (15.5 / (steps - 1));
            target.Add(new JsonObject
            {
                ["origin"] = Vec3(sx, sy, cz - t * 0.5),
                ["size"] = Vec3(2.4, 3.4, t),
                ["uv"] = SwatchWireUv("north_marker")
            });
        }

        double hy = yBase - 4.0;
        target.Add(new JsonObject
        {
            ["origin"] = Vec3(cx - 1.0, hy, cz),
            ["size"] = Vec3(2.0, 1.8, 14.0),
            ["uv"] = SwatchWireUv("north_marker")
        });
        for (int step = 1; step <= 4; step++)
        {
            double span = (5 - step) * 2.2;
            target.Add(new JsonObject
            {
                ["origin"] = Vec3(cx - span, hy, cz + 8.0 + step * 1.8),
                ["size"] = Vec3(span * 2.0, 1.8, 2.0),
                ["uv"] = SwatchWireUv("north_marker")
            });
        }
    }

    /// <summary>
    /// 指定した高さオフセット dy (1ブロック段差単位) における球断面の1ブロック角張りジグザグ外周ワイヤーを生成する。
    /// キューブ回転を一切使用せず純粋な軸平行ビームで構成し、ワールドの 1x1 ブロック境界と 0.000m 誤差で一致させる。
    /// </summary>
    private static void AppendVoxelHorizontalSliceCubes(
        JsonArray target, int dy, int rSqLimit, double thickness, string swatch)
    {
        int rem = rSqLimit - dy * dy;
        if (rem < 0)
        {
            return;
        }

        int maxDx = (int)Math.Floor(Math.Sqrt(rem));
        int widthCount = 2 * maxDx + 1;
        int[] maxDzAtDx = new int[widthCount];
        for (int dx = -maxDx; dx <= maxDx; dx++)
        {
            maxDzAtDx[dx + maxDx] = (int)Math.Floor(Math.Sqrt(rem - dx * dx));
        }

        double yCenter = dy * 16.0 + 0.32; // 各段差のブロック上面 +0.02m (Zファイティング防止)
        double half = thickness * 0.5;

        // 1. X方向ビーム (同一 maxDz を持つ連続 dx 区間をランレングス結合)
        int idx = 0;
        while (idx < widthCount)
        {
            int mVal = maxDzAtDx[idx];
            int startIdx = idx;
            while (idx + 1 < widthCount && maxDzAtDx[idx + 1] == mVal)
            {
                idx++;
            }

            int dxStart = startIdx - maxDx;
            int dxEnd = idx - maxDx;
            double x0 = (dxStart - 0.5) * 16.0;
            double x1 = (dxEnd + 0.5) * 16.0;
            double zNorth = (mVal + 0.5) * 16.0;
            double zSouth = -(mVal + 0.5) * 16.0;

            target.Add(new JsonObject
            {
                ["origin"] = Vec3(x0 - half, yCenter - half, zNorth - half),
                ["size"] = Vec3((x1 - x0) + thickness, thickness, thickness),
                ["uv"] = SwatchRibbonUv(swatch, "x")
            });
            target.Add(new JsonObject
            {
                ["origin"] = Vec3(x0 - half, yCenter - half, zSouth - half),
                ["size"] = Vec3((x1 - x0) + thickness, thickness, thickness),
                ["uv"] = SwatchRibbonUv(swatch, "x")
            });

            idx++;
        }

        // 2. Z方向ビーム (東西両端キャップ + 内側段差エッジ)
        double xWest = (-maxDx - 0.5) * 16.0;
        double zSpanWest = (maxDzAtDx[0] + 0.5) * 16.0;
        target.Add(new JsonObject
        {
            ["origin"] = Vec3(xWest - half, yCenter - half, -zSpanWest - half),
            ["size"] = Vec3(thickness, thickness, zSpanWest * 2.0 + thickness),
            ["uv"] = SwatchRibbonUv(swatch, "z")
        });

        double xEast = (maxDx + 0.5) * 16.0;
        double zSpanEast = (maxDzAtDx[widthCount - 1] + 0.5) * 16.0;
        target.Add(new JsonObject
        {
            ["origin"] = Vec3(xEast - half, yCenter - half, -zSpanEast - half),
            ["size"] = Vec3(thickness, thickness, zSpanEast * 2.0 + thickness),
            ["uv"] = SwatchRibbonUv(swatch, "z")
        });

        for (int i = 1; i < widthCount; i++)
        {
            int mPrev = maxDzAtDx[i - 1];
            int mCurr = maxDzAtDx[i];
            if (mPrev == mCurr)
            {
                continue;
            }

            int dx = i - maxDx;
            double xEdge = (dx - 0.5) * 16.0;
            double zLo = (Math.Min(mPrev, mCurr) + 0.5) * 16.0;
            double zHi = (Math.Max(mPrev, mCurr) + 0.5) * 16.0;
            double span = zHi - zLo;

            target.Add(new JsonObject
            {
                ["origin"] = Vec3(xEdge - half, yCenter - half, zLo - half),
                ["size"] = Vec3(thickness, thickness, span + thickness),
                ["uv"] = SwatchRibbonUv(swatch, "z")
            });
            target.Add(new JsonObject
            {
                ["origin"] = Vec3(xEdge - half, yCenter - half, -zHi - half),
                ["size"] = Vec3(thickness, thickness, span + thickness),
                ["uv"] = SwatchRibbonUv(swatch, "z")
            });
        }
    }

    /// <summary>
    /// 北極〜南極を一周する縦方向の1ブロック階段状経線ワイヤー (XY平面 または YZ平面) を生成する。
    /// 水平ジグザグ輪郭の各 dy 段差頂点と隙間なく直交接続し、ボーンY回転による地球儀アニメーションにも使用する。
    /// </summary>
    private static void AppendVoxelVerticalMeridianCubes(
        JsonArray target, int rSqLimit, double thickness, string swatch, bool yzPlane)
    {
        int maxDy = (int)Math.Floor(Math.Sqrt(rSqLimit));
        int count = 2 * maxDy + 1;
        int[] mAtDy = new int[count];
        for (int dy = -maxDy; dy <= maxDy; dy++)
        {
            mAtDy[dy + maxDy] = (int)Math.Floor(Math.Sqrt(rSqLimit - dy * dy));
        }

        double half = thickness * 0.5;
        string horizAxis = yzPlane ? "z" : "x";

        void AddHorizSegment(double h0, double h1, double yVal)
        {
            double lo = Math.Min(h0, h1);
            double len = Math.Abs(h1 - h0);
            if (yzPlane)
            {
                target.Add(new JsonObject
                {
                    ["origin"] = Vec3(-half, yVal - half, lo - half),
                    ["size"] = Vec3(thickness, thickness, len + thickness),
                    ["uv"] = SwatchRibbonUv(swatch, horizAxis)
                });
            }
            else
            {
                target.Add(new JsonObject
                {
                    ["origin"] = Vec3(lo - half, yVal - half, -half),
                    ["size"] = Vec3(len + thickness, thickness, thickness),
                    ["uv"] = SwatchRibbonUv(swatch, horizAxis)
                });
            }
        }

        void AddVertSegment(double hPos, double y0, double y1)
        {
            double loY = Math.Min(y0, y1);
            double lenY = Math.Abs(y1 - y0);
            if (yzPlane)
            {
                target.Add(new JsonObject
                {
                    ["origin"] = Vec3(-half, loY - half, hPos - half),
                    ["size"] = Vec3(thickness, lenY + thickness, thickness),
                    ["uv"] = SwatchRibbonUv(swatch, "y")
                });
            }
            else
            {
                target.Add(new JsonObject
                {
                    ["origin"] = Vec3(hPos - half, loY - half, -half),
                    ["size"] = Vec3(thickness, lenY + thickness, thickness),
                    ["uv"] = SwatchRibbonUv(swatch, "y")
                });
            }
        }

        // 上下極の水平キャップ
        double hTop = (mAtDy[count - 1] + 0.5) * 16.0;
        double yTop = maxDy * 16.0 + 0.32;
        AddHorizSegment(-hTop, hTop, yTop);

        double hBot = (mAtDy[0] + 0.5) * 16.0;
        double yBot = -maxDy * 16.0 + 0.32;
        AddHorizSegment(-hBot, hBot, yBot);

        // 各 dy -> dy+1 区間の外周階段 (連続する同一半径の縦壁はランレングス結合)
        int intervalCount = 2 * maxDy;
        int[] intervalOuterM = new int[intervalCount];
        for (int k = 0; k < intervalCount; k++)
        {
            int dy = k - maxDy;
            int m0 = mAtDy[k];
            int m1 = mAtDy[k + 1];
            intervalOuterM[k] = dy >= 0 ? m0 : m1;

            if (m0 != m1)
            {
                double hLo = (Math.Min(m0, m1) + 0.5) * 16.0;
                double hHi = (Math.Max(m0, m1) + 0.5) * 16.0;
                double stepY = (dy >= 0 ? (dy + 1) : dy) * 16.0 + 0.32;
                AddHorizSegment(hLo, hHi, stepY);
                AddHorizSegment(-hHi, -hLo, stepY);
            }
        }

        int kIdx = 0;
        while (kIdx < intervalCount)
        {
            int mOuter = intervalOuterM[kIdx];
            int kStart = kIdx;
            while (kIdx + 1 < intervalCount && intervalOuterM[kIdx + 1] == mOuter)
            {
                kIdx++;
            }

            double yStart = (kStart - maxDy) * 16.0 + 0.32;
            double yEnd = (kIdx + 1 - maxDy) * 16.0 + 0.32;
            double hOuter = (mOuter + 0.5) * 16.0;
            AddVertSegment(hOuter, yStart, yEnd);
            AddVertSegment(-hOuter, yStart, yEnd);

            kIdx++;
        }
    }
}
