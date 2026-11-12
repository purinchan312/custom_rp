using System.Drawing;

namespace RpBuilder;

/// <summary>
/// RP01 グリフ1行分の定義 (アトラス行番号・描画文字列・送り幅テクセル・文字色)
/// </summary>
public sealed record GlyphSpec(int Row, string Text, int AdvanceTexels, Color Color);

/// <summary>
/// RP01 日本語バイオーム1行分の定義 (アトラス行番号・日本語名・Molang条件式・文字色)
/// </summary>
public sealed record BiomeEntry(int Row, string JapaneseName, string? MolangCondition, Color Color);

/// <summary>
/// RP02 防具立て頭上モードラベル1行分の定義
/// </summary>
public sealed record ModeLabelSpec(int Row, string LabelText, Color TextColor, Color BorderColor);

/// <summary>
/// RP02 パレット内の単色スウォッチ定義
/// </summary>
public sealed record SwatchSpec(string Name, int U, int V, Color Color);

/// <summary>
/// アトラス生成・ジオメトリ生成・アニメーション生成・自動検証・HTMLレポート間で完全共有する定数定義クラス
/// </summary>
public static class RpConstants
{
    // --- RP01: グリフアトラス仕様 (64 x 512, 32行 × 16px) ---
    public const int Rp01GlyphTexW = 64;
    public const int Rp01GlyphTexH = 512;
    public const int Rp01GlyphRows = 32;
    public const int Rp01GlyphRowH = 16;

    // live_player_renderer (size=[6, 6], offset=[177, -5]) + rp01_yaw_cancel scale=(16/15) 校正値:
    // X_center = 177 + 3 = 180.0 UI px, Y_feet = -5 + 6 = +1.0 UI px
    // 1 model unit = 6/16 = 0.375 UI px, 1 texel = 2 model units = 0.75 UI px, 16 texels = 32 units = 12.0 UI px
    // X_root = -468 -> X_ui = 180.0 - 468 * 0.375 = 4.5 UI px (プレート左余白 4.5px)
    // Line1 Y=[-32..0] -> Y_ui=[1.0..13.0] / Line2 Y=[-64..-32] -> Y_ui=[13.0..25.0]
    public const int Rp01UnitsPerTexel = 2;
    public const int Rp01PanelRootX = -468;
    public const int Rp01PanelBaseY = 0;
    public const int Rp01Line1Y0 = -32;
    public const int Rp01Line2Y0 = -64;
    public const int Rp01LineHUnits = Rp01GlyphRowH * Rp01UnitsPerTexel; // 32
    public const int Rp01BiomeVisibleTexW = 160; // 最大日本語バイオーム幅 (~121px) を余裕収容しつつ他画面干渉を遮断

    public static readonly IReadOnlyDictionary<int, GlyphSpec> Rp01GlyphSpecs = new Dictionary<int, GlyphSpec>
    {
        [0]  = new(0,  "0",     7,  Color.FromArgb(255, 255, 255, 255)),
        [1]  = new(1,  "1",     7,  Color.FromArgb(255, 255, 255, 255)),
        [2]  = new(2,  "2",     7,  Color.FromArgb(255, 255, 255, 255)),
        [3]  = new(3,  "3",     7,  Color.FromArgb(255, 255, 255, 255)),
        [4]  = new(4,  "4",     7,  Color.FromArgb(255, 255, 255, 255)),
        [5]  = new(5,  "5",     7,  Color.FromArgb(255, 255, 255, 255)),
        [6]  = new(6,  "6",     7,  Color.FromArgb(255, 255, 255, 255)),
        [7]  = new(7,  "7",     7,  Color.FromArgb(255, 255, 255, 255)),
        [8]  = new(8,  "8",     7,  Color.FromArgb(255, 255, 255, 255)),
        [9]  = new(9,  "9",     7,  Color.FromArgb(255, 255, 255, 255)),
        [10] = new(10, "-",     7,  Color.FromArgb(255, 255, 255, 255)),
        [11] = new(11, ",",     9,  Color.FromArgb(255, 220, 225, 235)), // ',' + 右余白 (', ')
        [12] = new(12, " (",    11, Color.FromArgb(255, 200, 220, 255)), // 左余白 + '('
        [13] = new(13, ")",     6,  Color.FromArgb(255, 200, 220, 255)),
        [16] = new(16, " : 線",   64, Color.FromArgb(255, 255, 235, 110)),
        [17] = new(17, " : 左上", 64, Color.FromArgb(255, 110, 255, 190)),
        [18] = new(18, " : 右上", 64, Color.FromArgb(255, 110, 255, 190)),
        [19] = new(19, " : 左下", 64, Color.FromArgb(255, 110, 255, 190)),
        [20] = new(20, " : 右下", 64, Color.FromArgb(255, 110, 255, 190)),
    };

