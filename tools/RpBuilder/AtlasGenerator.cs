using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RpBuilder;

/// <summary>
/// .NET 10 標準 Windows Forms / System.Drawing API と PngWriter を用いて
/// 日本語フォントアトラス・床面グリッドパレット・パックアイコンをピクセル単位で確定生成するクラス
/// </summary>
public static class AtlasGenerator
{
    private static readonly string[] PreferredFontFamilies =
    [
        "MS Gothic",
        "ＭＳ ゴシック",
        "BIZ UDGothic",
        "Yu Gothic UI",
        "Meiryo"
    ];

    public static void GenerateAll(string rpRoot, string projectRoot)
    {
        string customTexDir = Path.Combine(rpRoot, "textures", "rp_custom");
        Directory.CreateDirectory(customTexDir);

        string fontFamily = ResolveInstalledFontFamily();

        string p1 = GenerateRp01GlyphAtlas(customTexDir, fontFamily);
        Report(projectRoot, p1);

        string p2 = GenerateRp01BiomeAtlas(customTexDir, fontFamily);
        Report(projectRoot, p2);

        string p3 = GenerateRp02ModeLabels(customTexDir, fontFamily);
        Report(projectRoot, p3);

        string p4 = GenerateRp02VisualizerPalette(customTexDir);
        Report(projectRoot, p4);

        string p5 = GeneratePackIcon(rpRoot, fontFamily);
        Report(projectRoot, p5);
    }

    private static void Report(string projectRoot, string fullPath)
    {
        long size = new FileInfo(fullPath).Length;
        string rel = Path.GetRelativePath(projectRoot, fullPath);
        Console.WriteLine($"[OK] Generated: {rel} ({size:N0} bytes)");
    }

    private static string ResolveInstalledFontFamily()
    {
        using var collection = new InstalledFontCollection();
        var installed = new HashSet<string>(collection.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in PreferredFontFamilies)
        {
            if (installed.Contains(candidate))
            {
                return candidate;
            }
        }
        return SystemFonts.DefaultFont.FontFamily.Name;
    }

    private static string GenerateRp01GlyphAtlas(string customTexDir, string fontFamily)
    {
        byte[] buf = new byte[RpConstants.Rp01GlyphTexW * RpConstants.Rp01GlyphTexH * 4];
        foreach (var (_, spec) in RpConstants.Rp01GlyphSpecs)
        {
            int dstY = spec.Row * RpConstants.Rp01GlyphRowH;
            bool isSingleSymbol = spec.Row <= 10 || spec.Row == 13;
            DrawShadowedText(
                buf,
                RpConstants.Rp01GlyphTexW,
                RpConstants.Rp01GlyphTexH,
                dstX: 0,
                dstY: dstY,
                boxW: spec.AdvanceTexels,
                boxH: RpConstants.Rp01GlyphRowH,
                text: spec.Text,
                fontFamily: fontFamily,
                fontPx: 12,
                fgColor: spec.Color,
                padX: isSingleSymbol ? 0 : 1,
                padY: 1,
                crisp: true,
                centerX: isSingleSymbol
            );
        }

        string outPath = Path.Combine(customTexDir, "rp01_glyph_atlas.png");
        PngWriter.WriteRgba(outPath, RpConstants.Rp01GlyphTexW, RpConstants.Rp01GlyphTexH, buf);
        return outPath;
    }

    private static string GenerateRp01BiomeAtlas(string customTexDir, string fontFamily)
    {
        byte[] buf = new byte[RpConstants.Rp01BiomeTexW * RpConstants.Rp01BiomeTexH * 4];
        foreach (BiomeEntry entry in RpConstants.Rp01BiomeEntries)
        {
            int dstY = entry.Row * RpConstants.Rp01BiomeRowH;
            DrawShadowedText(
                buf,
                RpConstants.Rp01BiomeTexW,
                RpConstants.Rp01BiomeTexH,
                dstX: 0,
                dstY: dstY,
                boxW: RpConstants.Rp01BiomeTexW,
                boxH: RpConstants.Rp01BiomeRowH,
                text: entry.JapaneseName,
                fontFamily: fontFamily,
                fontPx: 12,
                fgColor: entry.Color,
                padX: 0,
                padY: 1,
                crisp: true,
                centerX: false
            );
        }

        string outPath = Path.Combine(customTexDir, "rp01_biome_atlas.png");
        PngWriter.WriteRgba(outPath, RpConstants.Rp01BiomeTexW, RpConstants.Rp01BiomeTexH, buf);
        return outPath;
    }

