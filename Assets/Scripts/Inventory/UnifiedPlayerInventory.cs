using UnityEngine;
using System;

public class UnifiedPlayerInventory : MonoBehaviour
{
    [Header("Inventory Settings")] 
    public int hotbarSize = 9;
    public int mainInventorySize = 27;
    public int craftingSlots = 5;  // 2x2 + result slot (36-40)
    public int tableCraftingSlots = 10; // 3x3 + result slot (41-50)
    
    public InventoryEntry[] hotbar;
    public InventoryEntry[] mainInventory;
    public InventoryEntry[] craftingInventory;
    public InventoryEntry[] tableCraftingInventory;
    public int selectedIndex = 0;

    public event Action OnInventoryChanged;

    void Awake()
    {
        if (hotbarSize <= 0) hotbarSize = 9;
        if (mainInventorySize <= 0) mainInventorySize = 27;
        
        hotbar = new InventoryEntry[hotbarSize];
        mainInventory = new InventoryEntry[mainInventorySize];
        craftingInventory = new InventoryEntry[craftingSlots];
        tableCraftingInventory = new InventoryEntry[tableCraftingSlots];
        
        for (int i = 0; i < hotbar.Length; i++)
        {
            hotbar[i] = InventoryEntry.Empty;
        }
        
        for (int i = 0; i < mainInventory.Length; i++)
        {
            mainInventory[i] = InventoryEntry.Empty;
        }
        
        for (int i = 0; i < craftingInventory.Length; i++)
        {
            craftingInventory[i] = InventoryEntry.Empty;
        }
        
        for (int i = 0; i < tableCraftingInventory.Length; i++)
        {
            tableCraftingInventory[i] = InventoryEntry.Empty;
        }
        
        // Add test items
        AddBlock(BlockType.Grass, 64);
        AddBlock(BlockType.Stone, 32);
        AddBlock(BlockType.Dirt, 16);
        AddBlock(BlockType.Log, 8);
        AddBlock(BlockType.Water, 16);  // Water blocks for testing flow system
        AddItem(ItemType.Stick, 16);  // Now properly as an item
        AddItem(ItemType.CraftingTable, 2);  // Now properly as an item
    }

    public bool AddBlock(BlockType type, int amount = 1)
    {
        return AddEntry(InventoryEntry.CreateBlock(type, amount));
    }
    
    public bool AddItem(ItemType type, int amount = 1)
    {
        return AddEntry(InventoryEntry.CreateItem(type, amount));
    }
    
    public bool AddEntry(InventoryEntry entry)
    {
        if (entry.IsEmpty || entry.count <= 0) return false;
        
        int remaining = entry.count;
        
        // First pass: existing stacks in hotbar
        for (int i = 0; i < hotbar.Length && remaining > 0; i++)
        {
            if (hotbar[i].CanStackWith(entry) && hotbar[i].count < hotbar[i].MaxStackSize)
            {
                int space = hotbar[i].MaxStackSize - hotbar[i].count;
                int add = Mathf.Min(space, remaining);
                
                if (hotbar[i].IsEmpty)
                {
                    hotbar[i] = entry;
                    hotbar[i].count = add;
                }
                else
                {
                    hotbar[i].count += add;
                }
                remaining -= add;
            }
        }
        
        // Second pass: existing stacks in main inventory
        for (int i = 0; i < mainInventory.Length && remaining > 0; i++)
        {
            if (mainInventory[i].CanStackWith(entry) && mainInventory[i].count < mainInventory[i].MaxStackSize)
            {
                int space = mainInventory[i].MaxStackSize - mainInventory[i].count;
                int add = Mathf.Min(space, remaining);
                
                if (mainInventory[i].IsEmpty)
                {
                    mainInventory[i] = entry;
                    mainInventory[i].count = add;
                }
                else
                {
                    mainInventory[i].count += add;
                }
                remaining -= add;
            }
        }
        
        // Third pass: empty slots in hotbar
        for (int i = 0; i < hotbar.Length && remaining > 0; i++)
        {
            if (hotbar[i].IsEmpty)
            {
                int add = Mathf.Min(entry.MaxStackSize, remaining);
                hotbar[i] = entry;
                hotbar[i].count = add;
                remaining -= add;
            }
        }
        
        // Fourth pass: empty slots in main inventory
        for (int i = 0; i < mainInventory.Length && remaining > 0; i++)
        {
            if (mainInventory[i].IsEmpty)
            {
                int add = Mathf.Min(entry.MaxStackSize, remaining);
                mainInventory[i] = entry;
                mainInventory[i].count = add;
                remaining -= add;
            }
        }
        
        bool fullyAdded = (remaining == 0);
        if (entry.count != remaining) OnInventoryChanged?.Invoke();
        return fullyAdded;
    }