    // --- RP01: 日本語バイオームアトラス仕様 (256 x 1024, 64行 × 16px) ---
    public const int Rp01BiomeTexW = 256;
    public const int Rp01BiomeTexH = 1024;
    public const int Rp01BiomeRows = 64;
    public const int Rp01BiomeRowH = 16;

    // 1.21.130+ クライアントエンティティ正式対応 Molang クエリ:
    // q.entity_biome_has_any_identifier / q.entity_biome_has_all_tags / q.entity_biome_has_any_tags
    // 優先判定順: 具体的ID・複合タグ → 汎用単一タグ → 0: フォールバック
    public static readonly IReadOnlyList<BiomeEntry> Rp01BiomeEntries =
    [
        new(0,  "その他のバイオーム", null, Color.FromArgb(255, 225, 230, 235)),
        // 1. ジ・エンド系
        new(1,  "ジ・エンド",
            "(q.entity_biome_has_any_identifier('minecraft:the_end') || q.entity_biome_has_any_tags('the_end'))",
            Color.FromArgb(255, 230, 200, 255)),
        // 2. ネザー系
        new(2,  "ソウルサンドの谷",
            "(q.entity_biome_has_any_identifier('minecraft:soulsand_valley') || q.entity_biome_has_any_tags('soulsand_valley'))",
            Color.FromArgb(255, 145, 235, 245)),
        new(3,  "真紅の森",
            "(q.entity_biome_has_any_identifier('minecraft:crimson_forest') || q.entity_biome_has_any_tags('crimson_forest'))",
            Color.FromArgb(255, 255, 145, 145)),
        new(4,  "歪んだ森",
            "(q.entity_biome_has_any_identifier('minecraft:warped_forest') || q.entity_biome_has_any_tags('warped_forest'))",
            Color.FromArgb(255, 110, 240, 215)),
        new(5,  "玄武岩のデルタ",
            "(q.entity_biome_has_any_identifier('minecraft:basalt_deltas') || q.entity_biome_has_any_tags('basalt_deltas'))",
            Color.FromArgb(255, 205, 205, 215)),
        new(6,  "ネザーの荒地",
            "(q.entity_biome_has_any_identifier('minecraft:nether_wastes', 'minecraft:hell') || q.entity_biome_has_any_tags('nether_wastes', 'hell'))",
            Color.FromArgb(255, 255, 180, 140)),
        // 3. 地下バイオーム系
        new(7,  "ディープダーク",
            "(q.entity_biome_has_any_identifier('minecraft:deep_dark') || q.entity_biome_has_any_tags('deep_dark'))",
            Color.FromArgb(255, 120, 210, 230)),
        new(8,  "繁茂した洞窟",
            "(q.entity_biome_has_any_identifier('minecraft:lush_caves') || q.entity_biome_has_any_tags('lush_caves'))",
            Color.FromArgb(255, 165, 255, 150)),
        new(9,  "鍾乳洞",
            "(q.entity_biome_has_any_identifier('minecraft:dripstone_caves') || q.entity_biome_has_any_tags('dripstone_caves'))",
            Color.FromArgb(255, 225, 195, 165)),
        // 4. オーバーワールド主要バイオーム系 (具体的複合タグ・亜種 → 汎用タグ)
        new(10, "ペールガーデン",
            "(q.entity_biome_has_any_identifier('minecraft:pale_garden') || q.entity_biome_has_any_tags('pale_garden'))",
            Color.FromArgb(255, 225, 225, 235)),
        new(11, "桜の林",
            "(q.entity_biome_has_any_identifier('minecraft:cherry_grove') || q.entity_biome_has_any_tags('cherry_grove'))",
            Color.FromArgb(255, 255, 195, 225)),
        new(12, "マングローブの沼地",
            "(q.entity_biome_has_any_identifier('minecraft:mangrove_swamp') || q.entity_biome_has_any_tags('mangrove_swamp'))",
            Color.FromArgb(255, 165, 230, 155)),
        new(13, "キノコ島",
            "(q.entity_biome_has_any_identifier('minecraft:mushroom_island', 'minecraft:mushroom_island_shore', 'minecraft:mushroom_fields') || q.entity_biome_has_any_tags('mushroom_island'))",
            Color.FromArgb(255, 235, 185, 255)),
        new(14, "竹林",
            "(q.entity_biome_has_any_identifier('minecraft:bamboo_jungle', 'minecraft:bamboo_jungle_hills') || q.entity_biome_has_all_tags('bamboo', 'jungle'))",
            Color.FromArgb(255, 165, 250, 125)),
        new(15, "ジャングルの端",
            "(q.entity_biome_has_any_identifier('minecraft:sparse_jungle', 'minecraft:jungle_edge', 'minecraft:jungle_edge_mutated') || q.entity_biome_has_all_tags('jungle', 'edge'))",
            Color.FromArgb(255, 165, 240, 145)),
        new(16, "ジャングル",
            "(q.entity_biome_has_any_identifier('minecraft:jungle', 'minecraft:jungle_hills', 'minecraft:jungle_mutated') || q.entity_biome_has_any_tags('jungle'))",
            Color.FromArgb(255, 145, 240, 135)),
        new(17, "森のある荒野",
            "(q.entity_biome_has_any_identifier('minecraft:wooded_badlands', 'minecraft:mesa_plateau_stone', 'minecraft:mesa_plateau_stone_mutated') || q.entity_biome_has_all_tags('mesa', 'plateau_stone'))",
            Color.FromArgb(255, 245, 190, 135)),
        new(18, "浸食された荒野",
            "(q.entity_biome_has_any_identifier('minecraft:eroded_badlands', 'minecraft:mesa_bryce') || q.entity_biome_has_all_tags('mesa', 'bryce'))",
            Color.FromArgb(255, 255, 175, 125)),
        new(19, "荒野",
            "(q.entity_biome_has_any_identifier('minecraft:badlands', 'minecraft:mesa', 'minecraft:mesa_plateau', 'minecraft:mesa_plateau_mutated') || q.entity_biome_has_any_tags('mesa'))",
            Color.FromArgb(255, 245, 175, 125)),
        new(20, "吹きさらしのサバンナ",
            "(q.entity_biome_has_any_identifier('minecraft:windswept_savanna', 'minecraft:savanna_mutated', 'minecraft:savanna_plateau_mutated') || q.entity_biome_has_all_tags('savanna', 'mutated'))",
            Color.FromArgb(255, 235, 225, 145)),
        new(21, "サバンナの高原",
            "(q.entity_biome_has_any_identifier('minecraft:savanna_plateau') || q.entity_biome_has_all_tags('savanna', 'plateau'))",
            Color.FromArgb(255, 230, 220, 145)),
        new(22, "サバンナ",
            "(q.entity_biome_has_any_identifier('minecraft:savanna') || q.entity_biome_has_any_tags('savanna'))",
            Color.FromArgb(255, 225, 220, 145)),
        new(23, "沼地",
            "(q.entity_biome_has_any_identifier('minecraft:swamp', 'minecraft:swampland', 'minecraft:swampland_mutated') || q.entity_biome_has_any_tags('swamp'))",
            Color.FromArgb(255, 175, 215, 155)),
        new(24, "砂漠",
            "(q.entity_biome_has_any_identifier('minecraft:desert', 'minecraft:desert_hills', 'minecraft:desert_mutated') || q.entity_biome_has_any_tags('desert'))",
            Color.FromArgb(255, 255, 240, 165)),
        new(25, "樹氷",
            "(q.entity_biome_has_any_identifier('minecraft:ice_spikes', 'minecraft:ice_plains_spikes') || q.entity_biome_has_all_tags('ice_plains', 'mutated'))",
            Color.FromArgb(255, 185, 245, 255)),
        new(26, "雪原",
            "(q.entity_biome_has_any_identifier('minecraft:snowy_plains', 'minecraft:ice_plains', 'minecraft:ice_mountains') || q.entity_biome_has_any_tags('ice_plains'))",
            Color.FromArgb(255, 230, 250, 255)),
        new(27, "雪の林",
            "(q.entity_biome_has_any_identifier('minecraft:grove') || q.entity_biome_has_any_tags('grove'))",
            Color.FromArgb(255, 220, 245, 255)),
        new(28, "雪の斜面",
            "(q.entity_biome_has_any_identifier('minecraft:snowy_slopes') || q.entity_biome_has_any_tags('snowy_slopes'))",
            Color.FromArgb(255, 230, 248, 255)),
        new(29, "尖った山頂",
            "(q.entity_biome_has_any_identifier('minecraft:jagged_peaks') || q.entity_biome_has_any_tags('jagged_peaks'))",
            Color.FromArgb(255, 235, 245, 255)),
        new(30, "凍った山頂",
            "(q.entity_biome_has_any_identifier('minecraft:frozen_peaks') || q.entity_biome_has_any_tags('frozen_peaks'))",
            Color.FromArgb(255, 205, 240, 255)),
        new(31, "石だらけの山頂",
            "(q.entity_biome_has_any_identifier('minecraft:stony_peaks') || q.entity_biome_has_any_tags('stony_peaks'))",
            Color.FromArgb(255, 220, 225, 230)),
        new(32, "草地",
            "(q.entity_biome_has_any_identifier('minecraft:meadow') || q.entity_biome_has_any_tags('meadow'))",
            Color.FromArgb(255, 185, 245, 175)),
        new(33, "吹きさらしの森",
            "(q.entity_biome_has_any_identifier('minecraft:windswept_forest', 'minecraft:extreme_hills_plus_trees', 'minecraft:extreme_hills_plus_trees_mutated') || (q.entity_biome_has_any_tags('extreme_hills', 'mountains') && q.entity_biome_has_any_tags('plus_trees')))",
            Color.FromArgb(255, 185, 225, 185)),
        new(34, "吹きさらしの砂利の丘",
            "(q.entity_biome_has_any_identifier('minecraft:windswept_gravelly_hills', 'minecraft:extreme_hills_mutated') || (q.entity_biome_has_any_tags('extreme_hills', 'mountains') && q.entity_biome_has_any_tags('mutated')))",
            Color.FromArgb(255, 210, 215, 220)),
        new(35, "吹きさらしの丘",
            "(q.entity_biome_has_any_identifier('minecraft:windswept_hills', 'minecraft:extreme_hills', 'minecraft:extreme_hills_edge') || q.entity_biome_has_any_tags('extreme_hills', 'mountains'))",
            Color.FromArgb(255, 200, 220, 205)),
        new(36, "暗い森",
            "(q.entity_biome_has_any_identifier('minecraft:dark_forest', 'minecraft:roofed_forest', 'minecraft:dark_forest_hills', 'minecraft:roofed_forest_mutated') || q.entity_biome_has_all_tags('roofed', 'forest'))",
            Color.FromArgb(255, 155, 215, 155)),
        new(37, "花の森",
            "(q.entity_biome_has_any_identifier('minecraft:flower_forest') || q.entity_biome_has_any_tags('flower_forest'))",
            Color.FromArgb(255, 255, 215, 235)),
        new(38, "原生林のシラカバの森",
            "(q.entity_biome_has_any_identifier('minecraft:old_growth_birch_forest', 'minecraft:birch_forest_mutated', 'minecraft:birch_forest_hills_mutated') || q.entity_biome_has_all_tags('birch', 'mutated'))",
            Color.FromArgb(255, 215, 245, 195)),
        new(39, "シラカバの森",
            "(q.entity_biome_has_any_identifier('minecraft:birch_forest', 'minecraft:birch_forest_hills') || q.entity_biome_has_any_tags('birch'))",
            Color.FromArgb(255, 210, 245, 190)),
        new(40, "原生林のタイガ",
            "(q.entity_biome_has_any_identifier('minecraft:old_growth_pine_taiga', 'minecraft:old_growth_spruce_taiga', 'minecraft:mega_taiga', 'minecraft:mega_taiga_hills', 'minecraft:redwood_taiga_mutated', 'minecraft:redwood_taiga_hills_mutated') || q.entity_biome_has_all_tags('mega', 'taiga'))",
            Color.FromArgb(255, 170, 220, 185)),
        new(41, "雪のタイガ",
            "(q.entity_biome_has_any_identifier('minecraft:snowy_taiga', 'minecraft:cold_taiga', 'minecraft:cold_taiga_hills', 'minecraft:cold_taiga_mutated') || q.entity_biome_has_all_tags('taiga', 'cold'))",
            Color.FromArgb(255, 200, 240, 240)),
        new(42, "タイガ",
            "(q.entity_biome_has_any_identifier('minecraft:taiga', 'minecraft:taiga_hills', 'minecraft:taiga_mutated') || q.entity_biome_has_any_tags('taiga'))",
            Color.FromArgb(255, 170, 225, 190)),
        new(43, "森",
            "(q.entity_biome_has_any_identifier('minecraft:forest', 'minecraft:wooded_hills', 'minecraft:forest_hills') || q.entity_biome_has_any_tags('forest'))",
            Color.FromArgb(255, 175, 240, 165)),
        new(44, "ヒマワリ平原",
            "(q.entity_biome_has_any_identifier('minecraft:sunflower_plains') || q.entity_biome_has_any_tags('sunflower_plains') || q.entity_biome_has_all_tags('plains', 'mutated'))",
            Color.FromArgb(255, 255, 245, 155)),
        new(45, "平原",
            "(q.entity_biome_has_any_identifier('minecraft:plains') || q.entity_biome_has_any_tags('plains'))",
            Color.FromArgb(255, 195, 245, 175)),
        // 5. 海・川・海岸系
        new(46, "ぬるい深海",
            "(q.entity_biome_has_any_identifier('minecraft:deep_lukewarm_ocean', 'minecraft:deep_warm_ocean') || (q.entity_biome_has_all_tags('deep', 'ocean') && q.entity_biome_has_any_tags('warm', 'lukewarm')))",
            Color.FromArgb(255, 135, 225, 255)),
        new(47, "凍った深海",
            "(q.entity_biome_has_any_identifier('minecraft:deep_frozen_ocean') || q.entity_biome_has_all_tags('deep', 'frozen', 'ocean'))",
            Color.FromArgb(255, 180, 225, 255)),
        new(48, "冷たい深海",
            "(q.entity_biome_has_any_identifier('minecraft:deep_cold_ocean') || q.entity_biome_has_all_tags('deep', 'cold', 'ocean'))",
            Color.FromArgb(255, 145, 195, 255)),
        new(49, "暖かい海",
            "(q.entity_biome_has_any_identifier('minecraft:warm_ocean') || q.entity_biome_has_all_tags('warm', 'ocean'))",
            Color.FromArgb(255, 135, 245, 255)),
        new(50, "ぬるい海",
            "(q.entity_biome_has_any_identifier('minecraft:lukewarm_ocean') || q.entity_biome_has_all_tags('lukewarm', 'ocean'))",
            Color.FromArgb(255, 145, 230, 255)),
        new(51, "凍った海",
            "(q.entity_biome_has_any_identifier('minecraft:frozen_ocean', 'minecraft:legacy_frozen_ocean') || q.entity_biome_has_all_tags('frozen', 'ocean'))",
            Color.FromArgb(255, 195, 235, 255)),
        new(52, "冷たい海",
            "(q.entity_biome_has_any_identifier('minecraft:cold_ocean') || q.entity_biome_has_all_tags('cold', 'ocean'))",
            Color.FromArgb(255, 155, 205, 255)),
        new(53, "深海",
            "(q.entity_biome_has_any_identifier('minecraft:deep_ocean') || q.entity_biome_has_all_tags('deep', 'ocean'))",
            Color.FromArgb(255, 135, 185, 255)),
        new(54, "海",
            "(q.entity_biome_has_any_identifier('minecraft:ocean') || q.entity_biome_has_any_tags('ocean'))",
            Color.FromArgb(255, 155, 215, 255)),
        new(55, "凍った川",
            "(q.entity_biome_has_any_identifier('minecraft:frozen_river') || q.entity_biome_has_all_tags('frozen', 'river'))",
            Color.FromArgb(255, 200, 240, 255)),
        new(56, "川",
            "(q.entity_biome_has_any_identifier('minecraft:river') || q.entity_biome_has_any_tags('river'))",
            Color.FromArgb(255, 165, 225, 255)),
        new(57, "石だらけの海岸",
            "(q.entity_biome_has_any_identifier('minecraft:stony_shore', 'minecraft:stone_beach') || q.entity_biome_has_any_tags('stone_beach', 'stony_shore'))",
            Color.FromArgb(255, 215, 220, 225)),
        new(58, "雪の砂浜",
            "(q.entity_biome_has_any_identifier('minecraft:snowy_beach', 'minecraft:cold_beach') || q.entity_biome_has_all_tags('beach', 'cold'))",
            Color.FromArgb(255, 235, 245, 255)),
        new(59, "砂浜",
            "(q.entity_biome_has_any_identifier('minecraft:beach') || q.entity_biome_has_any_tags('beach'))",
            Color.FromArgb(255, 255, 240, 185)),
    ];

