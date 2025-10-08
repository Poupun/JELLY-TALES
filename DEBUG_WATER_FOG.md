# 🔍 Debug Water Depth Fog - Troubleshooting Guide

## 🧪 Testing Steps

Follow these steps to identify why depth fog isn't visible from the surface:

### Step 1: Check Console Logs on Startup

1. **Start the game in Play mode**
2. **Look for these messages in Console:**

```
✅ "Initial water depth fog properties applied"
✅ "Updating water depth fog properties - Shader: Custom/WaterWaves"
✅ "Shader properties - DepthFogColor: True, MaxDepth: True, Intensity: True"
✅ "Set _DepthFogColor to RGBA(...)"
✅ "Set _MaxDepthDarkening to 20"
✅ "Set _DepthFogIntensity to 0.85"
```

**If you see warnings:**
- ❌ "blockMaterials not ready" → Materials not initialized
- ❌ "water material is null" → Water material missing
- ❌ "Shader properties - False, False, False" → Wrong shader being used!

### Step 2: Verify Water Material Shader

1. **Find a water block in your scene**
2. **Check its MeshRenderer → Materials**
3. **Water material should use shader:** `Custom/WaterWaves`

**If using different shader:**
- The depth fog properties won't exist
- Need to ensure water uses WaterWaves shader

### Step 3: Check WorldGenerator Settings

Open WorldGenerator inspector and verify:

```
✅ Enable Water Depth Fog: Checked
✅ Deep Water Color: Dark blue (NOT transparent!)
✅ Max Water Depth: 15-25 (NOT 100+)
✅ Depth Fog Intensity: 0.80-0.95 (NOT 0.0)
```

**Common mistakes:**
- Intensity set to 0
- Max Water Depth too high (try 20)
- Deep Water Color is same as water color
- Enable Water Depth Fog unchecked

### Step 4: Test Viewing Angles

Look at ocean from different positions:

**From above (flying/high ground):**
- Should see water getting darker with depth
- Deep areas should be noticeably darker than shallow

**From side (beach level):**
- Should see depth gradient
- Water far from shore should be darker

**Underwater:**
- Should switch to distance fog (different effect)

### Step 5: Extreme Values Test

Try these extreme settings to make it obvious:

```
Deep Water Color: RGB(0, 0, 0) - Pure black
Max Water Depth: 10 blocks
Depth Fog Intensity: 1.0 - Maximum
```

**If STILL no effect:**
- Shader not being applied correctly
- Check Step 2 again

### Step 6: Check Shader Compilation

1. **Select WaterWaves.shader in Project**
2. **Look for compilation errors**
3. **Should say "Compiled successfully"**

**If errors:**
- Shader won't work
- Fix compilation issues first

## 🔧 Quick Fixes

### Fix 1: Force Shader Refresh
```
1. Find water material in Project
2. Select it
3. Change shader to "Universal Render Pipeline/Lit"
4. Change back to "Custom/WaterWaves"
```

### Fix 2: Force Material Update
```
1. Stop Play mode
2. Delete "Library" folder (Unity will regenerate)
3. Start Play mode again
```

### Fix 3: Manual Material Check
```csharp
// Add this to WorldGenerator.Start() after CreateBlockMaterials()
var waterMat = blockMaterials[(int)BlockType.Water];
Debug.Log($"Water material shader: {waterMat.shader.name}");
Debug.Log($"Has _DepthFogColor: {waterMat.HasProperty("_DepthFogColor")}");
```

## 🎯 Expected Behavior

### What You Should See:

**Looking down at ocean:**
- Shallow water (1-5 blocks deep): Normal water color
- Medium depth (10-15 blocks): Noticeably darker
- Deep ocean (20+ blocks): Very dark, almost black

**The effect should be gradual:**
```
Surface → Slightly darker → Darker → Much darker → Very dark
```

## 📊 Diagnostic Checklist

Check console logs for:
- [ ] "Initial water depth fog properties applied" message
- [ ] No warnings about missing properties
- [ ] Shader name is "Custom/WaterWaves"
- [ ] All three properties (Color, MaxDepth, Intensity) return True

Check inspector settings:
- [ ] Enable Water Depth Fog is checked
- [ ] Depth Fog Intensity is above 0.5
- [ ] Max Water Depth is reasonable (15-25)
- [ ] Deep Water Color is dark

Test in-game:
- [ ] Water exists in scene
- [ ] Camera is above water (not underwater)
- [ ] Looking at water from various angles
- [ ] Ocean has depth variation (not all same level)

## 🚨 Common Issues

### Issue: "Properties return False in console"
**Solution:** Water material isn't using WaterWaves shader
- Check material shader assignment
- May be using fallback shader

### Issue: "No visible difference at any setting"
**Solution:** Shader code may not be executing
- Verify shader compiles without errors
- Check that _DepthFogIntensity > 0

### Issue: "Only works underwater, not from surface"
**Solution:** Check if camera is detected as underwater
- UnderwaterFogManager may be interfering
- Check _UnderwaterFogEnabled global property

## 📝 Debug Output Example

**Good output (working):**
```
Initial water depth fog properties applied
Updating water depth fog properties - Shader: Custom/WaterWaves
Shader properties - DepthFogColor: True, MaxDepth: True, Intensity: True
Set _DepthFogColor to RGBA(0.050, 0.150, 0.300, 1.000)
Set _MaxDepthDarkening to 20
Set _DepthFogIntensity to 0.85 (enabled: True)
Water depth fog properties update complete
```

**Bad output (not working):**
```
UpdateWaterDepthFogProperties: water material is null
```
or
```
Shader properties - DepthFogColor: False, MaxDepth: False, Intensity: False
```

---

## 📤 Report Results

After testing, report:
1. Console log output (copy/paste)
2. Water material shader name
3. Settings you tried
4. What you see vs what you expect

This will help identify the exact issue! 🎯