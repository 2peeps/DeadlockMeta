namespace DeadlockMeta.Core;

/// <summary>Raw BGRA pixels of a screen region. OriginX/Y is where the region sits inside the game client area.</summary>
public sealed class Frame
{
    public int W, H, OriginX, OriginY;
    public byte[] Px = [];
}

/// <summary>Position of the hero grid in game client pixels.</summary>
public record struct GridGeom(double X0, double Y0, double PitchX, double PitchY, double CardW, double CardH)
{
    // Measured on the hero select screen at 1080p; Panorama scales the UI with the window height.
    public const double RefX0 = 76, RefY0 = 154, RefPitchX = 83, RefPitchY = 133, RefCardW = 71, RefCardH = 118;

    public static GridGeom Make(double k, double anchorX, double dx, double dy) =>
        new(anchorX + RefX0 * k + dx, RefY0 * k + dy, RefPitchX * k, RefPitchY * k, RefCardW * k, RefCardH * k);

    public readonly bool CellAt(double x, double y, out int col, out int row)
    {
        col = (int)Math.Floor((x - X0 + (PitchX - CardW) / 2) / PitchX);
        row = (int)Math.Floor((y - Y0 + (PitchY - CardH) / 2) / PitchY);
        return col >= 0 && col < Matcher.Cols && row >= 0 && row < Matcher.Rows;
    }
}

public record struct CellMatch(int HeroId, float Score);

/// <summary>Recognises hero cards on the hero select screen by comparing them with the card art.</summary>
public sealed class Matcher
{
    public const int Cols = 9, Rows = 7;
    const int SigW = 10, SigH = 16, SigLen = SigW * SigH * 3;
    // Part of the card that is compared: skips the frame, the "new" ribbon on top and the lock icon at the bottom.
    const double InL = 0.10, InR = 0.90, InT = 0.17, InB = 0.86;
    // Empty cells score up to ~0.6 against some card, real cards 0.85 and up.
    public const float MinScore = 0.72f;
    public const int MinCards = 8;

    readonly List<int> ids = [];
    readonly List<float[]> sigs = [];
    readonly float[] tmp = new float[SigLen];

    public int TemplateCount => ids.Count;

    public void AddTemplate(int heroId, Frame card)
    {
        var s = new float[SigLen];
        if (Signature(card, 0, 0, card.W, card.H, 1, s)) { ids.Add(heroId); sigs.Add(s); }
    }

    // Box-averaged, mean/variance normalised thumbnail of a card. False when the card is off-frame or flat.
    static bool Signature(Frame f, double x, double y, double w, double h, int step, float[] sig)
    {
        double ax = x - f.OriginX + w * InL, bx = x - f.OriginX + w * InR;
        double ay = y - f.OriginY + h * InT, by = y - f.OriginY + h * InB;
        if (ax < 0 || ay < 0 || bx > f.W || by > f.H) return false;
        double cw = (bx - ax) / SigW, ch = (by - ay) / SigH;
        var px = f.Px;
        int n = 0;
        for (int j = 0; j < SigH; j++)
        {
            int y1 = (int)(ay + j * ch), y2 = Math.Max(y1 + 1, (int)(ay + (j + 1) * ch));
            for (int i = 0; i < SigW; i++)
            {
                int x1 = (int)(ax + i * cw), x2 = Math.Max(x1 + 1, (int)(ax + (i + 1) * cw));
                int sb = 0, sg = 0, sr = 0, c = 0;
                for (int yy = y1; yy < y2; yy += step)
                {
                    int o = (yy * f.W + x1) * 4;
                    for (int xx = x1; xx < x2; xx += step, o += 4 * step)
                    {
                        sb += px[o]; sg += px[o + 1]; sr += px[o + 2]; c++;
                    }
                }
                sig[n++] = (float)sb / c; sig[n++] = (float)sg / c; sig[n++] = (float)sr / c;
            }
        }
        double mean = 0;
        for (int i = 0; i < SigLen; i++) mean += sig[i];
        mean /= SigLen;
        double var = 0;
        for (int i = 0; i < SigLen; i++) { sig[i] -= (float)mean; var += sig[i] * sig[i]; }
        if (var < SigLen * 16) return false;
        float inv = (float)(1 / Math.Sqrt(var));
        for (int i = 0; i < SigLen; i++) sig[i] *= inv;
        return true;
    }

    CellMatch MatchCell(Frame f, GridGeom g, int col, int row, int step)
    {
        var m = new CellMatch(0, 0);
        if (!Signature(f, g.X0 + col * g.PitchX, g.Y0 + row * g.PitchY, g.CardW, g.CardH, step, tmp)) return m;
        for (int t = 0; t < sigs.Count; t++)
        {
            var s = sigs[t];
            float dot = 0;
            for (int i = 0; i < SigLen; i++) dot += s[i] * tmp[i];
            if (dot > m.Score) m = new CellMatch(ids[t], dot);
        }
        return m;
    }

    /// <summary>Matches every cell of the grid; returns how many look like a hero card.</summary>
    public int MatchGrid(Frame f, GridGeom g, int rows, int step, CellMatch[]? cells, out double total)
    {
        int good = 0; total = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                var m = MatchCell(f, g, c, r, step);
                if (cells != null) cells[r * Cols + c] = m;
                if (m.Score >= MinScore) { good++; total += m.Score; }
            }
        return good;
    }

    /// <summary>Cheap check that the grid is where it was last time (first two rows only).</summary>
    public bool Confirm(Frame f, GridGeom g) => MatchGrid(f, g, 2, 2, null, out _) >= MinCards;

    /// <summary>
    /// Looks for the grid on a full client-area frame. The UI is assumed to scale with the window height;
    /// the horizontal anchor and a small offset are searched.
    /// </summary>
    public bool FindGrid(Frame f, int clientW, int clientH, out GridGeom found)
    {
        found = default;
        double k = clientH / 1080.0;
        var anchors = new List<double> { 0 };
        double wide = (clientW - clientH * 16.0 / 9.0) / 2;
        if (Math.Abs(wide) > 2) anchors.Add(wide);

        double best = 0, bestA = 0, bestDx = 0, bestDy = 0;
        foreach (double a in anchors)
            for (int dy = -24; dy <= 24; dy += 6)
                for (int dx = -24; dx <= 24; dx += 6)
                {
                    if (MatchGrid(f, GridGeom.Make(k, a, dx * k, dy * k), 2, 2, null, out double total) < 4) continue;
                    if (total > best) { best = total; bestA = a; bestDx = dx; bestDy = dy; }
                }
        if (best == 0) return false;

        double fineDx = bestDx, fineDy = bestDy;
        for (double dy = bestDy - 4; dy <= bestDy + 4; dy += 1)
            for (double dx = bestDx - 4; dx <= bestDx + 4; dx += 1)
            {
                MatchGrid(f, GridGeom.Make(k, bestA, dx * k, dy * k), 2, 1, null, out double total);
                if (total > best) { best = total; fineDx = dx; fineDy = dy; }
            }
        found = GridGeom.Make(k, bestA, fineDx * k, fineDy * k);
        return MatchGrid(f, found, 2, 1, null, out _) >= MinCards;
    }
}
