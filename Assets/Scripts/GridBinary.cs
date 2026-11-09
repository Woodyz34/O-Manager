using System;
using System.Collections.Generic;
using System.IO;

// Binärt format för grid-datat. JSON-filen är fortfarande originalet, men den tar över en sekund att läsa.
// Editorn bakar därför en .bytes-fil bredvid JSON-filen (se Editor/GridBinaryBaker.cs) som spelet läser i stället.
public static class GridBinary
{
    // Läggs till efter JSON-filens namn, t.ex. output_grid.json -> output_grid_bin.bytes
    public const string Suffix = "_bin";

    private const int Magic = 0x4452474F; // "OGRD"
    private const int Version = 1;

    public static void Write(GridData data, Stream stream)
    {
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Magic);
            writer.Write(Version);

            writer.Write(data.bounds.minx);
            writer.Write(data.bounds.miny);
            writer.Write(data.bounds.maxx);
            writer.Write(data.bounds.maxy);
            writer.Write(data.grid_size);

            WriteGrid(writer, data.base_speed_grid);

            writer.Write(data.slope_factors.Count);
            foreach (var kvp in data.slope_factors)
            {
                writer.Write(kvp.Key);
                WriteGrid(writer, kvp.Value);
            }
        }
    }

    // Fyller i bounds och grid_size i gridData. De stora rutnäten returneras direkt som 2D-arrayer.
    public static bool TryRead(byte[] bytes, out GridData gridData, out float[,] speedGrid, out Dictionary<string, float[,]> slopeFactors)
    {
        gridData = null;
        speedGrid = null;
        slopeFactors = null;
        LastHeights = null;

        try
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Magic)
                {
                    return false;
                }
                int version = reader.ReadInt32();
                if (version == 2)
                {
                    return ReadVersion2(reader, bytes, out gridData, out speedGrid, out slopeFactors);
                }
                if (version != Version)
                {
                    return false;
                }

                var data = new GridData { bounds = new GridData.BoundsData() };
                data.bounds.minx = reader.ReadSingle();
                data.bounds.miny = reader.ReadSingle();
                data.bounds.maxx = reader.ReadSingle();
                data.bounds.maxy = reader.ReadSingle();
                data.grid_size = reader.ReadSingle();

                float[,] baseGrid = ReadGrid(reader, bytes);

                int directionCount = reader.ReadInt32();
                var slopes = new Dictionary<string, float[,]>(directionCount);
                for (int d = 0; d < directionCount; d++)
                {
                    string direction = reader.ReadString();
                    slopes[direction] = ReadGrid(reader, bytes);
                }

                gridData = data;
                speedGrid = baseGrid;
                slopeFactors = slopes;
                return true;
            }
        }
        catch (Exception)
        {
            // Trasig eller avklippt fil: låt anroparen falla tillbaka på JSON
            return false;
        }
    }

    private static void WriteGrid(BinaryWriter writer, float[][] grid)
    {
        int rows = grid.Length;
        int cols = grid[0].Length;
        writer.Write(rows);
        writer.Write(cols);

        byte[] rowBytes = new byte[cols * sizeof(float)];
        for (int i = 0; i < rows; i++)
        {
            Buffer.BlockCopy(grid[i], 0, rowBytes, 0, rowBytes.Length);
            writer.Write(rowBytes);
        }
    }

    private static float[,] ReadGrid(BinaryReader reader, byte[] bytes)
    {
        int rows = reader.ReadInt32();
        int cols = reader.ReadInt32();
        int byteCount = rows * cols * sizeof(float);

        // Kopiera hela rutnätet i ett svep i stället för att läsa ett tal i taget
        float[,] grid = new float[rows, cols];
        int offset = (int)reader.BaseStream.Position;
        Buffer.BlockCopy(bytes, offset, grid, 0, byteCount);
        reader.BaseStream.Position = offset + byteCount;
        return grid;
    }
    // Version 2 is written by the map import (MapImport/MapPackageBuilder.cs): speed and ground height per cell.
    // The uphill/downhill factors are worked out here from the heights with TerrainSpeedModel.
    // Ground height per cell (metres) of the last map package grid read, or null when it has none
    public static float[,] LastHeights;
    static readonly string[] directionNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    static readonly int[] directionX = { 0, 1, 1, 1, 0, -1, -1, -1 };
    static readonly int[] directionY = { 1, 1, 0, -1, -1, -1, 0, 1 };

    private static bool ReadVersion2(BinaryReader reader, byte[] bytes, out GridData gridData, out float[,] speedGrid, out Dictionary<string, float[,]> slopeFactors)
    {
        var data = new GridData { bounds = new GridData.BoundsData() };
        data.bounds.minx = reader.ReadSingle();
        data.bounds.miny = reader.ReadSingle();
        data.bounds.maxx = reader.ReadSingle();
        data.bounds.maxy = reader.ReadSingle();
        data.grid_size = reader.ReadSingle();
        int rows = reader.ReadInt32();
        int cols = reader.ReadInt32();
        int count = rows * cols;
        speedGrid = new float[rows, cols];
        int offset = (int)reader.BaseStream.Position;
        Buffer.BlockCopy(bytes, offset, speedGrid, 0, count * sizeof(float));
        reader.BaseStream.Position = offset + count * sizeof(float);
        bool hasHeights = reader.ReadBoolean();
        float[,] height = new float[rows, cols];
        offset = (int)reader.BaseStream.Position;
        Buffer.BlockCopy(bytes, offset, height, 0, count * sizeof(float));
        // Kept for the forest mistakes (hills, reading the contours, seeing further from high ground)
        LastHeights = hasHeights ? height : null;

        slopeFactors = new Dictionary<string, float[,]>(8);
        for (int d = 0; d < 8; d++)
        {
            float[,] factors = new float[rows, cols];
            float distance = data.grid_size * ((directionX[d] != 0 && directionY[d] != 0) ? 1.41421356f : 1f);
            for (int i = 0; i < rows; i++)
            {
                int ni = i + directionY[d];
                for (int j = 0; j < cols; j++)
                {
                    int nj = j + directionX[d];
                    if (!hasHeights || ni < 0 || ni >= rows || nj < 0 || nj >= cols)
                    {
                        factors[i, j] = 1f;
                        continue;
                    }
                    float gradient = (height[ni, nj] - height[i, j]) / distance;
                    factors[i, j] = TerrainSpeedModel.Factor(gradient, speedGrid[i, j]);
                }
            }
            TerrainSpeedModel.MarkConverted(factors);
            slopeFactors[directionNames[d]] = factors;
        }
        gridData = data;
        return true;
    }
}