    // --- RP02: 頭上モードラベルアトラス仕様 (512 x 128, 4行 × 32px) ---
    public const int Rp02LabelTexW = 512;
    public const int Rp02LabelTexH = 128;
    public const int Rp02LabelRows = 4;
    public const int Rp02LabelRowH = 32;

    public static readonly IReadOnlyList<ModeLabelSpec> Rp02ModeLabels =
    [
        new(0, "", Color.Black, Color.Black),
        new(1, "[1] チャンク境界表示", Color.FromArgb(252, 120, 245, 255), Color.FromArgb(252, 0, 210, 255)),
        new(2, "[2] チャンク＋湧き範囲 (24m安全 / 44m湧き)", Color.FromArgb(252, 130, 255, 165), Color.FromArgb(252, 255, 75, 75)),
        new(3, "", Color.Black, Color.Black),
    ];

    // --- RP02: 3Dビジュアライザーパレット仕様 (512 x 512) ---
    public const int Rp02PaletteW = 512;
    public const int Rp02PaletteH = 512;
    public const int Rp02FloorDiameterBlocks = 90; // -45m .. +45m (-720 .. +720 units, ±44.5mの角張り端を完全収容)
    public const int Rp02TexelsPerBlock = 4;       // 90 * 4 = 360x360 対称フルグリッド
    public const int Rp02QuadrantPx = Rp02FloorDiameterBlocks * Rp02TexelsPerBlock; // 360
    public const int Rp02FloorCenterPx = Rp02QuadrantPx / 2; // 180
    public const int Rp02SafeDistSqMax = 24 * 24 - 1;        // 575 (dist < 24.0m: 自然湧き禁止安全領域)
    public const int Rp02SpawnDistSqMax = 44 * 44;           // 1936 (dist <= 44.0m: Sim4 MOB自然スポーン領域)

