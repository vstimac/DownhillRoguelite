using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// The explored ground of the chart, painted once into textures instead of redrawn cell by cell:
/// the world is cut into square tiles of CellsPerTile chart cells, each an image with PixelsPerCell
/// pixels per cell. A newly seen cell is painted into its tile (its height band's tone, and a
/// contour line on any edge where the ground drops into a lower band), together with its four
/// neighbours, whose edges may change. The canvas then draws a handful of textures per frame,
/// whatever the zoom. Unseen ground stays transparent (the paper shows through).
/// </summary>
public sealed class ChartTiles
{
    public ChartTiles(float cellSize, int cellsPerTile, int pixelsPerCell)
    {
        CellSize = cellSize;
        CellsPerTile = System.Math.Max(cellsPerTile, 1);
        PixelsPerCell = System.Math.Max(pixelsPerCell, 1);
    }

    public float CellSize { get; }
    public int CellsPerTile { get; }
    public int PixelsPerCell { get; }
    public Color Light { get; set; } = new(0.78f, 0.74f, 0.6f);
    public Color Dark { get; set; } = new(0.7f, 0.66f, 0.53f);
    public Color ContourInk { get; set; } = new(0.5f, 0.42f, 0.3f, 0.6f);
    /// <summary>Height between contour lines, in m.</summary>
    public float ContourStep { get; set; } = 10.0f;

    /// <summary>Tiles holding painted ground (tests, memory checks).</summary>
    public int TileCount => _tiles.Count;
    /// <summary>Tiles whose texture is behind their image (painted since the last Commit).</summary>
    public int DirtyCount => _dirty.Count;
    /// <summary>Size of one tile in the world, in m.</summary>
    public float TileMetres => CellsPerTile * CellSize;

    private sealed class Tile
    {
        public Tile(int pixels)
        {
            Image = Image.CreateEmpty(pixels, pixels, false, Image.Format.Rgba8);
            Image.Fill(new Color(0.0f, 0.0f, 0.0f, 0.0f));
        }

        public Image Image { get; }
        public ImageTexture? Texture { get; set; }
    }

    private readonly Dictionary<Vector2I, Tile> _tiles = new();
    private readonly HashSet<Vector2I> _dirty = new();

    public Vector2I TileOf(Vector2I cell)
    {
        return new Vector2I(Mathf.FloorToInt((float)cell.X / CellsPerTile), Mathf.FloorToInt((float)cell.Y / CellsPerTile));
    }

    /// <summary>Paints newly seen cells and the neighbours whose contour edges they change.</summary>
    public void Paint(MapChart chart, IReadOnlyList<Vector2I> fresh)
    {
        HashSet<Vector2I> touched = new();
        foreach (Vector2I cell in fresh)
        {
            touched.Add(cell);
            touched.Add(cell + Vector2I.Left);
            touched.Add(cell + Vector2I.Right);
            touched.Add(cell + Vector2I.Up);
            touched.Add(cell + Vector2I.Down);
        }
        foreach (Vector2I cell in touched)
        {
            PaintCell(chart, cell);
        }
    }

    private void PaintCell(MapChart chart, Vector2I cell)
    {
        if (!chart.Seen.TryGetValue(cell, out float height))
        {
            return;
        }
        Vector2I tileKey = TileOf(cell);
        if (!_tiles.TryGetValue(tileKey, out Tile? tile))
        {
            tile = new Tile(CellsPerTile * PixelsPerCell);
            _tiles[tileKey] = tile;
        }
        int band = Band(height);
        Color tone = Light;
        if ((band & 1) == 1)
        {
            tone = Dark;
        }
        int x0 = (cell.X - tileKey.X * CellsPerTile) * PixelsPerCell;
        int y0 = (cell.Y - tileKey.Y * CellsPerTile) * PixelsPerCell;
        tile.Image.FillRect(new Rect2I(x0, y0, PixelsPerCell, PixelsPerCell), tone);
        Color line = tone.Blend(ContourInk);
        int last = PixelsPerCell - 1;
        // A contour on each side where the neighbour lies in a lower band (each line drawn once, on the higher side).
        if (Lower(chart, cell + Vector2I.Left, band))
        {
            tile.Image.FillRect(new Rect2I(x0, y0, 1, PixelsPerCell), line);
        }
        if (Lower(chart, cell + Vector2I.Right, band))
        {
            tile.Image.FillRect(new Rect2I(x0 + last, y0, 1, PixelsPerCell), line);
        }
        if (Lower(chart, cell + Vector2I.Up, band))
        {
            tile.Image.FillRect(new Rect2I(x0, y0, PixelsPerCell, 1), line);
        }
        if (Lower(chart, cell + Vector2I.Down, band))
        {
            tile.Image.FillRect(new Rect2I(x0, y0 + last, PixelsPerCell, 1), line);
        }
        _dirty.Add(tileKey);
    }

    private bool Lower(MapChart chart, Vector2I neighbour, int band)
    {
        return chart.Seen.TryGetValue(neighbour, out float height) && Band(height) < band;
    }

    private int Band(float height)
    {
        return Mathf.FloorToInt(height / Mathf.Max(ContourStep, 0.01f));
    }

    /// <summary>Uploads the tiles painted since the last call (only those).</summary>
    public void Commit()
    {
        foreach (Vector2I key in _dirty)
        {
            if (!_tiles.TryGetValue(key, out Tile? tile))
            {
                continue;
            }
            if (tile.Texture == null)
            {
                tile.Texture = ImageTexture.CreateFromImage(tile.Image);
            }
            else
            {
                tile.Texture.Update(tile.Image);
            }
        }
        _dirty.Clear();
    }

    /// <summary>Draws the tiles that overlap the sheet: one textured rectangle each.</summary>
    public void Draw(CanvasItem canvas, Vector2 centre, float metresPerPixel, Vector2 size)
    {
        Vector2 half = size * 0.5f * metresPerPixel;
        Vector2I low = TileOf(new Vector2I(Mathf.FloorToInt((centre.X - half.X) / CellSize), Mathf.FloorToInt((centre.Y - half.Y) / CellSize)));
        Vector2I high = TileOf(new Vector2I(Mathf.FloorToInt((centre.X + half.X) / CellSize), Mathf.FloorToInt((centre.Y + half.Y) / CellSize)));
        float tilePixels = TileMetres / metresPerPixel;
        for (int tx = low.X; tx <= high.X; tx++)
        {
            for (int tz = low.Y; tz <= high.Y; tz++)
            {
                if (!_tiles.TryGetValue(new Vector2I(tx, tz), out Tile? tile) || tile.Texture == null)
                {
                    continue;
                }
                Vector2 corner = MapCanvas.ToPixel(new Vector2(tx * TileMetres, tz * TileMetres), centre, metresPerPixel, size);
                canvas.DrawTextureRect(tile.Texture, new Rect2(corner, new Vector2(tilePixels, tilePixels)), false);
            }
        }
    }

    /// <summary>Forgets everything (a new mountain).</summary>
    public void Clear()
    {
        _tiles.Clear();
        _dirty.Clear();
    }
}