    private static string GenerateRp02ModeLabels(string customTexDir, string fontFamily)
    {
        byte[] buf = new byte[RpConstants.Rp02LabelTexW * RpConstants.Rp02LabelTexH * 4];
        foreach (ModeLabelSpec spec in RpConstants.Rp02ModeLabels)
        {
            if (spec.Row == 0 || string.IsNullOrEmpty(spec.LabelText))
            {
                continue;
            }

            int dstY = spec.Row * RpConstants.Rp02LabelRowH;
            FillRect(buf, RpConstants.Rp02LabelTexW, RpConstants.Rp02LabelTexH, 4, dstY + 4, RpConstants.Rp02LabelTexW - 8, 24,
                Color.FromArgb(205, 12, 16, 24));

            Color topBottomBorder = Color.FromArgb(235, spec.BorderColor.R, spec.BorderColor.G, spec.BorderColor.B);
            Color sideBorder = Color.FromArgb(spec.BorderColor.A, spec.BorderColor.R, spec.BorderColor.G, spec.BorderColor.B);

            FillRect(buf, RpConstants.Rp02LabelTexW, RpConstants.Rp02LabelTexH, 4, dstY + 4, RpConstants.Rp02LabelTexW - 8, 1, topBottomBorder);
            FillRect(buf, RpConstants.Rp02LabelTexW, RpConstants.Rp02LabelTexH, 4, dstY + 27, RpConstants.Rp02LabelTexW - 8, 1, topBottomBorder);
            FillRect(buf, RpConstants.Rp02LabelTexW, RpConstants.Rp02LabelTexH, 4, dstY + 4, 2, 24, sideBorder);
            FillRect(buf, RpConstants.Rp02LabelTexW, RpConstants.Rp02LabelTexH, RpConstants.Rp02LabelTexW - 6, dstY + 4, 2, 24, sideBorder);

            DrawShadowedText(
                buf,
                RpConstants.Rp02LabelTexW,
                RpConstants.Rp02LabelTexH,
                dstX: 10,
                dstY: dstY + 6,
                boxW: RpConstants.Rp02LabelTexW - 20,
                boxH: 22,
                text: spec.LabelText,
                fontFamily: fontFamily,
                fontPx: 16,
                fgColor: spec.TextColor,
                padX: 2,
                padY: 1,
                crisp: true,
                centerX: true
            );
        }

        string outPath = Path.Combine(customTexDir, "rp02_mode_labels.png");
        PngWriter.WriteRgba(outPath, RpConstants.Rp02LabelTexW, RpConstants.Rp02LabelTexH, buf);
        return outPath;
    }

