using System.Collections.Generic;
using UnityEngine;

namespace WorldGeneration.Chunks
{
	/// <summary>
	/// A simple per-chunk container for blocks and their instantiated GameObjects.
	/// For now, it relies on WorldGenerator.CreateBlock for rendering each visible block.
	/// Later we can replace this with a meshed chunk for performance.
	/// </summary>
	public class Chunk
	{
		public readonly Vector2Int coord; // XZ chunk coordinate
		public readonly int sizeX;
		public readonly int sizeY;
		public readonly int sizeZ;

		// Block data for this chunk (local coordinates 0..size-1)
		public readonly BlockType[,,] blocks;

		// Parent GameObject to make unload easy
		public readonly GameObject parentGO;
		public readonly Transform parent;

		// Track rendered block positions belonging to this chunk for cleanup
		private readonly HashSet<Vector3Int> renderedCells = new HashSet<Vector3Int>();

		public Chunk(Vector2Int coord, int sizeX, int sizeY, int sizeZ, Transform worldParent)
		{
			this.coord = coord;
			this.sizeX = sizeX;
			this.sizeY = sizeY;
			this.sizeZ = sizeZ;
			blocks = new BlockType[sizeX, sizeY, sizeZ];

			parentGO = new GameObject($"Chunk ({coord.x},{coord.y})");
			parent = parentGO.transform;
			parent.SetParent(worldParent, false);
			parent.position = new Vector3(coord.x * sizeX, 0f, coord.y * sizeZ);
		}

		public Vector3Int WorldToLocal(Vector3Int worldPos)
		{
			int originX = coord.x * sizeX;
			int originZ = coord.y * sizeZ;
			return new Vector3Int(worldPos.x - originX, worldPos.y, worldPos.z - originZ);
		}

		public Vector3Int LocalToWorld(Vector3Int localPos)
		{
			int originX = coord.x * sizeX;
			int originZ = coord.y * sizeZ;
			return new Vector3Int(originX + localPos.x, localPos.y, originZ + localPos.z);
		}

		public bool ContainsWorldCell(Vector3Int worldPos)
		{
			var lp = WorldToLocal(worldPos);
			return lp.x >= 0 && lp.x < sizeX && lp.y >= 0 && lp.y < sizeY && lp.z >= 0 && lp.z < sizeZ;
		}

		public BlockType GetLocal(int lx, int ly, int lz)
		{
			if (lx < 0 || lx >= sizeX || ly < 0 || ly >= sizeY || lz < 0 || lz >= sizeZ) return BlockType.Air;
			return blocks[lx, ly, lz];
		}

		public void SetLocal(int lx, int ly, int lz, BlockType t)
		{
			if (lx < 0 || lx >= sizeX || ly < 0 || ly >= sizeY || lz < 0 || lz >= sizeZ) return;

			// DEBUG: Track when leaves are being replaced
			BlockType oldBlock = blocks[lx, ly, lz];
			if (oldBlock == BlockType.Leaves && t != BlockType.Leaves)
			{
				Vector3Int worldPos = new Vector3Int(coord.x * sizeX + lx, ly, coord.y * sizeZ + lz);
				UnityEngine.Debug.LogError($"Chunk.SetLocal: LEAVES at {worldPos} (local {lx},{ly},{lz}) being replaced with {t}!");
			}

			blocks[lx, ly, lz] = t;
		}

		public void GenerateSuperflat(WorldGenerator world)
		{
			// Fill blocks based on world's superflat settings
			for (int lx = 0; lx < sizeX; lx++)
			{
				for (int lz = 0; lz < sizeZ; lz++)
				{
					for (int ly = 0; ly < sizeY; ly++)
					{
						var wp = LocalToWorld(new Vector3Int(lx, ly, lz));
						blocks[lx, ly, lz] = world.GenerateBlockTypeAt(wp);
					}
				}
			}
		}

		public void BuildVisible(WorldGenerator world)
		{
			// Instantiate visible blocks under this chunk's parent for easy unload
			for (int lx = 0; lx < sizeX; lx++)
			{
				for (int ly = 0; ly < sizeY; ly++)
				{
					for (int lz = 0; lz < sizeZ; lz++)
					{
						var t = blocks[lx, ly, lz];
						if (t == BlockType.Air) continue;
						var wp = LocalToWorld(new Vector3Int(lx, ly, lz));
						if (world.ShouldRenderBlock(wp.x, wp.y, wp.z))
						{
							world.CreateBlock(wp, t, parent);
							renderedCells.Add(wp);
						}
					}
				}
			}

			// Plants: request spawn on exposed grass cells
			for (int lx = 0; lx < sizeX; lx++)
			{
				for (int lz = 0; lz < sizeZ; lz++)
				{
					for (int ly = 1; ly < sizeY; ly++)
					{
						var wp = LocalToWorld(new Vector3Int(lx, ly, lz));
						if (world.GetBlockType(wp) == BlockType.Grass && world.GetBlockType(wp + Vector3Int.up) == BlockType.Air)
						{
							if (world.ShouldRenderBlock(wp.x, wp.y, wp.z))
							{
								world.TrySpawnPlantClusterAt(wp, parent);
							}
						}
					}
				}
			}
		}

		public void Unload(WorldGenerator world)
		{
			// Ask world to unload cells and destroy parent
			world.UnloadCells(renderedCells, parentGO);
			renderedCells.Clear();
		}
	}
}
