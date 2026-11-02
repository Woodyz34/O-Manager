using System.Collections.Generic;
using UnityEngine;

public class GridData
{
    public BoundsData bounds;
    public float grid_size;
    public float[][] base_speed_grid;
    public Dictionary<string, float[][]> slope_factors;
    public List<string> directions;
    public PositionData start;
    public PositionData finish;
    public List<ControlPoint> controls;

    public class BoundsData
    {
        public float minx;
        public float miny;
        public float maxx;
        public float maxy;
    }

    public class PositionData
    {
        public int i;
        public int j;
        public string name; // För start/finish
        public string id;   // För kontrollpunkter kan ha id
    }

    public class ControlPoint
    {
        public int i;
        public int j;
        public string id;
    }
}