    private static string GenerateRp02VisualizerPalette(string customTexDir)
    {
        byte[] buf = new byte[RpConstants.Rp02PaletteW * RpConstants.Rp02PaletteH * 4];

        // A. 左上 360x360: 中心 (180, 180) の1ブロック単位ジグザグ境界グリッド (-45m..+45m, 4 texels/block)
        // 防具立て中心ブロック (dx=0, dz=0) は px/py in [178..181] に厳密対応し、3Dジグザグ球体と100%一致
        int tpb = RpConstants.Rp02TexelsPerBlock;
        int centerZeroStart = RpConstants.Rp02FloorCenterPx - (tpb / 2); // 178

        for (int py = 0; py < RpConstants.Rp02QuadrantPx; py++)
        {
            int dz = (int)Math.Floor((py - (double)centerZeroStart) / tpb);
            int localZ = ((py - centerZeroStart) % tpb + tpb) % tpb;
            bool isGridZ = (localZ == 0);
            int rowBase = py * RpConstants.Rp02PaletteW * 4;

            for (int px = 0; px < RpConstants.Rp02QuadrantPx; px++)
            {
                int dx = (int)Math.Floor((px - (double)centerZeroStart) / tpb);
                int dSq = dx * dx + dz * dz;
                if (dSq > RpConstants.Rp02SpawnDistSqMax)
                {
                    continue;
                }

                int localX = ((px - centerZeroStart) % tpb + tpb) % tpb;
                bool isGrid = isGridZ || (localX == 0);

                // 隣接ブロックがゾーン外かどうかでジグザグ境界エッジ強調を判定
                bool isSafe = dSq <= RpConstants.Rp02SafeDistSqMax;
                bool isBorderEdge = false;
                if (isSafe)
                {
                    if ((localX == 0 && ((dx - 1) * (dx - 1) + dz * dz > RpConstants.Rp02SafeDistSqMax)) ||
                        (localX == tpb - 1 && ((dx + 1) * (dx + 1) + dz * dz > RpConstants.Rp02SafeDistSqMax)) ||
                        (localZ == 0 && (dx * dx + (dz - 1) * (dz - 1) > RpConstants.Rp02SafeDistSqMax)) ||
                        (localZ == tpb - 1 && (dx * dx + (dz + 1) * (dz + 1) > RpConstants.Rp02SafeDistSqMax)))
                    {
                        isBorderEdge = true;
                    }
                }
                else
                {
                    if ((localX == 0 && ((dx - 1) * (dx - 1) + dz * dz > RpConstants.Rp02SpawnDistSqMax)) ||
                        (localX == tpb - 1 && ((dx + 1) * (dx + 1) + dz * dz > RpConstants.Rp02SpawnDistSqMax)) ||
                        (localZ == 0 && (dx * dx + (dz - 1) * (dz - 1) > RpConstants.Rp02SpawnDistSqMax)) ||
                        (localZ == tpb - 1 && (dx * dx + (dz + 1) * (dz + 1) > RpConstants.Rp02SpawnDistSqMax)))
                    {
                        isBorderEdge = true;
                    }
                }

                // 下方の谷・斜面から見上げた際にも視界を塞がない控えめな透過度に設定
                byte r, g, b, a;
                if (isSafe)
                {
                    if (isBorderEdge)
                        (r, g, b, a) = (55, 255, 140, 175);
                    else if (isGrid)
                        (r, g, b, a) = (40, 230, 120, 58);
                    else
                        (r, g, b, a) = (25, 195, 95, 22);
                }
                else
                {
                    if (isBorderEdge)
                        (r, g, b, a) = (255, 60, 60, 185);
                    else if (isGrid)
                        (r, g, b, a) = (245, 55, 55, 66);
                    else
                        (r, g, b, a) = (210, 35, 35, 26);
                }

                int idx = rowBase + px * 4;
                buf[idx] = r;
                buf[idx + 1] = g;
                buf[idx + 2] = b;
                buf[idx + 3] = a;
            }
        }

        // B. 右側カラースウォッチ群
        foreach (var (_, sw) in RpConstants.Rp02Swatches)
        {
            FillRect(buf, RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, sw.U, sw.V, 16, 16, sw.Color);
        }

        string outPath = Path.Combine(customTexDir, "rp02_visualizer_palette.png");
        PngWriter.WriteRgba(outPath, RpConstants.Rp02PaletteW, RpConstants.Rp02PaletteH, buf);
        return outPath;
    }

