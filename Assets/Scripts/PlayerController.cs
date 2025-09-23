using UnityEngine;
using System;

public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")] public float walkSpeed = 5f; public float sprintSpeed = 8f; public float crouchSpeed = 1f; public float mouseSensitivity = 2f; public float jumpForce = 5f;
    [Tooltip("Gravity multiplier - higher values make you fall faster")]
    [Range(1f, 5f)]
    public float gravityMultiplier = 1.5f;
    
    [Header("Movement Physics - Tweak These!")]
    [Tooltip("How fast the player accelerates to target speed on ground")]
    public float groundAcceleration = 5000f;
    [Tooltip("How fast the player decelerates when no input on ground")]
    public float groundDeceleration = 5000f;
    [Tooltip("Air control acceleration - how fast you can change direction in air")]
    public float airAcceleration = 5000f;
    [Tooltip("Air control multiplier - how much control you have in air (1 = full, 0 = none)")]
    [Range(0f, 10f)]
    public float airControlMultiplier = 1f;
    [Tooltip("Max air speed multiplier - limits air speed relative to ground speed")]
    [Range(0.1f, 2f)]
    public float maxAirSpeedMultiplier = 1f;
    [Tooltip("Air deceleration when no input in air (units per second) - KEEP LOW")]
    [Range(0f, 10f)]
    public float airDeceleration = 0.1f;
    [Tooltip("Air resistance/drag - additional slowdown in air")]
    [Range(0f, 1f)]
    public float airDrag = 0f;
    [Header("Sprint Settings")] public KeyCode sprintKey = KeyCode.LeftShift; public float sprintFOVIncrease = 10f; public float fovTransitionSpeed = 8f;
    [Header("Crouch Settings")] public KeyCode crouchKey = KeyCode.LeftControl; public float crouchHeight = 1.3f; public float standHeight = 1.8f; public float crouchTransitionSpeed = 10f;
    [Header("Interaction")] public float interactionRange = 6f; public LayerMask blockLayerMask = -1;
    [Header("Drops")] 
    public bool grassDropsDirt = true; // if true, breaking grass gives dirt (Minecraft-like); otherwise drops grass block
    [Header("Drop Settings")]
    public KeyCode dropKey = KeyCode.Q; // Key to drop items (Q is Minecraft standard)
    public float dropForce = 5f; // Force applied to dropped items
    public Vector3 dropOffset = new Vector3(0, 0.5f, 1f); // Offset from player position to drop items

    // Components
    private CharacterController characterController; private Camera playerCamera; private WorldGenerator worldGenerator; private UnifiedPlayerInventory playerInventory;

    // Movement state
    private Vector3 velocity; 
    private Vector3 horizontalVelocity; // Separate horizontal velocity for physics-based movement
    private float xRotation = 0f; private bool isSprinting; private bool isCrouching; private float currentSpeed; private float baseFOV; private float targetHeight;
    
    // Noclip state
    private bool isNoclip = false;
    private float noclipSpeed = 10f;

    // Events
    public event Action<BlockType, Vector3Int> OnBlockPlaced; // Fired when player successfully places a block

    // Target highlight removed

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();
        worldGenerator = FindFirstObjectByType<WorldGenerator>(FindObjectsInactive.Exclude);
        playerInventory = GetComponent<UnifiedPlayerInventory>() ?? gameObject.AddComponent<UnifiedPlayerInventory>();
        baseFOV = playerCamera.fieldOfView; targetHeight = standHeight; currentSpeed = walkSpeed; Cursor.lockState = CursorLockMode.Locked;
    }

    // Update method moved below to include dropped item hover checking

    void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity; float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity; xRotation -= mouseY; xRotation = Mathf.Clamp(xRotation,-90f,90f); playerCamera.transform.localRotation = Quaternion.Euler(xRotation,0f,0f); transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovementState()
    {
        float horizontal = Input.GetAxisRaw("Horizontal"); float vertical = Input.GetAxisRaw("Vertical"); bool isMoving = Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f;
        if (Input.GetKeyDown(crouchKey)) { isCrouching = !isCrouching; targetHeight = isCrouching ? crouchHeight : standHeight; }
        
        // Sprint state - maintain while in air if key is held and moving
        if (Input.GetKey(sprintKey) && !isCrouching && isMoving) 
        { 
            isSprinting = true; 
            currentSpeed = sprintSpeed; 
        }
        else if (isCrouching) 
        { 
            isSprinting = false; 
            currentSpeed = crouchSpeed; 
        }
        else if (isMoving)
        { 
            isSprinting = false; 
            currentSpeed = walkSpeed; 
        }
        // Keep current speed/sprint state when not moving (preserves air state)
    }

    void HandleMovement()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        
        // Calculate input direction in world space
        Vector3 inputDirection = (transform.right * horizontal + transform.forward * vertical).normalized;
        Vector3 targetVelocity = inputDirection * currentSpeed;
        
        // Physics-based horizontal movement
        bool isGrounded = characterController.isGrounded;
        
        if (isGrounded)
        {
            // Ground movement - accelerate/decelerate to target velocity
            if (inputDirection.magnitude > 0.01f)
            {
                // Accelerating toward target - use proper acceleration
                Vector3 velocityDiff = targetVelocity - horizontalVelocity;
                if (velocityDiff.magnitude > 0.01f)
                {
                    Vector3 acceleration = velocityDiff.normalized * groundAcceleration;
                    horizontalVelocity += acceleration * Time.deltaTime;
                    
                    // Clamp to target speed if we overshoot
                    if (Vector3.Dot(targetVelocity - horizontalVelocity, acceleration) <= 0)
                    {
                        horizontalVelocity = targetVelocity;
                    }
                    
                    // Debug acceleration (remove this later)
                    if (Time.frameCount % 30 == 0) // Log every 30 frames
                    {
                        Debug.Log($"Ground Accel: Current={horizontalVelocity.magnitude:F2}, Target={targetVelocity.magnitude:F2}, Accel={groundAcceleration}");
                    }
                }
                else
                {
                    horizontalVelocity = targetVelocity;
                }
            }
            else
            {
                // Decelerating when no input - use proper deceleration
                Vector3 deceleration = -horizontalVelocity.normalized * groundDeceleration;
                Vector3 newVelocity = horizontalVelocity + deceleration * Time.deltaTime;
                
                // Stop if we would overshoot zero
                if (Vector3.Dot(horizontalVelocity, newVelocity) <= 0)
                {
                    horizontalVelocity = Vector3.zero;
                }
                else
                {
                    horizontalVelocity = newVelocity;
                }
            }
        }
        else
        {
            // Air movement - configurable air control
            if (inputDirection.magnitude > 0.01f)
            {
                // Apply air control multiplier and max speed limit
                Vector3 limitedTargetVelocity = targetVelocity * maxAirSpeedMultiplier;
                
                // Air acceleration toward target - modified by air control multiplier
                Vector3 velocityDiff = limitedTargetVelocity - horizontalVelocity;
                if (velocityDiff.magnitude > 0.01f)
                {
                    Vector3 acceleration = velocityDiff.normalized * (airAcceleration * airControlMultiplier);
                    horizontalVelocity += acceleration * Time.deltaTime;
                    
                    // Clamp to target speed if we overshoot
                    if (Vector3.Dot(limitedTargetVelocity - horizontalVelocity, acceleration) <= 0)
                    {
                        horizontalVelocity = limitedTargetVelocity;
                    }
                    
                    // Debug air control (remove this later)
                    if (Time.frameCount % 30 == 0) // Log every 30 frames
                    {
                        Debug.Log($"Air Control: Current={horizontalVelocity.magnitude:F2}, Target={limitedTargetVelocity.magnitude:F2}, Control={airControlMultiplier:F2}");
                    }
                }
                else
                {
                    horizontalVelocity = limitedTargetVelocity;
                }
            }
            else
            {
                // Air deceleration when no input - LINEAR slowdown (not exponential)
                if (horizontalVelocity.magnitude > 0.01f)
                {
                    // Linear deceleration - reduce speed by fixed amount per second
                    float currentSpeed = horizontalVelocity.magnitude;
                    float newSpeed = Mathf.Max(0f, currentSpeed - airDeceleration * Time.deltaTime);
                    
                    if (newSpeed > 0f)
                    {
                        horizontalVelocity = horizontalVelocity.normalized * newSpeed;
                    }
                    else
                    {
                        horizontalVelocity = Vector3.zero;
                    }
                }
            }
        }
        
        // Vertical movement (gravity and jumping)
        if (isGrounded && velocity.y < 0) velocity.y = -2f;
        if (Input.GetButtonDown("Jump") && isGrounded && !isCrouching) 
        {
            velocity.y = Mathf.Sqrt(jumpForce * -2f * Physics.gravity.y * gravityMultiplier);
        }
        velocity.y += Physics.gravity.y * gravityMultiplier * Time.deltaTime;
        
        // Combine horizontal and vertical movement
        Vector3 finalMovement = horizontalVelocity + Vector3.up * velocity.y;
        characterController.Move(finalMovement * Time.deltaTime);
    }

    void UpdateCrouchHeight()
    {
        float newHeight = Mathf.Lerp(characterController.height, targetHeight, Time.deltaTime * crouchTransitionSpeed); characterController.height = newHeight; Vector3 c = characterController.center; c.y = newHeight/2f; characterController.center = c; Vector3 camPos = playerCamera.transform.localPosition; float targetY = (isCrouching?crouchHeight:standHeight)*0.9f; camPos.y = Mathf.Lerp(camPos.y,targetY,Time.deltaTime * crouchTransitionSpeed); playerCamera.transform.localPosition = camPos;
    }

    void UpdateSprintFOV()
    { float targetFOV = isSprinting ? baseFOV + sprintFOVIncrease : baseFOV; playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView,targetFOV,Time.deltaTime * fovTransitionSpeed); }

    void HandleInteraction()
    { 
        // Mining is now handled by MiningSystem component - no more instant breaking
        // Handle right-click for block placement with dropped item priority
        if (Input.GetMouseButtonDown(1))
        {
            Debug.Log("PlayerController: Right-click detected");
            
            // First check if we're clicking on a dropped item
            if (TryPickupDroppedItem())
            {
                Debug.Log("PlayerController: Picked up dropped item, skipping block placement");
                return;
            }
            
            // Check if we're clicking on a crafting table
            if (TryInteractWithCraftingTable())
            {
                Debug.Log("PlayerController: Interacted with crafting table, skipping block placement");
                return;
            }
            
            // If no dropped item or crafting table was clicked, place a block
            Debug.Log("PlayerController: No special interaction, placing block");
            PlaceBlock();
        }
    }
    
    private bool TryPickupDroppedItem()
    {
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width/2, Screen.height/2, 0));
        RaycastHit[] hits = Physics.RaycastAll(ray, interactionRange);
        
        Debug.Log($"PlayerController: Checking {hits.Length} raycast hits for dropped items");
        
        // Sort hits by distance (closest first)
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        
        foreach (RaycastHit hit in hits)
        {
            DroppedItem droppedItem = hit.collider.GetComponent<DroppedItem>();
            if (droppedItem != null && !droppedItem.isBeingPickedUp)
            {
                Debug.Log($"PlayerController: Found dropped item {droppedItem.itemType} at distance {hit.distance}");
                droppedItem.TryPickup();
                return true;
            }
        }
        
        Debug.Log("PlayerController: No dropped items found in raycast");
        return false;
    }
    
    private bool TryInteractWithCraftingTable()
    {
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width/2, Screen.height/2, 0));
        
        // Use the same voxel raycast system as block placement
        if (worldGenerator != null && worldGenerator.useChunkStreaming && worldGenerator.useChunkMeshing)
        {
            Vector3Int hitCell, placeCell;
            Vector3 hitNormal;
            if (worldGenerator.TryVoxelRaycast(ray, interactionRange, out hitCell, out placeCell, out hitNormal))
            {
                BlockType blockType = worldGenerator.GetBlockType(hitCell);
                if (blockType == BlockType.CraftingTable)
                {
                    Debug.Log("PlayerController: Player right-clicked on crafting table, opening table crafting interface");
                    var inventoryManager = FindFirstObjectByType<InventoryManager>();
                    if (inventoryManager != null)
                    {
                        inventoryManager.OpenInventoryWithTableCrafting();
                        return true;
                    }
                }
            }
        }
        else
        {
            // Fallback raycast method
            if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
            {
                Vector3Int blockPosition = Vector3Int.RoundToInt(hit.collider.bounds.center);
                if (worldGenerator != null)
                {
                    BlockType blockType = worldGenerator.GetBlockType(blockPosition);
                    if (blockType == BlockType.CraftingTable)
                    {
                        Debug.Log("PlayerController: Player right-clicked on crafting table (fallback), opening table crafting interface");
                        var inventoryManager = FindFirstObjectByType<InventoryManager>();
                        if (inventoryManager != null)
                        {
                            inventoryManager.OpenInventoryWithTableCrafting();
                            return true;
                        }
                    }
                }
            }
        }
        
        return false;
    }
    
    private void HandleItemDrop()
    {
        // Handle drop input
        if (Input.GetKeyDown(dropKey))
        {
            // Check if Ctrl is held for full stack drop
            if (Input.GetKey(KeyCode.LeftControl))
            {
                DropEntireStack();
            }
            else
            {
                // Single item drop
                DropItem(1);
            }
        }
    }
    
    private void DropItem(int quantity = 1)
    {
        if (playerInventory == null)
        {
            Debug.Log("PlayerController: Cannot drop item - no inventory found");
            return;
        }
        
        ItemStack selectedItem = playerInventory.GetSelectedItem();
        if (selectedItem.IsEmpty)
        {
            Debug.Log("PlayerController: Cannot drop item - no item selected");
            return;
        }
        
        // Clamp quantity to available amount
        int actualQuantity = Mathf.Min(quantity, selectedItem.count);
        
        // Remove items from inventory
        if (!playerInventory.DropSelectedItem(actualQuantity))
        {
            Debug.Log("PlayerController: Failed to remove items from inventory");
            return;
        }
        
        // Calculate drop position
        Vector3 dropPosition = transform.position + transform.TransformDirection(dropOffset);
        
        // Create dropped item
        GameObject droppedItemObject = DroppedItem.CreateDroppedItem(dropPosition, selectedItem.blockType, actualQuantity);
        
        // Add physics force to simulate throw - directly in camera direction
        Rigidbody rb = droppedItemObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Use exact camera forward direction with slight upward component for arc
            Vector3 throwDirection = (playerCamera.transform.forward + Vector3.up * 0.2f).normalized;
            rb.AddForce(throwDirection * dropForce, ForceMode.VelocityChange);
            
            // Minimal angular velocity for natural rotation without randomness
            rb.angularVelocity = playerCamera.transform.forward * 2f;
        }
        
        Debug.Log($"PlayerController: Dropped {actualQuantity}x {selectedItem.blockType} at {dropPosition}");
    }
    
    private void DropEntireStack()
    {
        if (playerInventory == null)
        {
            Debug.Log("PlayerController: Cannot drop stack - no inventory found");
            return;
        }
        
        ItemStack selectedItem = playerInventory.GetSelectedItem();
        if (selectedItem.IsEmpty)
        {
            Debug.Log("PlayerController: Cannot drop stack - no item selected");
            return;
        }
        
        int quantity = selectedItem.count;
        
        // Remove entire stack from inventory
        if (!playerInventory.DropSelectedStack())
        {
            Debug.Log("PlayerController: Failed to remove stack from inventory");
            return;
        }
        
        // Calculate drop position
        Vector3 dropPosition = transform.position + transform.TransformDirection(dropOffset);
        
        // Create dropped item
        GameObject droppedItemObject = DroppedItem.CreateDroppedItem(dropPosition, selectedItem.blockType, quantity);
        
        // Add physics force to simulate throw - directly in camera direction
        Rigidbody rb = droppedItemObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Use exact camera forward direction with slight upward component for arc
            Vector3 throwDirection = (playerCamera.transform.forward + Vector3.up * 0.2f).normalized;
            rb.AddForce(throwDirection * dropForce, ForceMode.VelocityChange);
            
            // Minimal angular velocity for natural rotation without randomness
            rb.angularVelocity = playerCamera.transform.forward * 2f;
        }
        
        Debug.Log($"PlayerController: Dropped entire stack of {quantity}x {selectedItem.blockType} at {dropPosition}");
    }
    
    // Add hover checking for dropped items
    void Update()
    {
        // Handle F8 noclip toggle
        HandleNoclipToggle();
        
        HandleMouseLook(); 
        
        if (isNoclip)
        {
            HandleNoclipMovement();
        }
        else
        {
            HandleMovementState(); HandleMovement(); UpdateCrouchHeight(); UpdateSprintFOV();
        }
        
        HandleInteraction();
        
        // Handle item dropping
        HandleItemDrop();
        
        // Check for dropped item hover every frame
        CheckDroppedItemHover();
    }
    
    private DroppedItem currentlyHoveredItem = null;
    
    private void CheckDroppedItemHover()
    {
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width/2, Screen.height/2, 0));
        RaycastHit[] hits = Physics.RaycastAll(ray, interactionRange);
        
        // Find the closest dropped item being looked at
        DroppedItem closestDroppedItem = null;
        float closestDistance = float.MaxValue;
        
        foreach (RaycastHit hit in hits)
        {
            DroppedItem droppedItem = hit.collider.GetComponent<DroppedItem>();
            if (droppedItem != null && !droppedItem.isBeingPickedUp && hit.distance < closestDistance)
            {
                closestDroppedItem = droppedItem;
                closestDistance = hit.distance;
            }
        }
        
        // Only update if the hovered item changed to avoid expensive operations
        if (currentlyHoveredItem != closestDroppedItem)
        {
            // Remove hover from previously hovered item
            if (currentlyHoveredItem != null)
            {
                Debug.Log($"PlayerController: Removing hover from {currentlyHoveredItem.itemType}");
                currentlyHoveredItem.SetHovered(false);
            }
            
            // Set hover on new item
            if (closestDroppedItem != null)
            {
                Debug.Log($"PlayerController: Setting {closestDroppedItem.itemType} to hovered");
                closestDroppedItem.SetHovered(true);
            }
            
            currentlyHoveredItem = closestDroppedItem;
        }
    }
    
    public void PlaceBlockFromInteractionManager(Ray ray)
    {
        PlaceBlock(); // Use existing block placement logic
    }

    void BreakBlock()
    {
        // Remove plant first if cursor hits a plant cell
        if (TryRemovePlant()) return;
        if (AcquireTargetCell(out var cell,out var type))
    { 
        BlockType drop = (type == BlockType.Grass && grassDropsDirt) ? BlockType.Dirt : type; 
        worldGenerator.PlaceBlock(cell, BlockType.Air); 
        playerInventory?.AddBlock(drop,1); 
        return; 
    }
    }

    bool TryRemovePlant()
    {
        if (worldGenerator == null) return false; Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width/2,Screen.height/2,0)); const float step=0.05f; float maxD = Mathf.Min(interactionRange,8f); for(float d=0; d<=maxD; d+=step){ Vector3 p = ray.origin + ray.direction*d; Vector3Int c = new Vector3Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z)); if (worldGenerator.HasBatchedPlantAt(c)){ worldGenerator.RemoveBatchedPlantAt(c); return true; } }
        return false;
    }

    bool AcquireTargetCell(out Vector3Int cell, out BlockType type)
    {
        cell = default; type = BlockType.Air; if (worldGenerator == null) return false; Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width/2,Screen.height/2,0));
        if (worldGenerator.useChunkStreaming && worldGenerator.useChunkMeshing)
        { Vector3Int hitCell, placeCell; Vector3 hitNormal; if (worldGenerator.TryVoxelRaycast(ray, interactionRange, out hitCell, out placeCell, out hitNormal)) { var t = worldGenerator.GetBlockType(hitCell); if (t != BlockType.Air) { cell = hitCell; type = t; return true; } } }
        const float step=0.075f; float maxD = Mathf.Min(interactionRange,8f); for(float d=0; d<=maxD; d+=step){ Vector3 p = ray.origin + ray.direction*d; Vector3Int c = new Vector3Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z)); if (c.y < 0 || c.y >= worldGenerator.worldHeight) continue; var bt = worldGenerator.GetBlockType(c); if (bt != BlockType.Air){ cell = c; type = bt; return true; } }
        return false;
    }

    void PlaceBlock()
    {
        Debug.Log("PlayerController: PlaceBlock called");
        
        if (playerInventory == null) 
        {
            Debug.Log("PlayerController: No playerInventory found");
            return; 
        }
        
        if (!playerInventory.HasBlockForPlacement(out BlockType placeType)) 
        {
            Debug.Log("PlayerController: No block available for placement");
            return; 
        }
        
        Debug.Log($"PlayerController: Attempting to place {placeType}");
        
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width/2,Screen.height/2,0));
        if (worldGenerator != null && worldGenerator.useChunkStreaming && worldGenerator.useChunkMeshing)
        { 
            Debug.Log("PlayerController: Using voxel raycast for block placement");
            Vector3Int hitCell, placeCell; Vector3 hitNormal; 
            if (worldGenerator.TryVoxelRaycast(ray, interactionRange, out hitCell, out placeCell, out hitNormal)) 
            { 
                Vector3Int pos = placeCell; 
                Debug.Log($"PlayerController: Voxel raycast hit, placing at {pos}");
                if (characterController){ Bounds bb = new Bounds((Vector3)pos, Vector3.one); if (bb.Intersects(characterController.bounds)) { Debug.Log("PlayerController: Cannot place - intersects player bounds"); return; } } 
                if (worldGenerator.GetBlockType(pos) == BlockType.Air)
                { 
                    Debug.Log($"PlayerController: Placing {placeType} at {pos}");
                    worldGenerator.PlaceBlock(pos, placeType); 
                    playerInventory.ConsumeOneFromSelected(); 
                    OnBlockPlaced?.Invoke(placeType, pos);
                } 
                else
                {
                    Debug.Log($"PlayerController: Cannot place - position occupied by {worldGenerator.GetBlockType(pos)}");
                }
                return; 
            } 
            else
            {
                Debug.Log("PlayerController: Voxel raycast missed");
            }
        }
        // Physics fallback (plants / colliders)
        Debug.Log("PlayerController: Trying physics raycast fallback");
        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange, blockLayerMask))
        { 
            Debug.Log($"PlayerController: Physics raycast hit {hit.collider.name}");
            Vector3Int pos; 
            BlockInfo bi = hit.collider.GetComponent<BlockInfo>(); 
            if (bi != null)
            { 
                Vector3 hp = hit.point + hit.normal*0.5f; 
                pos = Vector3Int.RoundToInt(hp); 
            } 
            else 
            { 
                pos = Vector3Int.RoundToInt(hit.collider.bounds.center); 
            } 
            if (characterController)
            { 
                Bounds bb = new Bounds((Vector3)pos,Vector3.one); 
                if (bb.Intersects(characterController.bounds)) 
                {
                    Debug.Log("PlayerController: Physics fallback - Cannot place, intersects player bounds");
                    return; 
                }
            } 
            if (worldGenerator != null && worldGenerator.GetBlockType(pos) == BlockType.Air)
            { 
                Debug.Log($"PlayerController: Physics fallback - Placing {placeType} at {pos}");
                worldGenerator.PlaceBlock(pos, placeType); 
                playerInventory.ConsumeOneFromSelected(); 
                OnBlockPlaced?.Invoke(placeType, pos);
            }
            else
            {
                Debug.Log($"PlayerController: Physics fallback - Cannot place, position occupied by {worldGenerator?.GetBlockType(pos)}");
            }
        }
        else
        {
            Debug.Log("PlayerController: Physics raycast fallback missed");
        }
    }

    // Block highlighting system removed

    private void HandleNoclipToggle()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            isNoclip = !isNoclip;
            
            if (isNoclip)
            {
                Debug.Log("PlayerController: Noclip ENABLED - Flying through blocks");
                // Reset velocity when entering noclip
                velocity = Vector3.zero;
                horizontalVelocity = Vector3.zero;
                // Disable collision
                characterController.enabled = false;
            }
            else
            {
                Debug.Log("PlayerController: Noclip DISABLED - Normal physics");
                // Re-enable collision
                characterController.enabled = true;
                // Reset states
                velocity = Vector3.zero;
                horizontalVelocity = Vector3.zero;
            }
        }
    }
    
    private void HandleNoclipMovement()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        float upDown = 0f;
        
        // Use Space for up, Left Shift for down (or crouch key)
        if (Input.GetKey(KeyCode.Space)) upDown = 1f;
        if (Input.GetKey(crouchKey)) upDown = -1f;
        
        // Calculate movement direction
        Vector3 direction = (transform.right * horizontal + transform.forward * vertical + Vector3.up * upDown).normalized;
        
        // Apply speed multiplier if sprinting
        float currentNoclipSpeed = noclipSpeed;
        if (Input.GetKey(sprintKey))
        {
            currentNoclipSpeed *= 2f; // Double speed when sprinting
        }
        
        // Move directly through transform (no collision)
        Vector3 movement = direction * currentNoclipSpeed * Time.deltaTime;
        transform.position += movement;
        
        // Debug noclip movement
        if (movement.magnitude > 0.01f && Time.frameCount % 60 == 0)
        {
            Debug.Log($"PlayerController: Noclip flying at {currentNoclipSpeed:F1} units/sec");
        }
    }

    // Public getters
    public bool IsSprinting() => isSprinting; public bool IsCrouching() => isCrouching; public float GetCurrentSpeed() => currentSpeed; public bool IsNoclip() => isNoclip;
}