    public bool HasBlockForPlacement(out BlockType type)
    {
        var entry = hotbar[selectedIndex];
        Debug.Log($"HasBlockForPlacement: entry = {entry.entryType}, {(entry.entryType == InventoryEntryType.Block ? entry.blockType.ToString() : entry.itemType.ToString())}, canBePlaced = {entry.CanBePlaced()}");
        if (!entry.IsEmpty && entry.CanBePlaced())
        {
            type = entry.GetPlacementBlock();
            Debug.Log($"HasBlockForPlacement: Placing {type}");
            return true;
        }
        type = BlockType.Air;
        Debug.Log($"HasBlockForPlacement: No block to place");
        return false;
    }

    public bool ConsumeOneFromSelected()
    {
        if (hotbar[selectedIndex].IsEmpty) return false;
        
        hotbar[selectedIndex].count--;
        if (hotbar[selectedIndex].count <= 0)
        {
            hotbar[selectedIndex] = InventoryEntry.Empty;
        }
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void SetSelectedIndex(int index)
    {
        if (index < 0 || index >= hotbar.Length) return;
        if (selectedIndex == index) return;
        selectedIndex = index;
        OnInventoryChanged?.Invoke();
    }

    public void Cycle(int delta)
    {
        if (hotbar.Length == 0) return;
        selectedIndex = (selectedIndex + delta + hotbar.Length) % hotbar.Length;
        OnInventoryChanged?.Invoke();
    }

    public bool IsHotbarSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < hotbarSize;
    }

    public bool IsMainInventorySlot(int slotIndex)
    {
        return slotIndex >= hotbarSize && slotIndex < (hotbarSize + mainInventorySize);
    }

    public bool IsCraftingSlot(int slotIndex)
    {
        int craftingStartIndex = hotbarSize + mainInventorySize;
        return slotIndex >= craftingStartIndex && slotIndex < (craftingStartIndex + craftingSlots);
    }
    
    public bool IsTableCraftingSlot(int slotIndex)
    {
        int tableCraftingStartIndex = hotbarSize + mainInventorySize + craftingSlots;
        return slotIndex >= tableCraftingStartIndex && slotIndex < (tableCraftingStartIndex + tableCraftingSlots);
    }

    public InventoryEntry GetSlot(int slotIndex)
    {
        if (IsHotbarSlot(slotIndex))
        {
            return hotbar[slotIndex];
        }
        else if (IsMainInventorySlot(slotIndex))
        {
            return mainInventory[slotIndex - hotbarSize];
        }
        else if (IsCraftingSlot(slotIndex))
        {
            int craftingStartIndex = hotbarSize + mainInventorySize;
            return craftingInventory[slotIndex - craftingStartIndex];
        }
        else if (IsTableCraftingSlot(slotIndex))
        {
            int tableCraftingStartIndex = hotbarSize + mainInventorySize + craftingSlots;
            return tableCraftingInventory[slotIndex - tableCraftingStartIndex];
        }
        return InventoryEntry.Empty;
    }

    public void SetSlot(int slotIndex, InventoryEntry entry)
    {
        if (IsHotbarSlot(slotIndex))
        {
            hotbar[slotIndex] = entry;
        }
        else if (IsMainInventorySlot(slotIndex))
        {
            mainInventory[slotIndex - hotbarSize] = entry;
        }
        else if (IsCraftingSlot(slotIndex))
        {
            int craftingStartIndex = hotbarSize + mainInventorySize;
            craftingInventory[slotIndex - craftingStartIndex] = entry;
        }
        else if (IsTableCraftingSlot(slotIndex))
        {
            int tableCraftingStartIndex = hotbarSize + mainInventorySize + craftingSlots;
            tableCraftingInventory[slotIndex - tableCraftingStartIndex] = entry;
        }
        
        OnInventoryChanged?.Invoke();
    }

    public void SwapSlots(int slotA, int slotB)
    {
        InventoryEntry entryA = GetSlot(slotA);
        InventoryEntry entryB = GetSlot(slotB);
        SetSlot(slotA, entryB);
        SetSlot(slotB, entryA);
    }

