using UnityEngine;

public class SmokeSimulator : MonoBehaviour
{
    [Header("Volume Around Source")]
    public Vector3Int gridSize = new Vector3Int(15, 10, 25);
    public float voxelSize = 0.6f;
    public Vector3 sourceOffset = Vector3.zero;

    [Header("Simulation")]
    public float tickRate = 0.05f; // 20 frames por segundo para a simulacao fluida
    public float injectionPerTick = 0.8f;
    public float upwardFlowRate = 0.5f;
    public float lateralFlowFactor = 0.25f;
    public float dissipationPerTick = 0.01f;
    public float blockedRefreshRate = 0.25f;
    private float timer;
    private float blockedRefreshTimer;

    [Header("Rendering (Assign in Inspector)")]
    public Mesh voxelMesh;
    public Material voxelMaterial;

    [Header("Environment Interaction")]
    public LayerMask obstacleMask = ~0;
    [Range(0.05f, 0.95f)] public float occupancyProbeFactor = 0.45f;
    public float flowBlockRayPadding = 0.01f;

    private float[] densityGrid;
    private float[] nextDensityGrid;
    private bool[] blockedGrid;
    private int totalNodes;
    private Vector3 localMin;
    private Vector3 halfProbeExtents;

    // GPU Instancing Batches (Unity limite: 1023 por chamada)
    private Matrix4x4[][] batches;
    private int[] batchCounts;

    void Start()
    {
        totalNodes = gridSize.x * gridSize.y * gridSize.z;
        densityGrid = new float[totalNodes];
        nextDensityGrid = new float[totalNodes];
        blockedGrid = new bool[totalNodes];
        localMin = new Vector3(-gridSize.x * 0.5f * voxelSize, 0f, -gridSize.z * 0.5f * voxelSize);
        halfProbeExtents = Vector3.one * (voxelSize * occupancyProbeFactor);

        // Preparar os lotes de instanciamento para rendering
        int numBatches = Mathf.CeilToInt((float)totalNodes / 1023);
        batches = new Matrix4x4[numBatches][];
        batchCounts = new int[numBatches];
        for (int i = 0; i < numBatches; i++) batches[i] = new Matrix4x4[1023];

        RefreshBlockedGrid();
    }

    int GetIndex(int x, int y, int z)
    {
        if (x < 0 || x >= gridSize.x || y < 0 || y >= gridSize.y || z < 0 || z >= gridSize.z) return -1;
        return x + (y * gridSize.x) + (z * gridSize.x * gridSize.y);
    }

    Vector3 GetCellCenterWorld(int x, int y, int z)
    {
        Vector3 local = localMin + new Vector3((x + 0.5f) * voxelSize, (y + 0.5f) * voxelSize, (z + 0.5f) * voxelSize);
        return transform.position + local;
    }

    int WorldToCellIndex(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position - localMin;
        int x = Mathf.FloorToInt(local.x / voxelSize);
        int y = Mathf.FloorToInt(local.y / voxelSize);
        int z = Mathf.FloorToInt(local.z / voxelSize);
        return GetIndex(x, y, z);
    }

    bool IsFlowBlocked(int fromIdx, int toIdx)
    {
        if (toIdx < 0 || blockedGrid[toIdx]) return true;

        Vector3 from = IndexToWorld(fromIdx);
        Vector3 to = IndexToWorld(toIdx);
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= Mathf.Epsilon) return false;

