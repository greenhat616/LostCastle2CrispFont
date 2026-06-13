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

## Font Replacer

Each profile can either be **replaced** with a font you supply, or left on the game's own font (crisp re-render only).

- When a profile's replacement is **on**, the mod builds the high-sampling SDF asset from that profile's `FontFilePath`.
- When it is **off** (or the master switch is off, or the file is missing), the mod falls back to the original game font, trying in order:
  - The original TMP font asset's embedded `sourceFontFile`
  - A matching Unity `Font` already loaded by the game
  - The profile's configured `FontFilePath` (last resort)

Default mapping shipped with the mod (West-fantasy cartoon set):

| Profile | Default font | Enabled by default |
|---|---|---|
| `AlibabaPuHuiTi` (main UI / body) | 华康圆体W7 | yes |
| `Chat` (dialogue) | 站酷快乐体 | yes |
| `BossTitle` (big titles) | 汉仪雅酷黑 75W | yes |
| `SourceHanSansCN` (ASCII / numbers) | 优设标题黑 | no |
| `TraditionalChinese` / `Japanese` | — | no |
| `Korean` | Alibaba Sans KR | no |

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

Global settings:

- `[General] EnableFontReplacer`: master switch, default `true`. When `false`, every profile keeps the game font (crisp re-render only).
- `[Font] SamplingPointSize`: default `56`. Global SDF sampling; profiles can override.
- `[Font] AtlasPadding`: default `9`.
- `[Font] AtlasWidth` / `AtlasHeight`: default `4096`.
- `[Material] Sharpness`: default `0.08`. Global; profiles can override.
- `[Apply] PreferEmbeddedSourceFont`: default `true`; for non-replacing profiles, use the original embedded source font before any fallback file.
- `[Apply] ReplaceLegacyUiText`, `PreloadLocalizationGlyphs`, `ScanIntervalSeconds`, `MaxPreloadChars`, `EnableImmediateHooks`.

Per-profile settings (one section each, e.g. `[Profile.AlibabaPuHuiTi]`, `[Profile.Chat]`, `[Profile.BossTitle]`, `[Profile.SourceHanSansCN]`, `[Profile.TraditionalChinese]`, `[Profile.Japanese]`, `[Profile.Korean]`):

- `Enabled`: replace this category's font with `FontFilePath`. When `false`, the game font is reused.
- `FontFilePath`: replacement `.ttf`/`.otf`. Relative paths resolve from the game directory.
- `Sharpness`: per-profile material sharpness. Negative (default `-1`) means use the global value.
- `SamplingPointSize`: per-profile SDF sampling. Zero (default) means use the global value.

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
