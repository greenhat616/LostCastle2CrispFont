using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace LostCastle2.CrispChineseFont;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class CrispChineseFontPlugin : BasePlugin
{
    public const string PluginGuid = "local.lostcastle2.crispchinesefont";
    public const string PluginName = "Lost Castle 2 Crisp Chinese Font";
    public const string PluginVersion = "0.1.2";

    internal static ManualLogSource LogSource = null!;
    internal static ConfigEntry<string> FontCandidates = null!;
    internal static ConfigEntry<string> FontFilePath = null!;
    internal static ConfigEntry<int> SamplingPointSize = null!;
    internal static ConfigEntry<int> AtlasPadding = null!;
    internal static ConfigEntry<int> AtlasWidth = null!;
    internal static ConfigEntry<int> AtlasHeight = null!;
    internal static ConfigEntry<float> Sharpness = null!;
    internal static ConfigEntry<bool> ReplaceAllTmpText = null!;
    internal static ConfigEntry<bool> ReplaceLegacyUiText = null!;
    internal static ConfigEntry<bool> PreloadLocalizationGlyphs = null!;
    internal static ConfigEntry<float> ScanIntervalSeconds = null!;
    internal static ConfigEntry<int> MaxPreloadChars = null!;
    internal static ConfigEntry<bool> TryOsFonts = null!;

    public override void Load()
    {
        LogSource = Log;
        BindConfig();
        ClassInjector.RegisterTypeInIl2Cpp<CrispChineseFontRuntime>();
        AddComponent<CrispChineseFontRuntime>();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void BindConfig()
    {
        FontCandidates = Config.Bind(
            "Font",
            "FontCandidates",
            "AlibabaPuHuiTi|Alibaba PuHuiTi|Microsoft YaHei UI|Microsoft YaHei|Noto Sans CJK SC|Source Han Sans SC|SimHei",
            "Preferred font name tokens, separated by '|'. Used when selecting loaded fonts.");
        FontFilePath = Config.Bind(
            "Font",
            "FontFilePath",
            "BepInEx/plugins/LostCastle2.CrispChineseFont/fonts/AlibabaPuHuiTi-3-85-Bold.ttf",
            "Font file path. Relative paths are resolved from the game directory.");
        SamplingPointSize = Config.Bind("Font", "SamplingPointSize", 56, "TMP dynamic SDF sampling point size.");
        AtlasPadding = Config.Bind("Font", "AtlasPadding", 9, "TMP dynamic SDF atlas padding.");
        AtlasWidth = Config.Bind("Font", "AtlasWidth", 4096, "TMP dynamic SDF atlas width.");
        AtlasHeight = Config.Bind("Font", "AtlasHeight", 4096, "TMP dynamic SDF atlas height.");
        Sharpness = Config.Bind("Material", "Sharpness", 0.08f, "TMP material sharpness. Try 0.05 to 0.15.");
        ReplaceAllTmpText = Config.Bind("Apply", "ReplaceAllTmpText", false, "Replace all TMP_Text. False only replaces texts containing CJK characters.");
        ReplaceLegacyUiText = Config.Bind("Apply", "ReplaceLegacyUiText", true, "Also replace legacy UnityEngine.UI.Text when it contains CJK characters.");
        PreloadLocalizationGlyphs = Config.Bind("Apply", "PreloadLocalizationGlyphs", true, "Preload CJK glyphs found in StreamingAssets/Localization JSON files.");
        ScanIntervalSeconds = Config.Bind("Apply", "ScanIntervalSeconds", 1.5f, "How often loaded text objects are scanned.");
        MaxPreloadChars = Config.Bind("Apply", "MaxPreloadChars", 5000, "Maximum unique CJK characters to preload from localization files.");
        TryOsFonts = Config.Bind("Compatibility", "TryOsFonts", false, "Try Unity OS font APIs. Disabled by default because Unity 6000 IL2CPP commonly strips these methods.");
    }
}

public sealed class CrispChineseFontRuntime : MonoBehaviour
{
    private TMP_FontAsset _tmpFont;
    private Font _legacyFont;
    private float _nextScanTime;
    private float _nextFontRetryTime;
    private int _lastTmpCount;
    private int _lastLegacyCount;
    private bool _preloadedGlyphs;

    public CrispChineseFontRuntime(IntPtr ptr)
        : base(ptr)
    {
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        TryCreateFonts();
        TryPreloadLocalizationGlyphs();
    }

    private void Update()
    {
        if (_tmpFont == null)
        {
            if (Time.unscaledTime >= _nextFontRetryTime)
            {
                _nextFontRetryTime = Time.unscaledTime + Math.Max(1f, CrispChineseFontPlugin.ScanIntervalSeconds.Value);
                if (TryCreateFonts())
                {
                    TryPreloadLocalizationGlyphs();
                }
            }

            return;
        }

        TryPreloadLocalizationGlyphs();

        if (Time.unscaledTime < _nextScanTime)
        {
            return;
        }

        _nextScanTime = Time.unscaledTime + Math.Max(0.25f, CrispChineseFontPlugin.ScanIntervalSeconds.Value);
        ApplyToLoadedText();
    }

    private bool TryCreateFonts()
    {
        try
        {
            CreateFonts();
            return _tmpFont != null;
        }
        catch (Exception ex)
        {
            _tmpFont = null;
            CrispChineseFontPlugin.LogSource.LogError($"Font initialization failed: {ex}");
            return false;
        }
    }

    private void CreateFonts()
    {
        if (_tmpFont != null)
        {
            return;
        }

        var fontNames = GetConfiguredFontNames();

        _tmpFont = TryCreateTmpFontAssetFromFile();
        if (_tmpFont != null)
        {
            return;
        }

        _legacyFont = TryCreateFontFromFile();
        if (_legacyFont == null)
        {
            _legacyFont = TryFindSourceFontFromLoadedTmpFontAsset(fontNames);
        }

        if (_legacyFont == null)
        {
            _legacyFont = TryFindLoadedFont(fontNames);
        }

        if (_legacyFont == null && CrispChineseFontPlugin.TryOsFonts.Value)
        {
            _legacyFont = TryCreateDynamicFontFromOs(fontNames);
        }

        if (_legacyFont == null)
        {
            _tmpFont = TryFindLoadedTmpFontAsset(fontNames);
        }

        if (_legacyFont == null && _tmpFont == null)
        {
            CrispChineseFontPlugin.LogSource.LogWarning("No usable Unity/TMP Font is available yet; will retry after the game loads more assets.");
            return;
        }

        if (_tmpFont != null)
        {
            TuneMaterial(_tmpFont.material);
            return;
        }

        _tmpFont = TMP_FontAsset.CreateFontAsset(
            _legacyFont,
            CrispChineseFontPlugin.SamplingPointSize.Value,
            CrispChineseFontPlugin.AtlasPadding.Value,
            GlyphRenderMode.SDFAA,
            CrispChineseFontPlugin.AtlasWidth.Value,
            CrispChineseFontPlugin.AtlasHeight.Value,
            AtlasPopulationMode.Dynamic);

        if (_tmpFont == null)
        {
            CrispChineseFontPlugin.LogSource.LogError($"TMP failed to create a font asset from '{SafeFontName(_legacyFont)}'.");
            return;
        }

        _tmpFont.name = "LC2_CrispChinese_DynamicSDF";
        _tmpFont.isMultiAtlasTexturesEnabled = true;
        TuneMaterial(_tmpFont.material);

        CrispChineseFontPlugin.LogSource.LogInfo(
            $"Created TMP font '{_tmpFont.name}' from '{SafeFontName(_legacyFont)}', " +
            $"sampling={CrispChineseFontPlugin.SamplingPointSize.Value}, " +
            $"padding={CrispChineseFontPlugin.AtlasPadding.Value}, " +
            $"atlas={CrispChineseFontPlugin.AtlasWidth.Value}x{CrispChineseFontPlugin.AtlasHeight.Value}.");
    }

    private static string[] GetConfiguredFontNames()
    {
        var fontNames = CrispChineseFontPlugin.FontCandidates.Value
            .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static name => name.Trim())
            .Where(static name => name.Length > 0)
            .ToArray();

        if (fontNames.Length == 0)
        {
            fontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei" };
        }

        return fontNames;
    }

    private static TMP_FontAsset TryCreateTmpFontAssetFromFile()
    {
        var fontPath = ResolveGamePath(CrispChineseFontPlugin.FontFilePath.Value);
        if (string.IsNullOrWhiteSpace(fontPath) || !File.Exists(fontPath))
        {
            return null;
        }

        try
        {
            var tmpFont = TMP_FontAsset.CreateFontAsset(
                fontPath,
                0,
                CrispChineseFontPlugin.SamplingPointSize.Value,
                CrispChineseFontPlugin.AtlasPadding.Value,
                GlyphRenderMode.SDFAA,
                CrispChineseFontPlugin.AtlasWidth.Value,
                CrispChineseFontPlugin.AtlasHeight.Value);

            if (tmpFont == null)
            {
                CrispChineseFontPlugin.LogSource.LogWarning($"TMP failed to create a font asset from file '{fontPath}'.");
                return null;
            }

            tmpFont.name = "LC2_CrispChinese_DynamicSDF_File";
            tmpFont.isMultiAtlasTexturesEnabled = true;
            TuneMaterial(tmpFont.material);
            CrispChineseFontPlugin.LogSource.LogInfo(
                $"Created TMP font '{tmpFont.name}' from file '{fontPath}', " +
                $"sampling={CrispChineseFontPlugin.SamplingPointSize.Value}, " +
                $"padding={CrispChineseFontPlugin.AtlasPadding.Value}, " +
                $"atlas={CrispChineseFontPlugin.AtlasWidth.Value}x{CrispChineseFontPlugin.AtlasHeight.Value}.");
            return tmpFont;
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning($"TMP font-file creation failed for '{fontPath}': {GetBaseMessage(ex)}");
            return null;
        }
    }

    private static Font TryCreateFontFromFile()
    {
        var fontPath = ResolveGamePath(CrispChineseFontPlugin.FontFilePath.Value);
        if (string.IsNullOrWhiteSpace(fontPath) || !File.Exists(fontPath))
        {
            return null;
        }

        try
        {
            var font = new Font();
            var createFromPath = typeof(Font).GetMethod(
                "Internal_CreateFontFromPath",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (createFromPath == null)
            {
                CrispChineseFontPlugin.LogSource.LogWarning("Font.Internal_CreateFontFromPath is unavailable.");
                return null;
            }

            createFromPath.Invoke(null, new object[] { font, fontPath });
            if (font != null)
            {
                CrispChineseFontPlugin.LogSource.LogInfo($"Created Unity Font from file '{fontPath}' via Internal_CreateFontFromPath.");
                return font;
            }
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning($"Internal Font(path) failed for '{fontPath}': {GetBaseMessage(ex)}");
        }

        return null;
    }

    private static string ResolveGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(Paths.GameRootPath, path.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetBaseMessage(Exception ex)
    {
        return ex.GetBaseException().Message;
    }

    private static Font TryCreateDynamicFontFromOs(string[] fontNames)
    {
        var loggedMissingMethod = false;

        foreach (var fontName in fontNames)
        {
            try
            {
                var font = Font.CreateDynamicFontFromOSFont(fontName, CrispChineseFontPlugin.SamplingPointSize.Value);
                if (font != null)
                {
                    CrispChineseFontPlugin.LogSource.LogInfo($"Created dynamic OS Font from '{fontName}'.");
                    return font;
                }
            }
            catch (MissingMethodException ex)
            {
                if (!loggedMissingMethod)
                {
                    loggedMissingMethod = true;
                    CrispChineseFontPlugin.LogSource.LogWarning(
                        $"Unity OS font loader is unavailable in this IL2CPP runtime; falling back to loaded game fonts. {ex.Message}");
                }

                return null;
            }
            catch (Exception ex)
            {
                CrispChineseFontPlugin.LogSource.LogWarning($"Failed to create OS Font '{fontName}': {ex.Message}");
            }
        }

        return null;
    }

    private static Font TryFindSourceFontFromLoadedTmpFontAsset(string[] preferredNames)
    {
        var tmpFont = TryFindLoadedTmpFontAsset(preferredNames);
        if (tmpFont == null)
        {
            return null;
        }

        try
        {
            var sourceFont = tmpFont.sourceFontFile;
            if (sourceFont != null)
            {
                CrispChineseFontPlugin.LogSource.LogInfo(
                    $"Using source Font '{SafeFontName(sourceFont)}' from loaded TMP font '{SafeObjectName(tmpFont)}'.");
            }

            return sourceFont;
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"Loaded TMP font '{SafeObjectName(tmpFont)}' has no usable source Font: {ex.Message}");
            return null;
        }
    }

    private static TMP_FontAsset TryFindLoadedTmpFontAsset(string[] preferredNames)
    {
        var fontAssets = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (fontAssets == null || fontAssets.Length == 0)
        {
            return null;
        }

        foreach (var preferredName in preferredNames.Concat(GetFallbackFontNameTokens()))
        {
            foreach (var fontAsset in fontAssets)
            {
                if (fontAsset == null)
                {
                    continue;
                }

                var loadedName = SafeObjectName(fontAsset);
                if (loadedName.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CrispChineseFontPlugin.LogSource.LogInfo($"Using loaded TMP font '{loadedName}'.");
                    return fontAsset;
                }
            }
        }

        foreach (var fontAsset in fontAssets)
        {
            if (fontAsset == null)
            {
                continue;
            }

            var loadedName = SafeObjectName(fontAsset);
            if (!string.IsNullOrEmpty(loadedName))
            {
                CrispChineseFontPlugin.LogSource.LogInfo($"Using first loaded TMP font '{loadedName}'.");
                return fontAsset;
            }
        }

        return null;
    }

    private static Font TryFindLoadedFont(string[] preferredNames)
    {
        var fonts = Resources.FindObjectsOfTypeAll<Font>();
        if (fonts == null || fonts.Length == 0)
        {
            return null;
        }

        foreach (var preferredName in preferredNames.Concat(GetFallbackFontNameTokens()))
        {
            foreach (var font in fonts)
            {
                if (font == null)
                {
                    continue;
                }

                var loadedName = SafeFontName(font);
                if (loadedName.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CrispChineseFontPlugin.LogSource.LogInfo($"Using loaded Unity Font '{loadedName}'.");
                    return font;
                }
            }
        }

        foreach (var font in fonts)
        {
            if (font == null)
            {
                continue;
            }

            var loadedName = SafeFontName(font);
            if (!string.IsNullOrEmpty(loadedName))
            {
                CrispChineseFontPlugin.LogSource.LogInfo($"Using first loaded Unity Font '{loadedName}'.");
                return font;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetFallbackFontNameTokens()
    {
        yield return "Alibaba";
        yield return "PuHui";
        yield return "Puhui";
        yield return "Source Han";
        yield return "Noto";
        yield return "Microsoft YaHei";
        yield return "SimHei";
    }

    private static string SafeFontName(Font font)
    {
        try
        {
            return font == null ? string.Empty : font.name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeObjectName(UnityEngine.Object obj)
    {
        try
        {
            return obj == null ? string.Empty : obj.name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void TryPreloadLocalizationGlyphs()
    {
        if (_preloadedGlyphs || _tmpFont == null || !CrispChineseFontPlugin.PreloadLocalizationGlyphs.Value)
        {
            return;
        }

        try
        {
            PreloadLocalizationGlyphs();
            _preloadedGlyphs = true;
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning($"Localization glyph preload failed: {ex.Message}");
        }
    }

    private void PreloadLocalizationGlyphs()
    {
        if (_tmpFont == null || _tmpFont.atlasPopulationMode != AtlasPopulationMode.Dynamic)
        {
            return;
        }

        var localizationDir = Path.Combine(Application.streamingAssetsPath, "Localization");
        if (!Directory.Exists(localizationDir))
        {
            CrispChineseFontPlugin.LogSource.LogWarning($"Localization directory not found: {localizationDir}");
            return;
        }

        var chars = new SortedSet<char>();
        foreach (var file in Directory.EnumerateFiles(localizationDir, "*.json"))
        {
            foreach (var ch in File.ReadAllText(file, Encoding.UTF8))
            {
                if (IsCjk(ch))
                {
                    chars.Add(ch);
                    if (chars.Count >= CrispChineseFontPlugin.MaxPreloadChars.Value)
                    {
                        break;
                    }
                }
            }

            if (chars.Count >= CrispChineseFontPlugin.MaxPreloadChars.Value)
            {
                break;
            }
        }

        if (chars.Count == 0)
        {
            return;
        }

        var preloadText = new string(chars.ToArray());
        if (_tmpFont.TryAddCharacters(preloadText, out var missing) && string.IsNullOrEmpty(missing))
        {
            CrispChineseFontPlugin.LogSource.LogInfo($"Preloaded {chars.Count} CJK glyphs.");
        }
        else
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"Preloaded {chars.Count - missing.Length}/{chars.Count} CJK glyphs; missing={missing.Length}.");
        }
    }

    private void ApplyToLoadedText()
    {
        var tmpCount = 0;
        var legacyCount = 0;

        foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (text == null || !ShouldPatch(text.text))
            {
                continue;
            }

            PatchTmpText(text);
            tmpCount++;
        }

        if (CrispChineseFontPlugin.ReplaceLegacyUiText.Value && _legacyFont != null)
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text == null || !ShouldPatch(text.text))
                {
                    continue;
                }

                text.font = _legacyFont;
                text.SetAllDirty();
                legacyCount++;
            }
        }

        if (tmpCount != _lastTmpCount || legacyCount != _lastLegacyCount)
        {
            _lastTmpCount = tmpCount;
            _lastLegacyCount = legacyCount;
            CrispChineseFontPlugin.LogSource.LogInfo($"Patched TMP_Text={tmpCount}, legacy Text={legacyCount}.");
        }
    }

    private void PatchTmpText(TMP_Text text)
    {
        if (_tmpFont == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(text.text) && _tmpFont.atlasPopulationMode == AtlasPopulationMode.Dynamic)
        {
            _tmpFont.TryAddCharacters(text.text, out _);
        }

        text.font = _tmpFont;
        text.fontSharedMaterial = _tmpFont.material;
        TuneMaterial(text.fontSharedMaterial);
        text.ForceMeshUpdate(true);
    }

    private static bool ShouldPatch(string text)
    {
        return CrispChineseFontPlugin.ReplaceAllTmpText.Value || ContainsCjk(text);
    }

    private static bool ContainsCjk(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (IsCjk(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCjk(char ch)
    {
        return ch is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';
    }

    private static void TuneMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Sharpness"))
        {
            material.SetFloat("_Sharpness", CrispChineseFontPlugin.Sharpness.Value);
        }

        if (material.HasProperty("_OutlineSoftness"))
        {
            material.SetFloat("_OutlineSoftness", 0f);
        }

        if (material.HasProperty("_UnderlaySoftness"))
        {
            material.SetFloat("_UnderlaySoftness", 0f);
        }

        if (material.HasProperty("_MaskSoftnessX"))
        {
            material.SetFloat("_MaskSoftnessX", 0f);
        }

        if (material.HasProperty("_MaskSoftnessY"))
        {
            material.SetFloat("_MaskSoftnessY", 0f);
        }
    }
}
