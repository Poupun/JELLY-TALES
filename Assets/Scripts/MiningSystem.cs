using UnityEngine;
using System.Collections;

public class MiningSystem : MonoBehaviour
{
    [Header("Mining Settings")]
    public float miningRange = 5f;
    public LayerMask blockLayerMask = -1;
    
    [Header("Mining Speed")]
    [Tooltip("Global mining speed multiplier. Higher = faster mining. 1.0 = default speed")]
    [Range(0.1f, 10f)]
    public float globalMiningSpeedMultiplier = 1f;
    
    [Header("Visual Feedback")]
    public GameObject miningParticlesPrefab;
    public AudioSource miningAudioSource;
    public AudioClip[] miningStartSounds;
    public AudioClip[] miningSounds;
    public AudioClip[] blockBreakSounds;
    
    [Header("Mining Progress")]
    public float progressUpdateRate = 0.1f;
    
    private Camera playerCamera;
    private WorldGenerator worldGenerator;
    private PlayerController playerController;
    private bool isMining = false;
    private Vector3Int currentMiningBlock;
    private float miningProgress = 0f;
    private float miningTimeRequired = 0f;
    private Coroutine miningCoroutine;
    private GameObject currentParticles;
    
    // Events for UI feedback
    public System.Action<float> OnMiningProgressChanged;
    public System.Action OnMiningStarted;
    public System.Action OnMiningCanceled;
    public System.Action<BlockType> OnBlockBroken;
    
    // Block animation tracking
    private GameObject currentTargetBlock;
    private Vector3 originalBlockScale;
    private Coroutine blockScaleCoroutine;
    
    void Start()
    {
        playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null)
            playerCamera = Camera.main;
            
        worldGenerator = FindFirstObjectByType<WorldGenerator>();
        playerController = GetComponent<PlayerController>();