    public static readonly IReadOnlyDictionary<string, SwatchSpec> Rp02Swatches = new Dictionary<string, SwatchSpec>
    {
        ["chunk_corner"]  = new("chunk_corner",  384, 0,   Color.FromArgb(255, 0, 240, 255)),
        ["chunk_wall"]    = new("chunk_wall",    384, 32,  Color.FromArgb(245, 255, 235, 90)),
        ["subchunk_ring"] = new("subchunk_ring", 384, 64,  Color.FromArgb(225, 185, 235, 255)),
        ["north_marker"]  = new("north_marker",  384, 96,  Color.FromArgb(255, 255, 70, 70)),
        ["safe_24_wire"]  = new("safe_24_wire",  384, 128, Color.FromArgb(252, 45, 255, 130)),
        ["spawn_44_wire"] = new("spawn_44_wire", 384, 160, Color.FromArgb(252, 255, 55, 55)),
        ["safe_24_step"]  = new("safe_24_step",  384, 192, Color.FromArgb(185, 55, 245, 135)),
        ["spawn_44_step"] = new("spawn_44_step", 384, 224, Color.FromArgb(185, 255, 65, 65)),
        ["transparent"]   = new("transparent",   496, 496, Color.FromArgb(0, 0, 0, 0)),
    };
}
