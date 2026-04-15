using UnityEngine;
using System.Collections.Generic;

public class FishSpatialGrid
{
    private readonly float cellSize;
    private readonly Vector3 gridMin;
    private readonly int gridSizeX;
    private readonly int gridSizeY;
    private readonly int gridSizeZ;
    private readonly List<int>[] cells;
    private int lastUpdateFrame = -1;

    public FishSpatialGrid(Vector3 tankCenter, Vector3 tankExtents, float cellSize)
    {
        this.cellSize = Mathf.Max(0.1f, cellSize);
        gridMin = tankCenter - tankExtents;
        Vector3 tankSize = tankExtents * 2f;
        gridSizeX = Mathf.Max(1, Mathf.CeilToInt(tankSize.x / this.cellSize));
        gridSizeY = Mathf.Max(1, Mathf.CeilToInt(tankSize.y / this.cellSize));
        gridSizeZ = Mathf.Max(1, Mathf.CeilToInt(tankSize.z / this.cellSize));

        int totalCells = gridSizeX * gridSizeY * gridSizeZ;
        cells = new List<int>[totalCells];
        for (int i = 0; i < totalCells; i++)
        {
            cells[i] = new List<int>();
        }
    }

    public void UpdateGrid(List<FishMovement> activeFish)
    {
        int frame = Time.frameCount;
        if (frame == lastUpdateFrame) return;
        lastUpdateFrame = frame;

        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].Clear();
        }

        for (int i = 0; i < activeFish.Count; i++)
        {
            if (activeFish[i] == null) continue;
            int cellIndex = PositionToCellIndex(activeFish[i].transform.position);
            cells[cellIndex].Add(i);
        }
    }

    public void QueryRadius(Vector3 position, float radius, List<int> results)
    {
        results.Clear();

        int minCX = Mathf.Clamp(Mathf.FloorToInt((position.x - radius - gridMin.x) / cellSize), 0, gridSizeX - 1);
        int minCY = Mathf.Clamp(Mathf.FloorToInt((position.y - radius - gridMin.y) / cellSize), 0, gridSizeY - 1);
        int minCZ = Mathf.Clamp(Mathf.FloorToInt((position.z - radius - gridMin.z) / cellSize), 0, gridSizeZ - 1);
        int maxCX = Mathf.Clamp(Mathf.FloorToInt((position.x + radius - gridMin.x) / cellSize), 0, gridSizeX - 1);
        int maxCY = Mathf.Clamp(Mathf.FloorToInt((position.y + radius - gridMin.y) / cellSize), 0, gridSizeY - 1);
        int maxCZ = Mathf.Clamp(Mathf.FloorToInt((position.z + radius - gridMin.z) / cellSize), 0, gridSizeZ - 1);

        for (int cy = minCY; cy <= maxCY; cy++)
        {
            int yOffset = cy * gridSizeX;
            for (int cz = minCZ; cz <= maxCZ; cz++)
            {
                int yzOffset = yOffset + (cz * gridSizeX * gridSizeY);
                for (int cx = minCX; cx <= maxCX; cx++)
                {
                    List<int> cell = cells[cx + yzOffset];
                    for (int i = 0; i < cell.Count; i++)
                    {
                        results.Add(cell[i]);
                    }
                }
            }
        }
    }

    private int PositionToCellIndex(Vector3 position)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt((position.x - gridMin.x) / cellSize), 0, gridSizeX - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt((position.y - gridMin.y) / cellSize), 0, gridSizeY - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt((position.z - gridMin.z) / cellSize), 0, gridSizeZ - 1);
        return x + (y * gridSizeX) + (z * gridSizeX * gridSizeY);
    }
}