        if (miningAudioSource == null)
            miningAudioSource = GetComponent<AudioSource>();
    }
    
    void Update()
    {
        HandleMiningInput();
    }
    
    private void HandleMiningInput()
    {
        bool leftClickHeld = Input.GetMouseButton(0);
        bool leftClickDown = Input.GetMouseButtonDown(0);
        bool leftClickUp = Input.GetMouseButtonUp(0);
        
        if (leftClickDown)
        {
            StartMining();
        }
        else if (leftClickUp || !leftClickHeld)
        {
            StopMining();
        }
    }
    
    private void StartMining()
    {
        if (isMining) return;
        
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        
        Debug.Log("Mining: Casting voxel ray...");
        
        // Use WorldGenerator's voxel raycast for chunk-based systems
        if (worldGenerator != null && worldGenerator.useChunkStreaming && worldGenerator.useChunkMeshing)
        {
            Vector3Int hitCell, placeCell;
            Vector3 hitNormal;
            
            if (worldGenerator.TryVoxelRaycast(ray, miningRange, out hitCell, out placeCell, out hitNormal))
            {
                BlockType blockType = worldGenerator.GetBlockType(hitCell);
                Debug.Log($"Mining: Hit voxel {blockType} at position {hitCell}");
                
                if (blockType == BlockType.Air)
                {
                    Debug.Log("Mining: Hit block is Air, cannot mine");
                    return;
                }
                
                // Check if block can be mined
                if (BlockHardnessSystem.IsUnbreakable(blockType))
                {
                    Debug.Log($"Mining: Block {blockType} is unbreakable");
                    return;
                }
                
                // Start mining process
                currentMiningBlock = hitCell;

                // Calculate mining time with tool effectiveness
                float baseTime = BlockHardnessSystem.GetMiningTime(blockType);
                float toolMultiplier = ToolEffectivenessSystem.GetMiningSpeedMultiplierForHeldTool(blockType);
                float totalMultiplier = globalMiningSpeedMultiplier * toolMultiplier;

                // Instant mining when in noclip mode
                if (playerController != null && playerController.IsNoclip())
                {
                    miningTimeRequired = 0.01f; // Nearly instant (0.01 seconds)
                }
                else
                {
                    miningTimeRequired = baseTime / totalMultiplier;
                }
                miningProgress = 0f;
                isMining = true;
                
                Debug.Log($"Mining: Started mining {blockType}, time required: {miningTimeRequired:F2}s (base: {baseTime:F1}s, global: {globalMiningSpeedMultiplier}x, tool: {toolMultiplier:F1}x, total: {totalMultiplier:F1}x)");
                
                // Start mining coroutine
                miningCoroutine = StartCoroutine(MiningCoroutine(blockType));
                
                // Visual/Audio feedback
                PlayMiningStartSound();
                
                // Find and start animating the target block
                FindAndAnimateTargetBlock(hitCell);
                
                OnMiningStarted?.Invoke();
            }
            else
            {
                Debug.Log("Mining: Voxel raycast missed");
            }
        }
        else
        {
            // Fallback to physics raycast for non-chunk systems
            RaycastHit hit;
            Debug.Log("Mining: Using physics raycast fallback...");
            
            if (Physics.Raycast(ray, out hit, miningRange, blockLayerMask))
            {
                Debug.Log($"Mining: Hit object {hit.collider.name}");
                
                BlockInfo blockInfo = hit.collider.GetComponent<BlockInfo>();
                if (blockInfo == null) 
                {
                    Debug.Log("Mining: No BlockInfo component found on hit object");
                    return;
                }
                
                BlockType blockType = worldGenerator.GetBlockType(blockInfo.position);
                Debug.Log($"Mining: Found block type {blockType} at position {blockInfo.position}");
                
                // Check if block can be mined
                if (BlockHardnessSystem.IsUnbreakable(blockType))
                {
                    Debug.Log($"Mining: Block {blockType} is unbreakable");
                    return;
                }
                
                // Start mining process
                currentMiningBlock = blockInfo.position;
                
                // Calculate mining time with tool effectiveness
                float baseTime = BlockHardnessSystem.GetMiningTime(blockType);
                float toolMultiplier = ToolEffectivenessSystem.GetMiningSpeedMultiplierForHeldTool(blockType);
                float totalMultiplier = globalMiningSpeedMultiplier * toolMultiplier;
                miningTimeRequired = baseTime / totalMultiplier;
                miningProgress = 0f;
                isMining = true;
                
                Debug.Log($"Mining: Started mining {blockType}, time required: {miningTimeRequired:F2}s (base: {baseTime:F1}s, global: {globalMiningSpeedMultiplier}x, tool: {toolMultiplier:F1}x, total: {totalMultiplier:F1}x)");
                
                // Start mining coroutine
                miningCoroutine = StartCoroutine(MiningCoroutine(blockType));
                
                // Visual/Audio feedback
                PlayMiningStartSound();
                
                // Find and start animating the target block
                FindAndAnimateTargetBlock(currentMiningBlock);
                
                OnMiningStarted?.Invoke();
            }
            else
            {
                Debug.Log("Mining: No hit detected within range");
            }
        }
    }
    
    private void StopMining()
    {
        if (!isMining) return;
        
        // Cancel mining
        isMining = false;
        miningProgress = 0f;
        
        if (miningCoroutine != null)
        {
            StopCoroutine(miningCoroutine);
            miningCoroutine = null;
        }
        
        // Clean up effects and restore block scale
        RestoreBlockScale();
        StopMiningAudio();
        
        OnMiningCanceled?.Invoke();
    }
    
    private IEnumerator MiningCoroutine(BlockType blockType)
    {
        Debug.Log($"Mining: Coroutine started for {blockType}, required time: {miningTimeRequired}s");
        
        while (miningProgress < miningTimeRequired && isMining)
        {
            // Check if we're still looking at the same block
            if (!IsLookingAtCurrentBlock())
            {
                Debug.Log("Mining: No longer looking at target block, stopping");
                StopMining();
                yield break;
            }
            
            // Update mining progress
            miningProgress += Time.deltaTime;
            float progressPercent = miningProgress / miningTimeRequired;
            
            Debug.Log($"Mining: Progress {miningProgress:F2}/{miningTimeRequired:F2} ({progressPercent:P0})");
            
            OnMiningProgressChanged?.Invoke(progressPercent);
            
            // Update block scale based on progress
            UpdateBlockScale(progressPercent);
            
            // Play mining sounds periodically
            if (Random.Range(0f, 1f) < 0.1f) // 10% chance per frame
            {
                PlayMiningSound();
            }
            
            yield return new WaitForSeconds(progressUpdateRate);
        }
        
        // Mining complete
        if (isMining && miningProgress >= miningTimeRequired)
        {
            Debug.Log("Mining: Time requirement met, completing block breaking");
            CompleteBlockBreaking(blockType);
        }
        else
        {
            Debug.Log($"Mining: Coroutine ended without completion. isMining: {isMining}, progress: {miningProgress:F2}/{miningTimeRequired:F2}");
        }
    }
    
    private bool IsLookingAtCurrentBlock()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        
        // Use WorldGenerator's voxel raycast for chunk-based systems
        if (worldGenerator != null && worldGenerator.useChunkStreaming && worldGenerator.useChunkMeshing)
        {
            Vector3Int hitCell, placeCell;
            Vector3 hitNormal;
            
            if (worldGenerator.TryVoxelRaycast(ray, miningRange, out hitCell, out placeCell, out hitNormal))
            {
                return hitCell == currentMiningBlock;
            }
        }
        else
        {
            // Fallback to physics raycast for non-chunk systems
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit, miningRange, blockLayerMask))
            {
                BlockInfo blockInfo = hit.collider.GetComponent<BlockInfo>();
                if (blockInfo != null && blockInfo.position == currentMiningBlock)
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    private void CompleteBlockBreaking(BlockType blockType)
    {
        Debug.Log($"Mining: Block breaking completed for {blockType} at {currentMiningBlock}");
        
        // Break the block
        worldGenerator.PlaceBlock(currentMiningBlock, BlockType.Air);
        
        // Create dropped item
        Vector3 dropPosition = new Vector3(currentMiningBlock.x, currentMiningBlock.y, currentMiningBlock.z) + Vector3.up * 0.5f;
        DroppedItem.CreateDroppedItem(dropPosition, blockType, 1);
        
        Debug.Log($"Mining: Created dropped item {blockType} at {dropPosition}");
        
        // Audio/Visual feedback
        PlayBlockBreakSound();
        
        // Camera shake removed - was causing camera twitch
        
        // Clean up - block will be destroyed by WorldGenerator, no need to restore scale
        isMining = false;
        miningProgress = 0f;
        currentTargetBlock = null;
        
        OnBlockBroken?.Invoke(blockType);
    }
    
    private void FindAndAnimateTargetBlock(Vector3Int blockPosition)
    {
        // In chunk-based systems, blocks don't have individual GameObjects
        // We need to create a temporary visual representation or find existing block objects
        // For now, let's try to find if there are any block objects at this position
        
        Collider[] colliders = Physics.OverlapBox(
            new Vector3(blockPosition.x + 0.5f, blockPosition.y + 0.5f, blockPosition.z + 0.5f),
            Vector3.one * 0.4f,
            Quaternion.identity,
            blockLayerMask
        );
        
        foreach (Collider col in colliders)
        {
            // Look for block objects (not dropped items)
            if (col.GetComponent<DroppedItem>() == null)
            {
                currentTargetBlock = col.gameObject;
                originalBlockScale = currentTargetBlock.transform.localScale;
                Debug.Log($"Mining: Found target block {currentTargetBlock.name} at {blockPosition}");
                return;
            }
        }
        
        Debug.Log($"Mining: No individual block GameObject found at {blockPosition} (chunk-based system)");
        currentTargetBlock = null;
    }
    
    private void UpdateBlockScale(float progressPercent)
    {
        if (currentTargetBlock == null) return;
        
        // Scale from 1.0 to 0.1 as mining progresses (don't make it disappear completely)
        float scaleMultiplier = Mathf.Lerp(1.0f, 0.1f, progressPercent);
        Vector3 targetScale = originalBlockScale * scaleMultiplier;
        
        currentTargetBlock.transform.localScale = targetScale;
    }
    
    private void RestoreBlockScale()
    {
        if (currentTargetBlock == null) return;
        
        Debug.Log($"Mining: Restoring block scale for {currentTargetBlock.name}");
        
        // Smoothly restore the original scale
        if (blockScaleCoroutine != null)
        {
            StopCoroutine(blockScaleCoroutine);
        }
        blockScaleCoroutine = StartCoroutine(RestoreScaleCoroutine());
    }
    
    private IEnumerator RestoreScaleCoroutine()
    {
        if (currentTargetBlock == null) yield break;
        
        Vector3 currentScale = currentTargetBlock.transform.localScale;
        float duration = 0.3f;
        float elapsed = 0f;
        
        while (elapsed < duration && currentTargetBlock != null)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            progress = Mathf.SmoothStep(0f, 1f, progress);
            
            currentTargetBlock.transform.localScale = Vector3.Lerp(currentScale, originalBlockScale, progress);
            yield return null;
        }
        
        if (currentTargetBlock != null)
        {
            currentTargetBlock.transform.localScale = originalBlockScale;
        }
        
        currentTargetBlock = null;
        blockScaleCoroutine = null;
    }

    private void CreateMiningParticles(Vector3 position)
    {
        // Placeholder particles removed - using block shrinking animation instead
        // Only use prefab particles if available
        if (miningParticlesPrefab != null)
        {
            currentParticles = Instantiate(miningParticlesPrefab, position, Quaternion.identity);
        }
        // No more placeholder particles
    }
    
    // Placeholder particle methods removed - using block shrinking animation instead
    
    private void DestroyMiningParticles()
    {
        if (currentParticles != null)
        {
            Destroy(currentParticles);
            currentParticles = null;
        }
    }
    
    private void PlayMiningStartSound()
    {
        if (miningAudioSource != null && miningStartSounds.Length > 0)
        {
            AudioClip clip = miningStartSounds[Random.Range(0, miningStartSounds.Length)];
            miningAudioSource.PlayOneShot(clip);
        }
    }
    
    private void PlayMiningSound()
    {
        if (miningAudioSource != null && miningSounds.Length > 0)
        {
            AudioClip clip = miningSounds[Random.Range(0, miningSounds.Length)];
            miningAudioSource.PlayOneShot(clip, 0.3f);
        }
    }
    
    private void PlayBlockBreakSound()
    {
        if (miningAudioSource != null && blockBreakSounds.Length > 0)
        {
            AudioClip clip = blockBreakSounds[Random.Range(0, blockBreakSounds.Length)];
            miningAudioSource.PlayOneShot(clip);
        }
    }
    
    private void StopMiningAudio()
    {
        if (miningAudioSource != null && miningAudioSource.isPlaying)
        {
            miningAudioSource.Stop();
        }
    }
    
    // Public getters for UI
    public bool IsMining => isMining;
    public float MiningProgress => isMining ? (miningProgress / miningTimeRequired) : 0f;
    public BlockType CurrentMiningBlockType => isMining ? worldGenerator.GetBlockType(currentMiningBlock) : BlockType.Air;
}