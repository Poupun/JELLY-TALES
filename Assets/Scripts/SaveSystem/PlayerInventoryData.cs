using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PlayerInventoryData
{
    [System.Serializable]
    public class InventorySlotData
    {
        public ItemType itemType = ItemType.None;
        public BlockType blockType = BlockType.Air;
        public int quantity = 0;
        
        public InventorySlotData() { }
        
        public InventorySlotData(ItemType item, int qty)
        {
            itemType = item;
            quantity = qty;
        }
        
        public InventorySlotData(BlockType block, int qty)
        {
            blockType = block;
            quantity = qty;
        }
        
        public bool IsEmpty()
        {
            return quantity <= 0 || (itemType == ItemType.None && blockType == BlockType.Air);
        }
    }
    
    public List<InventorySlotData> hotbarSlots = new List<InventorySlotData>();
    public List<InventorySlotData> inventorySlots = new List<InventorySlotData>();
    public int selectedHotbarSlot = 0;
    
    public PlayerInventoryData()
    {
        // Initialize with empty slots (9 hotbar slots + 36 inventory slots = 45 total like Minecraft)
        for (int i = 0; i < 9; i++)
        {
            hotbarSlots.Add(new InventorySlotData());
        }
        
        for (int i = 0; i < 36; i++)
        {
            inventorySlots.Add(new InventorySlotData());
        }
    }
}