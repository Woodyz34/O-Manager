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

        try
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
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
}
