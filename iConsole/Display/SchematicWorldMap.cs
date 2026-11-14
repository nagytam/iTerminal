namespace iConsole.Display;

/// <summary>
/// A schematic world map drawn in characters: coarse continent outlines in longitude and latitude, sampled at the centre
/// of each character cell. It is for orientation, not for geography: a coast is a few degrees out, and the small islands
/// are left out. The latitude window stops at 75°N and 45°S, where the useful land is.
/// </summary>
public static class SchematicWorldMap
{
    /// <summary>The top row of the map.</summary>
    public const double NorthLatitude = 75;

    /// <summary>The bottom row of the map.</summary>
    public const double SouthLatitude = -45;

    private static readonly (double Longitude, double Latitude)[][] Landmasses =
    [
        // North America
        [(-168, 66), (-162, 70), (-140, 70), (-120, 72), (-95, 73), (-80, 73), (-62, 60), (-55, 52), (-66, 44), (-76, 37),
         (-81, 31), (-80, 25), (-82, 27), (-90, 30), (-97, 27), (-97, 21), (-88, 21), (-88, 16), (-83, 9), (-78, 8),
         (-86, 13), (-105, 20), (-110, 24), (-115, 31), (-120, 34), (-124, 40), (-124, 48), (-130, 55), (-140, 60),
         (-150, 60), (-160, 57), (-166, 60)],
        // Greenland
        [(-55, 60), (-44, 60), (-22, 70), (-20, 75), (-35, 75), (-60, 75), (-72, 75), (-58, 70)],
        // South America
        [(-78, 9), (-72, 12), (-62, 11), (-50, 2), (-35, -5), (-39, -14), (-41, -22), (-48, -27), (-58, -35), (-65, -42),
         (-68, -52), (-74, -52), (-74, -40), (-71, -30), (-70, -18), (-81, -5), (-80, 0), (-78, 8)],
        // Europe and Asia
        [(-9, 37), (-9, 43), (-2, 44), (-1, 47), (-4, 48), (2, 51), (5, 53), (8, 54), (9, 57), (11, 55), (14, 54), (20, 55),
         (22, 59), (29, 60), (23, 64), (26, 66), (18, 64), (14, 62), (10, 59), (5, 59), (5, 62), (14, 67), (22, 70),
         (30, 70), (40, 68), (60, 69), (70, 73), (80, 72), (100, 77), (115, 73), (130, 71), (150, 71), (170, 70),
         (180, 68), (180, 65), (178, 62), (165, 60), (163, 57), (156, 51), (142, 59), (138, 54), (140, 48), (132, 43),
         (129, 36), (126, 35), (129, 40), (127, 38), (125, 40), (122, 40), (118, 38), (122, 31), (120, 25), (110, 21),
         (106, 20), (108, 15), (109, 11), (105, 9), (100, 13), (99, 8), (103, 1), (100, 4), (98, 8), (98, 16), (92, 22),
         (88, 22), (80, 15), (77, 8), (73, 17), (70, 21), (67, 25), (57, 25), (50, 30), (48, 28), (51, 24), (55, 22),
         (59, 22), (52, 16), (43, 13), (39, 21), (35, 28), (34, 31), (36, 36), (28, 37), (26, 40), (23, 37), (20, 40),
         (19, 42), (13, 45), (12, 44), (18, 40), (16, 38), (12, 42), (8, 44), (3, 43), (-1, 37), (-5, 36)],
        // Africa
        [(-17, 21), (-16, 28), (-10, 30), (-6, 36), (10, 37), (11, 33), (20, 31), (32, 31), (34, 28), (38, 18), (43, 12),
         (51, 12), (48, 6), (40, -3), (40, -15), (35, -24), (32, -29), (26, -34), (18, -34), (12, -18), (13, -11), (9, -1),
         (9, 4), (4, 6), (-4, 5), (-8, 4), (-13, 8), (-17, 14)],
        // Great Britain
        [(-5, 50), (1, 51), (1, 53), (-2, 56), (-5, 58), (-6, 55), (-3, 54), (-5, 52)],
        // Japan
        [(130, 32), (135, 34), (140, 35), (141, 41), (140, 41), (137, 37), (132, 35)],
        // Australia
        [(114, -22), (114, -34), (118, -35), (126, -33), (131, -31), (138, -35), (141, -38), (147, -39), (153, -28),
         (150, -23), (145, -15), (142, -11), (141, -17), (136, -15), (131, -12), (127, -14), (122, -18)],
        // Borneo and Sumatra
        [(109, 2), (117, 7), (119, 1), (116, -4), (110, -3)],
        [(95, 5), (106, -6), (104, -5), (97, 2)],
        // Madagascar
        [(44, -25), (50, -16), (49, -12), (44, -17)],
        // New Zealand
        [(172, -35), (178, -38), (174, -41), (167, -46), (170, -44)],
    ];

