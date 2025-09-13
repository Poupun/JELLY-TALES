using System.Collections.Generic;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Mesh pooling system to reduce GC allocation and improve performance when creating/destroying chunks
    /// </summary>
    public class ChunkMeshPool : MonoBehaviour
    {
        private static ChunkMeshPool _instance;
        public static ChunkMeshPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ChunkMeshPool");
                    _instance = go.AddComponent<ChunkMeshPool>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        [Header("Pool Settings")]
        [SerializeField] private int initialPoolSize = 50;
        [SerializeField] private int maxPoolSize = 200;
        [SerializeField] private bool enablePooling = true;

        private readonly Queue<Mesh> _availableMeshes = new Queue<Mesh>();
        private readonly HashSet<Mesh> _activeMeshes = new HashSet<Mesh>();
        private int _totalCreated = 0;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                InitializePool();
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void InitializePool()
        {
            for (int i = 0; i < initialPoolSize; i++)
            {
                CreateNewMesh();
            }
        }

        private Mesh CreateNewMesh()
        {
            var mesh = new Mesh();
            mesh.name = $"ChunkMesh_{_totalCreated++}";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _availableMeshes.Enqueue(mesh);
            return mesh;
        }

        public Mesh GetMesh()
        {
            if (!enablePooling)
            {
                var newMesh = new Mesh();
                newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                return newMesh;
            }

            Mesh mesh;
            if (_availableMeshes.Count > 0)
            {
                mesh = _availableMeshes.Dequeue();
            }
            else
            {
                mesh = CreateNewMesh();
                _availableMeshes.Dequeue(); // Remove from queue since we're using it immediately
            }

            mesh.Clear();
            _activeMeshes.Add(mesh);
            return mesh;
        }

        public void ReturnMesh(Mesh mesh)
        {
            if (!enablePooling || mesh == null)
            {
                if (mesh != null)
                {
                    if (Application.isPlaying)
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh);
                }
                return;
            }

            if (_activeMeshes.Remove(mesh))
            {
                if (_availableMeshes.Count < maxPoolSize)
                {
                    mesh.Clear();
                    _availableMeshes.Enqueue(mesh);
                }
                else
                {
                    // Pool is full, destroy the mesh
                    if (Application.isPlaying)
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh);
                }
            }
        }

        public void ClearPool()
        {
            // Return all active meshes
            foreach (var mesh in _activeMeshes)
            {
                if (mesh != null)
                {
                    if (Application.isPlaying)
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh);
                }
            }
            _activeMeshes.Clear();

            // Clear available meshes
            while (_availableMeshes.Count > 0)
            {
                var mesh = _availableMeshes.Dequeue();
                if (mesh != null)
                {
                    if (Application.isPlaying)
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh);
                }
            }

            _totalCreated = 0;
        }

        private void OnDestroy()
        {
            ClearPool();
        }

        // Debug info
        public int ActiveMeshCount => _activeMeshes.Count;
        public int AvailableMeshCount => _availableMeshes.Count;
        public int TotalCreated => _totalCreated;
    }
}