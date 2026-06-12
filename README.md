# LostCastle2CrispFont

BepInEx 6 IL2CPP mod for Lost Castle 2. It improves blurry Simplified Chinese UI text by replacing loaded TextMeshPro text with a high-sampling dynamic SDF font at runtime.

## What It Fixes

Lost Castle 2 ships its main Simplified Chinese TMP asset as `Alibaba-PuHuiTi-Bold SDF` with a low source sampling size. Many CJK glyphs are effectively around 20 pixels in the atlas, so small UI text becomes soft after SDF sampling, bilinear filtering, outlines, and UI scaling.

This mod creates a dynamic TMP SDF font at `SamplingPointSize = 56`, preloads CJK glyphs from localization files, and patches loaded `TMP_Text` objects that contain CJK characters.

## Install

1. Install a recent BepInEx 6 IL2CPP x64 build into the Lost Castle 2 game directory.
2. Start the game once so BepInEx generates `BepInEx/interop`, then close the game.
3. Copy `LostCastle2.CrispChineseFont.dll` to:

   ```text
   <Lost Castle 2>/BepInEx/plugins/LostCastle2.CrispChineseFont/
   ```

4. Copy `AlibabaPuHuiTi-3-85-Bold.ttf` to:

   ```text
   <Lost Castle 2>/BepInEx/plugins/LostCastle2.CrispChineseFont/fonts/
   ```

The ready-to-install release zip contains both files in the correct layout.

## Build

The project expects BepInEx and generated IL2CPP interop assemblies in the game directory:

```powershell
.\scripts\build.ps1 -GameDir "D:\Program Files (x86)\Steam\steamapps\common\Lost Castle 2"
```

## Deploy Locally

```powershell
.\scripts\deploy.ps1 -GameDir "D:\Program Files (x86)\Steam\steamapps\common\Lost Castle 2"
```

`deploy.ps1` builds the plugin, copies the DLL, and copies `assets/fonts/AlibabaPuHuiTi-3-85-Bold.ttf` into the plugin's `fonts` directory.

## Configuration

After the first launch, BepInEx creates:

```text
BepInEx/config/local.lostcastle2.crispchinesefont.cfg
```

Useful settings:

- `FontFilePath`: font file used by the high-sampling TMP path.
- `SamplingPointSize`: default `56`.
- `AtlasPadding`: default `9`.
- `AtlasWidth` / `AtlasHeight`: default `4096`.
- `Sharpness`: default `0.08`.
- `ReplaceAllTmpText`: default `false`; set to `true` only if some CJK text is missed.
- `TryOsFonts`: default `false`; Unity 6000 IL2CPP commonly strips OS font methods.

Expected log line for the preferred path:

```text
Created TMP font 'LC2_CrispChinese_DynamicSDF_File' from file ...
```

If that line is missing, check `BepInEx/LogOutput.log`.

## Notes

- The mod does not rewrite game bundles.
- The bundled font file is extracted from the game's own font asset bundle for runtime use.
- If the font file is missing, the plugin falls back to loaded game TMP assets, which is safer but less sharp.