        return Physics.Raycast(from, delta / dist, dist - flowBlockRayPadding, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    Vector3 IndexToWorld(int idx)
    {
        int cellsPerLayer = gridSize.x * gridSize.y;
        int z = idx / cellsPerLayer;
        int rem = idx - z * cellsPerLayer;
        int y = rem / gridSize.x;
        int x = rem - y * gridSize.x;
        return GetCellCenterWorld(x, y, z);
    }

    void RefreshBlockedGrid()
    {
        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int z = 0; z < gridSize.z; z++)
                {
                    int idx = GetIndex(x, y, z);
                    Vector3 center = GetCellCenterWorld(x, y, z);
                    blockedGrid[idx] = Physics.CheckBox(center, halfProbeExtents, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
                }
            }
        }
    }

    void Update()
    {
        timer += Time.deltaTime;
        blockedRefreshTimer += Time.deltaTime;
        if (blockedRefreshTimer >= blockedRefreshRate)
        {
            blockedRefreshTimer = 0f;
            RefreshBlockedGrid();
        }

        if (timer >= tickRate)
        {
            timer = 0f;
            Simulate();
        }
        RenderSmoke();
    }

    void Simulate()
    {
        // 1. Injetar fumo na posicao da fonte (objeto no mundo)
        int fireIdx = WorldToCellIndex(transform.position + sourceOffset);
        if (fireIdx != -1 && !blockedGrid[fireIdx])
        {
            densityGrid[fireIdx] = Mathf.Min(1.0f, densityGrid[fireIdx] + injectionPerTick);
        }

        System.Array.Copy(densityGrid, nextDensityGrid, totalNodes);

        // 2. Automato Celular (Gravidade Invertida)
        for (int y = gridSize.y - 1; y >= 0; y--)
        {
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int z = 0; z < gridSize.z; z++)
                {
                    int idx = GetIndex(x, y, z);
                    if (blockedGrid[idx])
                    {
                        nextDensityGrid[idx] = 0f;
                        continue;
                    }

                    float currentDensity = densityGrid[idx];
                    if (currentDensity <= 0.01f) continue;

                    // REGRA 1: Subir (Pluma)
                    if (y < gridSize.y - 1)
                    {
                        int upIdx = GetIndex(x, y + 1, z);
                        if (!IsFlowBlocked(idx, upIdx))
                        {
                            float spaceUp = 1.0f - nextDensityGrid[upIdx];
                            if (spaceUp > 0)
                            {
                                float flow = Mathf.Min(currentDensity, spaceUp, upwardFlowRate);
                                nextDensityGrid[idx] -= flow;
                                nextDensityGrid[upIdx] += flow;
                                currentDensity -= flow;
                            }
                        }
                    }

                    // REGRA 2: Espalhar (Jato de Teto / Estratificacao)
                    if (currentDensity > 0.05f)
                    {
                        int[] neighbors = {
                            GetIndex(x + 1, y, z), GetIndex(x - 1, y, z),
                            GetIndex(x, y, z + 1), GetIndex(x, y, z - 1)
                        };

                        foreach (int nIdx in neighbors)
                        {
                            if (nIdx == -1) continue;
                            if (currentDensity <= 0.01f) break;
                            if (IsFlowBlocked(idx, nIdx)) continue;

                            if (nextDensityGrid[nIdx] < densityGrid[idx])
                            {
                                float diff = densityGrid[idx] - nextDensityGrid[nIdx];
                                float flow = Mathf.Min(currentDensity, 1.0f - nextDensityGrid[nIdx], diff * lateralFlowFactor);
                                nextDensityGrid[idx] -= flow;
                                nextDensityGrid[nIdx] += flow;
                                currentDensity -= flow;
                            }
                        }
                    }

                    nextDensityGrid[idx] = Mathf.Max(0f, nextDensityGrid[idx] - dissipationPerTick);
                }
            }
        }
        System.Array.Copy(nextDensityGrid, densityGrid, totalNodes);
    }

    void RenderSmoke()
    {
        for (int i = 0; i < batchCounts.Length; i++) batchCounts[i] = 0;

        int currentBatch = 0;
        int currentBatchIdx = 0;

        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int z = 0; z < gridSize.z; z++)
                {
                    float density = densityGrid[GetIndex(x, y, z)];
                    if (density > 0.05f)
                    {
                        // Posicao no mundo (baseada na posicao deste GameObject)
                        Vector3 pos = GetCellCenterWorld(x, y, z);
                        Vector3 scale = Vector3.one * (density * voxelSize); // O tamanho do cubo reflete a densidade

                        batches[currentBatch][currentBatchIdx] = Matrix4x4.TRS(pos, Quaternion.identity, scale);
                        currentBatchIdx++;
                        batchCounts[currentBatch]++;

                        if (currentBatchIdx >= 1023)
                        {
                            currentBatch++;
                            currentBatchIdx = 0;
                        }
                    }
                }
            }
        }

        // Desenhar todos os lotes diretamente na GPU
        for (int i = 0; i <= currentBatch; i++)
        {
            if (batchCounts[i] > 0)
            {
                // Limpar matrizes vazias do ultimo lote para nao dar erro no Unity
                Matrix4x4[] cleanBatch = new Matrix4x4[batchCounts[i]];
                System.Array.Copy(batches[i], cleanBatch, batchCounts[i]);

                Graphics.DrawMeshInstanced(voxelMesh, 0, voxelMaterial, cleanBatch, batchCounts[i]);
            }
        }
    }

    // Funcao publica para o jogador ler a densidade do fumo na sua posicao exata
    public float GetDensityAtWorldPosition(Vector3 worldPos)
    {
        int idx = WorldToCellIndex(worldPos);
        return idx != -1 ? densityGrid[idx] : 0f;
    }
}
