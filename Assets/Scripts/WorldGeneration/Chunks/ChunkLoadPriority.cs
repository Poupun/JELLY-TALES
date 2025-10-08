using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Priority-based chunk loading system that loads chunks closest to the player first.
    /// Uses a priority queue for optimal chunk streaming performance.
    /// </summary>
    public class ChunkLoadPriority
    {
        private class ChunkLoadRequest : IComparable<ChunkLoadRequest>
        {
            public Vector2Int coord;
            public float priority; // Lower = higher priority
            public float distanceToPlayer;

            public int CompareTo(ChunkLoadRequest other)
            {
                return priority.CompareTo(other.priority);
            }
        }

        private readonly SortedSet<ChunkLoadRequest> priorityQueue = new SortedSet<ChunkLoadRequest>(
            Comparer<ChunkLoadRequest>.Create((a, b) =>
            {
                int priorityCompare = a.priority.CompareTo(b.priority);
                if (priorityCompare != 0) return priorityCompare;

                // Tie-breaker: use coord hash for stable ordering
                int hashCompare = a.coord.GetHashCode().CompareTo(b.coord.GetHashCode());
                return hashCompare;
            })
        );

        private readonly Dictionary<Vector2Int, ChunkLoadRequest> requestLookup = new Dictionary<Vector2Int, ChunkLoadRequest>();
        private Vector3 lastPlayerPosition;

        /// <summary>
        /// Add a chunk to the load queue with priority based on distance to player
        /// </summary>
        public void Enqueue(Vector2Int coord, Vector3 playerPosition, int chunkSizeX, int chunkSizeZ)
        {
            // Calculate distance from player to chunk center
            Vector3 chunkCenter = new Vector3(
                coord.x * chunkSizeX + chunkSizeX * 0.5f,
                playerPosition.y,
                coord.y * chunkSizeZ + chunkSizeZ * 0.5f
            );

            float distance = Vector3.Distance(playerPosition, chunkCenter);

            // Priority formula: distance + directional bias
            // Chunks in front of player get slight priority boost
            Vector3 toChunk = (chunkCenter - playerPosition).normalized;
            Vector3 playerForward = Vector3.forward; // Could use actual player forward direction
            float directionalBonus = Vector3.Dot(toChunk, playerForward) > 0 ? -5f : 0f; // Negative = higher priority

            float priority = distance + directionalBonus;

            var request = new ChunkLoadRequest
            {
                coord = coord,
                priority = priority,
                distanceToPlayer = distance
            };

            // Remove old request if exists
            if (requestLookup.TryGetValue(coord, out var oldRequest))
            {
                priorityQueue.Remove(oldRequest);
            }

            // Add new request
            priorityQueue.Add(request);
            requestLookup[coord] = request;
            lastPlayerPosition = playerPosition;
        }

        /// <summary>
        /// Get the next highest priority chunk to load
        /// </summary>
        public bool TryDequeue(out Vector2Int coord)
        {
            if (priorityQueue.Count == 0)
            {
                coord = default;
                return false;
            }

            var request = priorityQueue.Min;
            priorityQueue.Remove(request);
            requestLookup.Remove(request.coord);

            coord = request.coord;
            return true;
        }

        /// <summary>
        /// Update priorities for all queued chunks based on new player position
        /// Call this periodically when player moves significantly
        /// </summary>
        public void UpdatePriorities(Vector3 playerPosition, int chunkSizeX, int chunkSizeZ)
        {
            // Only update if player moved significantly (5+ units)
            if (Vector3.Distance(playerPosition, lastPlayerPosition) < 5f)
                return;

            var requests = new List<ChunkLoadRequest>(requestLookup.Values);
            priorityQueue.Clear();
            requestLookup.Clear();

            foreach (var request in requests)
            {
                Enqueue(request.coord, playerPosition, chunkSizeX, chunkSizeZ);
            }
        }

        /// <summary>
        /// Remove a chunk from the queue
        /// </summary>
        public void Remove(Vector2Int coord)
        {
            if (requestLookup.TryGetValue(coord, out var request))
            {
                priorityQueue.Remove(request);
                requestLookup.Remove(coord);
            }
        }

        /// <summary>
        /// Check if a chunk is queued
        /// </summary>
        public bool Contains(Vector2Int coord)
        {
            return requestLookup.ContainsKey(coord);
        }

        /// <summary>
        /// Clear all queued chunks
        /// </summary>
        public void Clear()
        {
            priorityQueue.Clear();
            requestLookup.Clear();
        }

        /// <summary>
        /// Get number of chunks in queue
        /// </summary>
        public int Count => priorityQueue.Count;

        /// <summary>
        /// Get distance to furthest queued chunk (for debugging)
        /// </summary>
        public float GetMaxDistance()
        {
            if (priorityQueue.Count == 0) return 0f;

            float maxDist = 0f;
            foreach (var request in priorityQueue)
            {
                if (request.distanceToPlayer > maxDist)
                    maxDist = request.distanceToPlayer;
            }
            return maxDist;
        }
    }
}
