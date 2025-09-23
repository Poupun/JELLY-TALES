using UnityEngine;

[System.Serializable]
public enum ItemType
{
    None = 0,
    // Tools & Equipment
    Stick = 1000,
    WoodPickaxe = 1001,
    StonePickaxe = 1002,
    IronPickaxe = 1003,
    WoodAxe = 1010,
    StoneAxe = 1011,
    IronAxe = 1012,
    WoodShovel = 1020,
    StoneShovel = 1021,
    IronShovel = 1022,
    
    // Crafting Materials  
    Coal = 1100,
    IronIngot = 1101,
    GoldIngot = 1102,
    Diamond = 1103,
    
    // Food & Consumables
    Apple = 1200,
    Bread = 1201,
    
    // Special Items
    CraftingTable = 1300, // Placeable item, but still an item
}

[System.Serializable]
public enum ItemCategory
{
    Tool,
    Material,
    Food,
    Placeable, // Items that can be placed as blocks (like crafting table)
    Misc
}

[System.Serializable] 
public class ItemData
{
    public ItemType itemType;
    public string itemName;
    public string displayName;
    public ItemCategory category;
    public int maxStackSize = 64;
    public Sprite icon;
    public Texture2D texture;
    
    // Placeable items can become blocks when placed
    public bool canBePlaced = false;
    public BlockType placesAsBlock = BlockType.Air;
    
    public ItemData(ItemType type, string name, ItemCategory cat, int stackSize = 64)
    {
        itemType = type;
        itemName = name;
        displayName = name;
        category = cat;
        maxStackSize = stackSize;
    }
}