    public bool TryMergeSlots(int fromSlot, int toSlot)
    {
        InventoryEntry from = GetSlot(fromSlot);
        InventoryEntry to = GetSlot(toSlot);

        if (from.IsEmpty) return false;

        if (to.IsEmpty)
        {
            SetSlot(toSlot, from);
            SetSlot(fromSlot, InventoryEntry.Empty);
            return true;
        }

        if (from.CanStackWith(to) && to.count < to.MaxStackSize)
        {
            int spaceAvailable = to.MaxStackSize - to.count;
            int amountToTransfer = Mathf.Min(from.count, spaceAvailable);

            to.count += amountToTransfer;
            from.count -= amountToTransfer;

            if (from.count <= 0)
            {
                from = InventoryEntry.Empty;
            }

            SetSlot(fromSlot, from);
            SetSlot(toSlot, to);
            return true;
        }

        return false;
    }

    public int GetTotalSlotCount()
    {
        return hotbarSize + mainInventorySize + craftingSlots + tableCraftingSlots;
    }

    public InventoryEntry GetSelectedEntry()
    {
        if (selectedIndex >= 0 && selectedIndex < hotbar.Length)
        {
            return hotbar[selectedIndex];
        }
        return InventoryEntry.Empty;
    }
    
    // Legacy compatibility methods
    public ItemStack GetSelectedItem()
    {
        var entry = GetSelectedEntry();
        if (entry.entryType == InventoryEntryType.Block)
            return new ItemStack(entry.blockType, entry.count);
        else
            return new ItemStack(BlockType.Air, 0); // Items show as empty for compatibility
    }
    
    public bool AddItem(BlockType blockType, int quantity = 1)
    {
        return AddBlock(blockType, quantity);
    }

    public bool DropSelectedItem(int quantity = 1)
    {
        if (quantity <= 0) return false;
        
        InventoryEntry selectedEntry = GetSelectedEntry();
        if (selectedEntry.IsEmpty) return false;
        
        int actualQuantity = Mathf.Min(quantity, selectedEntry.count);
        
        hotbar[selectedIndex].count -= actualQuantity;
        if (hotbar[selectedIndex].count <= 0)
        {
            hotbar[selectedIndex] = InventoryEntry.Empty;
        }
        
        OnInventoryChanged?.Invoke();
        
        Debug.Log($"UnifiedPlayerInventory: Dropped {actualQuantity}x {selectedEntry.DisplayName} from slot {selectedIndex}");
        return true;
    }
    
    public bool DropSelectedStack()
    {
        InventoryEntry selectedEntry = GetSelectedEntry();
        if (selectedEntry.IsEmpty) return false;
        
        int quantity = selectedEntry.count;
        
        hotbar[selectedIndex] = InventoryEntry.Empty;
        
        OnInventoryChanged?.Invoke();
        
        Debug.Log($"UnifiedPlayerInventory: Dropped entire stack of {quantity}x {selectedEntry.DisplayName} from slot {selectedIndex}");
        return true;
    }

    public bool RemoveItem(BlockType blockType, int quantity = 1)
    {
        return RemoveEntry(InventoryEntry.CreateBlock(blockType, quantity));
    }
    
    public bool RemoveEntry(InventoryEntry targetEntry)
    {
        if (targetEntry.IsEmpty || targetEntry.count <= 0) return false;
        
        int remaining = targetEntry.count;
        
        // First check hotbar
        for (int i = 0; i < hotbar.Length && remaining > 0; i++)
        {
            if (hotbar[i].CanStackWith(targetEntry))
            {
                int toRemove = Mathf.Min(hotbar[i].count, remaining);
                hotbar[i].count -= toRemove;
                remaining -= toRemove;
                
                if (hotbar[i].count <= 0)
                {
                    hotbar[i] = InventoryEntry.Empty;
                }
            }
        }
        
        // Then check main inventory
        for (int i = 0; i < mainInventory.Length && remaining > 0; i++)
        {
            if (mainInventory[i].CanStackWith(targetEntry))
            {
                int toRemove = Mathf.Min(mainInventory[i].count, remaining);
                mainInventory[i].count -= toRemove;
                remaining -= toRemove;
                
                if (mainInventory[i].count <= 0)
                {
                    mainInventory[i] = InventoryEntry.Empty;
                }
            }
        }
        
        if (remaining != targetEntry.count) OnInventoryChanged?.Invoke();
        return remaining == 0;
    }
}