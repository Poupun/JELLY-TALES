# 🌊 Water Fog Quick Reference

## 🎮 Where to Find Controls

**WorldGenerator Inspector** → Scroll to:
- `Water Depth Fog (Surface View)` section
- `Underwater Fog (When Submerged)` section

---

## 🎛️ Key Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Enable Water Depth Fog** | ✓ | Toggles surface depth effect |
| **Deep Water Color** | RGB(0.05, 0.15, 0.3) | Color of deep water |
| **Max Water Depth** | 20 blocks | Depth for full darkening |
| **Depth Fog Intensity** | 0.85 | Effect strength (0-1) |
| **Enable Underwater Fog** | ✓ | Toggles underwater fog |
| **Underwater Fog Density** | 0.08 | Thickness of fog |
| **Underwater Visibility Range** | 35 blocks | View distance |

---

## 🎨 Quick Presets

**Copy these values into WorldGenerator:**

### Minecraft Classic
```
Deep Water Color: (0.02, 0.08, 0.20)
Max Water Depth: 15
Depth Fog Intensity: 0.90
Underwater Fog Density: 0.10
```

### Clear Tropical
```
Deep Water Color: (0.10, 0.30, 0.50)
Max Water Depth: 30
Depth Fog Intensity: 0.70
Underwater Fog Density: 0.05
```

### Murky Swamp
```
Deep Water Color: (0.05, 0.12, 0.08)
Max Water Depth: 10
Depth Fog Intensity: 0.95
Underwater Fog Density: 0.15
```

---

## ⚡ Quick Fixes

**"Can't see depth from surface"**
→ Increase `Depth Fog Intensity` to 0.90

**"Water too dark"**
→ Increase `Max Water Depth` to 30+

**"Can't see underwater"**
→ Decrease `Underwater Fog Density` to 0.05

**"Fog too strong"**
→ Decrease `Depth Fog Intensity` to 0.60

---

## ✅ What Works Now

✓ Surface depth fog visible from all angles
✓ Underwater fog when camera submerged
✓ Smooth transitions entering/exiting water
✓ Real-time tweaking during Play mode
✓ Zero performance impact
✓ All settings in WorldGenerator inspector

---

**That's it! Enjoy your water fog! 🌊**