    private static readonly object CacheLock = new();
    private static readonly Dictionary<(int Width, int Height), bool[,]> LandCache = [];

    /// <summary>Whether the point at <paramref name="longitude"/>, <paramref name="latitude"/> (degrees) is inside one of the outlines.</summary>
    public static bool IsLand(double longitude, double latitude)
    {
        foreach ((double Longitude, double Latitude)[] outline in Landmasses)
        {
            if (InsideOutline(outline, longitude, latitude))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The land of a map of <paramref name="width"/> by <paramref name="height"/> cells, sampled at each cell's centre.
    /// Row 0 is the top (<see cref="NorthLatitude"/>) and column 0 the left edge (180°W). Computed once per size and
    /// returned as a copy, so a caller cannot change the cached map.
    /// </summary>
    public static bool[,] LandCells(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        lock (CacheLock)
        {
            if (LandCache.TryGetValue((width, height), out bool[,]? cached))
            {
                return (bool[,])cached.Clone();
            }

            var land = new bool[height, width];
            for (int row = 0; row < height; row++)
            {
                double latitude = CellCentreLatitude(row, height);
                for (int column = 0; column < width; column++)
                {
                    land[row, column] = IsLand(CellCentreLongitude(column, width), latitude);
                }
            }

            LandCache[(width, height)] = land;
            return (bool[,])land.Clone();
        }
    }

    /// <summary>
    /// The map as <paramref name="height"/> rows of <paramref name="width"/> characters: <c>#</c> land, a space for sea,
    /// <c>|</c> for the meridian of <paramref name="markerLongitude"/> and <c>@</c> for the marker itself, which sits in
    /// the row of <paramref name="markerLatitude"/>.
    /// </summary>
    public static string[] Render(int width, int height, double markerLongitude, double markerLatitude)
    {
        bool[,] land = LandCells(width, height);
        int meridian = ColumnOf(markerLongitude, width);
        int markerRow = RowOf(markerLatitude, height);

        var rows = new string[height];
        for (int row = 0; row < height; row++)
        {
            var chars = new char[width];
            for (int column = 0; column < width; column++)
            {
                chars[column] = land[row, column] ? '#' : ' ';
                if (column == meridian)
                {
                    chars[column] = row == markerRow ? '@' : '|';
                }
            }

            rows[row] = new string(chars);
        }

        return rows;
    }

    /// <summary>The column whose cell holds <paramref name="longitude"/> (degrees east positive).</summary>
    public static int ColumnOf(double longitude, int width) =>
        Math.Clamp((int)Math.Floor((longitude + 180) / 360 * width), 0, width - 1);

    /// <summary>The row whose cell holds <paramref name="latitude"/>, clamped to the map.</summary>
    public static int RowOf(double latitude, int height) =>
        Math.Clamp((int)Math.Floor((NorthLatitude - latitude) / (NorthLatitude - SouthLatitude) * height), 0, height - 1);

    private static double CellCentreLongitude(int column, int width) => -180 + (column + 0.5) * 360 / width;

    private static double CellCentreLatitude(int row, int height) =>
        NorthLatitude - (row + 0.5) * (NorthLatitude - SouthLatitude) / height;

    // Ray casting: a point is inside when a ray to the east crosses the outline an odd number of times.
    private static bool InsideOutline((double Longitude, double Latitude)[] outline, double longitude, double latitude)
    {
        bool inside = false;
        for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
        {
            (double xi, double yi) = outline[i];
            (double xj, double yj) = outline[j];
            if ((yi > latitude) != (yj > latitude) &&
                longitude < (xj - xi) * (latitude - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
