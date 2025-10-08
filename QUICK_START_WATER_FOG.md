# 🚀 Quick Start: Water Depth Fog

## Instant Setup (2 Minutes)

Your water depth fog system is **already integrated** and ready to use! Just follow these steps:

### 1. Start Your Game ▶️

Hit Play in Unity. The system automatically:
- Creates `UnderwaterFogManager` GameObject
- Configures fog properties
- Applies to all water blocks

### 2. Test The Effect 🧪

**From Surface:**
1. Look down at ocean/water from above
2. You should see water getting darker with depth
3. Deep water appears much darker than shallow water

**Underwater:**
1. Dive into water (swim down)
2. Fog should smoothly fade in
3. Distant blocks become harder to see
4. Exit water - fog smoothly fades out

### 3. Adjust Settings (Optional) ⚙️

Find `UnderwaterFogManager` in Hierarchy and tweak:

**Surface Fog:**
```
Deep Water Color: Darker = more dramatic depth
Max Depth Darkening: 20 blocks (adjust if needed)
```

**Underwater Fog:**
```
Fog Density: 0.08 (higher = less visibility)
Visibility Range: 30 blocks
Fog Color: Blue-green tint
```

---

## ✅ Verification Checklist

Make sure everything works:

- [ ] No console errors on start
- [ ] Ocean looks darker at depth from surface
- [ ] Underwater fog appears when submerged
- [ ] Smooth transitions in/out of water
- [ ] Good frame rate (no lag)

---

## 🎨 Recommended Settings

**For Minecraft-Style Look:**
```
Deep Water Color: RGB(0.02, 0.10, 0.25) - Very dark blue
Max Depth: 15 blocks
Underwater Fog Density: 0.10
Underwater Fog Color: RGB(0.08, 0.25, 0.35)
```

**For Clear Tropical Water:**
```
Deep Water Color: RGB(0.10, 0.35, 0.50) - Lighter blue
Max Depth: 30 blocks
Underwater Fog Density: 0.05
Underwater Fog Color: RGB(0.15, 0.40, 0.55)
```

**For Murky/Swamp Water:**
```
Deep Water Color: RGB(0.05, 0.15, 0.12) - Dark green
Max Depth: 10 blocks
Underwater Fog Density: 0.15
Underwater Fog Color: RGB(0.10, 0.20, 0.15)
```

---

## 🎯 That's It!

Your water now has professional depth fog like Minecraft. For detailed technical info, see [WATER_DEPTH_FOG_README.md](WATER_DEPTH_FOG_README.md).

**Enjoy your enhanced water visuals! 🌊**