    private static string GeneratePackIcon(string rpRoot, string fontFamily)
    {
        const int w = 256;
        const int h = 256;
        byte[] buf = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 4;
                byte r = (byte)(14 + (y * 18) / h);
                byte g = (byte)(22 + (y * 28) / h);
                byte b = (byte)(38 + (y * 42) / h);
                if (x % 32 == 0 || y % 32 == 0)
                {
                    r = (byte)Math.Min(255, r + 14);
                    g = (byte)Math.Min(255, g + 26);
                    b = (byte)Math.Min(255, b + 36);
                }
                buf[idx] = r;
                buf[idx + 1] = g;
                buf[idx + 2] = b;
                buf[idx + 3] = 255;
            }
        }

        double cx = 128.0, cy = 128.0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (Math.Abs(d - 96.0) <= 2.2)
                {
                    BlendPixel(buf, w, x, y, 255, 85, 65, 220);
                }
                else if (d < 96.0 && d > 54.0)
                {
                    BlendPixel(buf, w, x, y, 220, 50, 45, 42);
                }
                else if (Math.Abs(d - 54.0) <= 2.2)
                {
                    BlendPixel(buf, w, x, y, 55, 255, 145, 235);
                }
                else if (d < 54.0)
                {
                    BlendPixel(buf, w, x, y, 35, 215, 110, 48);
                }
            }
        }

        Color cyan = Color.FromArgb(245, 0, 240, 255);
        FillRect(buf, w, h, 64, 64, 128, 3, cyan);
        FillRect(buf, w, h, 64, 189, 128, 3, cyan);
        FillRect(buf, w, h, 64, 64, 3, 128, cyan);
        FillRect(buf, w, h, 189, 64, 3, 128, cyan);

        Color border = Color.FromArgb(255, 90, 210, 255);
        FillRect(buf, w, h, 6, 6, w - 12, 3, border);
        FillRect(buf, w, h, 6, h - 9, w - 12, 3, border);
        FillRect(buf, w, h, 6, 6, 3, h - 12, border);
        FillRect(buf, w, h, w - 9, 6, 3, h - 12, border);

        FillRect(buf, w, h, 20, 196, w - 40, 40, Color.FromArgb(215, 10, 14, 22));
        DrawShadowedText(buf, w, h, 24, 202, w - 48, 32, "CHUNK / SPAWN / TRADE", fontFamily, 16,
            Color.FromArgb(255, 255, 245, 155), padX: 2, padY: 4, crisp: true, centerX: true);
        DrawShadowedText(buf, w, h, 24, 18, w - 48, 36, "UTILITY RP", fontFamily, 24,
            Color.FromArgb(255, 130, 245, 255), padX: 2, padY: 2, crisp: false, centerX: true);

        string outPath = Path.Combine(rpRoot, "pack_icon.png");
        PngWriter.WriteRgba(outPath, w, h, buf);
        return outPath;
    }

    private static byte[,] RenderAlphaMask(
        int width,
        int height,
        string text,
        string fontFamily,
        int fontPx,
        int drawX,
        int drawY,
        bool crisp)
    {
        byte[,] mask = new byte[height, width];
        if (string.IsNullOrEmpty(text) || width <= 0 || height <= 0)
        {
            return mask;
        }

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
            if (crisp)
            {
                // MS Gothic の埋め込みビットマップストライクをGDIで1px単位に鮮明描画
                using var font = new Font(fontFamily, fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
                TextRenderer.DrawText(
                    g,
                    text,
                    font,
                    new Point(drawX, drawY),
                    Color.White,
                    Color.Black,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                );
            }
            else
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using var font = new Font(fontFamily, fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                using var sf = new StringFormat(StringFormat.GenericTypographic)
                {
                    FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
                };
                g.DrawString(text, font, Brushes.White, drawX, drawY, sf);
            }
        }

        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = Math.Abs(bd.Stride) * height;
            byte[] raw = new byte[byteCount];
            Marshal.Copy(bd.Scan0, raw, 0, byteCount);
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * Math.Abs(bd.Stride);
                for (int x = 0; x < width; x++)
                {
                    int pxOffset = rowOffset + x * 4;
                    int lum = (raw[pxOffset + 2] + raw[pxOffset + 1] + raw[pxOffset]) / 3;
                    mask[y, x] = crisp ? (lum >= 96 ? (byte)255 : (byte)0) : (byte)Math.Clamp(lum, 0, 255);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        return mask;
    }

    private static void DrawShadowedText(
        byte[] rgba,
        int canvasW,
        int canvasH,
        int dstX,
        int dstY,
        int boxW,
        int boxH,
        string text,
        string fontFamily,
        int fontPx,
        Color fgColor,
        int padX,
        int padY,
        bool crisp,
        bool centerX)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int renderX = padX;
        if (centerX)
        {
            byte[,] probe = RenderAlphaMask(boxW * 2, boxH, text, fontFamily, fontPx, 4, padY, crisp);
            int minX = boxW * 2, maxX = -1;
            for (int ry = 0; ry < boxH; ry++)
            {
                for (int rx = 0; rx < boxW * 2; rx++)
                {
                    if (probe[ry, rx] > 32)
                    {
                        if (rx < minX) minX = rx;
                        if (rx > maxX) maxX = rx;
                    }
                }
            }
            if (maxX >= minX)
            {
                int glyphW = maxX - minX + 1;
                renderX = Math.Max(0, (boxW - glyphW) / 2 - (minX - 4));
            }
        }

        byte[,] mask = RenderAlphaMask(boxW, boxH, text, fontFamily, fontPx, renderX, padY, crisp);

        // 1. +1px ドロップシャドウパス
        for (int ry = 0; ry < boxH; ry++)
        {
            int sy = dstY + ry + 1;
            if (sy < 0 || sy >= canvasH) continue;
            for (int rx = 0; rx < boxW; rx++)
            {
                byte m = mask[ry, rx];
                if (m == 0) continue;
                int sx = dstX + rx + 1;
                if (sx < 0 || sx >= canvasW) continue;
                byte sa = (byte)((m * 230) / 255);
                BlendPixel(rgba, canvasW, sx, sy, 10, 12, 18, sa);
            }
        }

        // 2. 本体カラー文字パス
        for (int ry = 0; ry < boxH; ry++)
        {
            int ty = dstY + ry;
            if (ty < 0 || ty >= canvasH) continue;
            for (int rx = 0; rx < boxW; rx++)
            {
                byte m = mask[ry, rx];
                if (m == 0) continue;
                int tx = dstX + rx;
                if (tx < 0 || tx >= canvasW) continue;
                byte fgAlpha = (byte)((m * fgColor.A) / 255);
                BlendPixel(rgba, canvasW, tx, ty, fgColor.R, fgColor.G, fgColor.B, fgAlpha);
            }
        }
    }

    private static void FillRect(byte[] rgba, int width, int height, int x0, int y0, int w, int h, Color c)
    {
        int x1 = Math.Min(width, Math.Max(0, x0 + w));
        int y1 = Math.Min(height, Math.Max(0, y0 + h));
        int sx = Math.Max(0, x0);
        int sy = Math.Max(0, y0);

        for (int y = sy; y < y1; y++)
        {
            int rowBase = y * width * 4;
            for (int x = sx; x < x1; x++)
            {
                int idx = rowBase + x * 4;
                rgba[idx] = c.R;
                rgba[idx + 1] = c.G;
                rgba[idx + 2] = c.B;
                rgba[idx + 3] = c.A;
            }
        }
    }

    private static void BlendPixel(byte[] rgba, int width, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (a == 0) return;
        int idx = (y * width + x) * 4;
        byte dstA = rgba[idx + 3];
        if (dstA == 0 || a == 255)
        {
            rgba[idx] = r;
            rgba[idx + 1] = g;
            rgba[idx + 2] = b;
            rgba[idx + 3] = a;
            return;
        }

        int invA = 255 - a;
        int outA = a + (dstA * invA) / 255;
        if (outA > 0)
        {
            rgba[idx]     = (byte)((r * a + (rgba[idx]     * dstA * invA) / 255) / outA);
            rgba[idx + 1] = (byte)((g * a + (rgba[idx + 1] * dstA * invA) / 255) / outA);
            rgba[idx + 2] = (byte)((b * a + (rgba[idx + 2] * dstA * invA) / 255) / outA);
            rgba[idx + 3] = (byte)Math.Min(255, outA);
        }
    }
}
