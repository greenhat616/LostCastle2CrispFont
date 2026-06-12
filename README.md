# LostCastle2CrispFont

BepInEx 6 IL2CPP mod for Lost Castle 2. It improves blurry TextMeshPro UI text by replacing the game's low-sampling East Asian font assets with high-sampling dynamic SDF replacements at runtime.

## What It Fixes

Lost Castle 2 ships several East Asian TMP assets with low source sampling sizes. Many glyphs are effectively around 20 pixels in the atlas, so small UI text becomes soft after SDF sampling, bilinear filtering, outlines, and UI scaling.

This mod creates high-sampling dynamic TMP SDF replacements at `SamplingPointSize = 56`, preloads East Asian glyphs from localization files, and patches loaded `TMP_Text` objects whose original font asset matches one of the known game font profiles.

Currently matched profiles:

- `Alibaba-PuHuiTi-Bold SDF`
- `Alibaba-PuHuiTi-Bold SDF - Chat`
- `Alibaba-PuHuiTi-Bold SDF - BossTitle`
- `Alibaba-PuHuiTi-Bold SDF - TCN`
- `Alibaba-PuHuiTi-Bold SDF - JP`
- `Alibaba-PuHuiTi-Bold SDF - KR`
- `SourceHanSansCN-Bold-Hunter SDF`
- `SourceHanSansCN-Bold-Hunter SDF-ASCII`

English text is also sharpened when it is rendered through one of the matched font assets.

For each profile, the mod tries sources in this order:

- The original TMP font asset's embedded `sourceFontFile`
- A matching Unity `Font` already loaded by the game
- A configured TTF file under the plugin's `fonts` directory

## Install

1. Install a recent BepInEx 6 IL2CPP x64 build into the Lost Castle 2 game directory.
2. Start the game once so BepInEx generates `BepInEx/interop`, then close the game.
3. Copy `LostCastle2.CrispChineseFont.dll` to:

   ```text
   <Lost Castle 2>/BepInEx/plugins/LostCastle2.CrispChineseFont/
   ```

4. Copy the bundled font files to:

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

`deploy.ps1` builds the plugin, copies the DLL, and copies every file from `assets/fonts/` into the plugin's `fonts` directory.

## Configuration

After the first launch, BepInEx creates:

```text
BepInEx/config/local.lostcastle2.crispchinesefont.cfg
```

Useful settings:

- `FontFilePath`: font file used by the high-sampling TMP path.
- `SourceHanAsciiFontFilePath`: font file used by the `SourceHanSansCN-Bold-Hunter` Simplified Chinese profile.
- `BossTitleFontFilePath`: font file used by the BossTitle profile.
- `TraditionalChineseFontFilePath`: optional font file used by the Traditional Chinese profile.
- `JapaneseFontFilePath`: optional font file used by the Japanese profile.
- `KoreanFontFilePath`: font file used by the Korean profile.
- `SamplingPointSize`: default `56`.
- `AtlasPadding`: default `9`.
- `AtlasWidth` / `AtlasHeight`: default `4096`.
- `Sharpness`: default `0.08`.
- `PreferEmbeddedSourceFont`: default `true`; use the original TMP source font before configured TTF files.
- `EnableImmediateHooks`: default `true`; patch `TMP_Text` from Harmony hooks instead of waiting only for periodic scanning.
- `ScanIntervalSeconds`: fallback scan interval, default `1.5`.

Expected log line for the preferred path:

```text
Created TMP replacement 'LC2_Crisp_AlibabaPuHuiTi' ...
```

If that line is missing, check `BepInEx/LogOutput.log`.

## Notes

- The mod does not rewrite game bundles.
- The bundled font files are extracted from the game's own font asset bundles for runtime fallback use.
- The original TMP material is cloned for each patched text material, then pointed at the replacement atlas, so colors, outlines, underlay, and masking behavior are preserved where possible.
- If a configured font file is missing, the plugin still tries the original embedded source font and loaded game fonts first. It does not call Unity's OS font APIs.
