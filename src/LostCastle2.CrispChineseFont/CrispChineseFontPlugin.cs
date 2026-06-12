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
using HarmonyLib;
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
    public const string PluginVersion = "0.3.1";

    internal static ManualLogSource LogSource = null!;
    internal static ConfigEntry<string> FontFilePath = null!;
    internal static ConfigEntry<string> SourceHanAsciiFontFilePath = null!;
    internal static ConfigEntry<string> BossTitleFontFilePath = null!;
    internal static ConfigEntry<string> TraditionalChineseFontFilePath = null!;
    internal static ConfigEntry<string> JapaneseFontFilePath = null!;
    internal static ConfigEntry<string> KoreanFontFilePath = null!;
    internal static ConfigEntry<int> SamplingPointSize = null!;
    internal static ConfigEntry<int> AtlasPadding = null!;
    internal static ConfigEntry<int> AtlasWidth = null!;
    internal static ConfigEntry<int> AtlasHeight = null!;
    internal static ConfigEntry<float> Sharpness = null!;
    internal static ConfigEntry<bool> ReplaceLegacyUiText = null!;
    internal static ConfigEntry<bool> PreloadLocalizationGlyphs = null!;
    internal static ConfigEntry<float> ScanIntervalSeconds = null!;
    internal static ConfigEntry<int> MaxPreloadChars = null!;
    internal static ConfigEntry<bool> PreferEmbeddedSourceFont = null!;
    internal static ConfigEntry<bool> EnableImmediateHooks = null!;

    private Harmony _harmony;

    public override void Load()
    {
        LogSource = Log;
        BindConfig();
        ClassInjector.RegisterTypeInIl2Cpp<CrispChineseFontRuntime>();
        InstallHooks();
        AddComponent<CrispChineseFontRuntime>();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void BindConfig()
    {
        FontFilePath = Config.Bind(
            "Font",
            "FontFilePath",
            "BepInEx/plugins/LostCastle2.CrispChineseFont/fonts/AlibabaPuHuiTi-3-85-Bold.ttf",
            "Font file path for the game's AlibabaPuHuiTi Simplified Chinese TMP profile. Relative paths are resolved from the game directory.");
        SourceHanAsciiFontFilePath = Config.Bind(
            "Font",
            "SourceHanAsciiFontFilePath",
            "BepInEx/plugins/LostCastle2.CrispChineseFont/fonts/Alibaba-PuHuiTi-Bold.ttf",
            "Font file path for the game's SourceHanSansCN Simplified Chinese TMP profile. Relative paths are resolved from the game directory.");
        BossTitleFontFilePath = Config.Bind(
            "Font",
            "BossTitleFontFilePath",
            "BepInEx/plugins/LostCastle2.CrispChineseFont/fonts/AlibabaPuHuiTi-3-85-Bold.ttf",
            "Font file path for the game's BossTitle TMP profile. Relative paths are resolved from the game directory.");
        TraditionalChineseFontFilePath = Config.Bind(
            "Font",
            "TraditionalChineseFontFilePath",
            "",
            "Optional font file path for the game's Traditional Chinese TMP profile. Empty means embedded/loaded source fonts only.");
        JapaneseFontFilePath = Config.Bind(
            "Font",
            "JapaneseFontFilePath",
            "",
            "Optional font file path for the game's Japanese TMP profile. Empty means embedded/loaded source fonts only.");
        KoreanFontFilePath = Config.Bind(
            "Font",
            "KoreanFontFilePath",
            "BepInEx/plugins/LostCastle2.CrispChineseFont/fonts/AlibabaSansKR-Bold.ttf",
            "Font file path for the game's Korean TMP profile. Relative paths are resolved from the game directory.");
        SamplingPointSize = Config.Bind("Font", "SamplingPointSize", 56, "TMP dynamic SDF sampling point size.");
        AtlasPadding = Config.Bind("Font", "AtlasPadding", 9, "TMP dynamic SDF atlas padding.");
        AtlasWidth = Config.Bind("Font", "AtlasWidth", 4096, "TMP dynamic SDF atlas width.");
        AtlasHeight = Config.Bind("Font", "AtlasHeight", 4096, "TMP dynamic SDF atlas height.");
        Sharpness = Config.Bind("Material", "Sharpness", 0.08f, "TMP material sharpness. Try 0.05 to 0.15.");
        ReplaceLegacyUiText = Config.Bind("Apply", "ReplaceLegacyUiText", true, "Also replace legacy UnityEngine.UI.Text when it contains East Asian characters.");
        PreloadLocalizationGlyphs = Config.Bind("Apply", "PreloadLocalizationGlyphs", true, "Preload East Asian glyphs found in StreamingAssets/Localization JSON files.");
        ScanIntervalSeconds = Config.Bind("Apply", "ScanIntervalSeconds", 1.5f, "How often loaded text objects are scanned as a fallback.");
        MaxPreloadChars = Config.Bind("Apply", "MaxPreloadChars", 5000, "Maximum unique East Asian characters to preload from localization files.");
        PreferEmbeddedSourceFont = Config.Bind("Apply", "PreferEmbeddedSourceFont", true, "Prefer each original TMP font asset's embedded source Font before configured TTF files.");
        EnableImmediateHooks = Config.Bind("Apply", "EnableImmediateHooks", true, "Patch TMP_Text immediately from Harmony hooks. Periodic scanning remains as a fallback.");
    }

    private void InstallHooks()
    {
        if (!EnableImmediateHooks.Value)
        {
            return;
        }

        try
        {
            _harmony = new Harmony(PluginGuid);
            var patchedCount = TextMeshProHooks.Install(_harmony);
            Log.LogInfo($"Installed {patchedCount} TMP_Text immediate hook(s).");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"TMP_Text immediate hooks failed; periodic scanning will still work. {ex.GetBaseException().Message}");
        }
    }
}

public sealed class CrispChineseFontRuntime : MonoBehaviour
{
    private static CrispChineseFontRuntime _instance;

    private readonly Dictionary<string, TMP_FontAsset> _replacementFonts = new();
    private readonly Dictionary<string, Material> _replacementMaterials = new();
    private readonly Dictionary<int, TMP_Text> _pendingHookTexts = new();
    private readonly HashSet<int> _replacementFontIds = new();
    private readonly HashSet<int> _preloadedFontIds = new();
    private readonly HashSet<string> _loggedMissingProfiles = new();
    private Font _legacyFont;
    private float _nextScanTime;
    private int _lastTmpCount;
    private int _lastLegacyCount;
    private bool _triedLegacyFont;
    private bool _isPatching;

    public CrispChineseFontRuntime(IntPtr ptr)
        : base(ptr)
    {
    }

    private void Awake()
    {
        _instance = this;
        DontDestroyOnLoad(gameObject);
        TryCreateLegacyFont();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    internal static void QueueFromHook(TMP_Text text)
    {
        var instance = _instance;
        if (instance == null || text == null)
        {
            return;
        }

        instance.QueueTmpText(text);
    }

    private void Update()
    {
        ApplyQueuedText();

        if (Time.unscaledTime < _nextScanTime)
        {
            return;
        }

        _nextScanTime = Time.unscaledTime + Math.Max(0.25f, CrispChineseFontPlugin.ScanIntervalSeconds.Value);
        ApplyToLoadedText();
    }

    private void TryCreateLegacyFont()
    {
        if (_triedLegacyFont || _legacyFont != null)
        {
            return;
        }

        _triedLegacyFont = true;
        _legacyFont = TryCreateFontFromFile(CrispChineseFontPlugin.FontFilePath.Value);
    }

    private void QueueTmpText(TMP_Text text)
    {
        if (text == null || _isPatching)
        {
            return;
        }

        _pendingHookTexts[text.GetInstanceID()] = text;
    }

    private void ApplyQueuedText()
    {
        if (_pendingHookTexts.Count == 0)
        {
            return;
        }

        var pending = _pendingHookTexts.Values.ToArray();
        _pendingHookTexts.Clear();

        foreach (var text in pending)
        {
            if (text != null)
            {
                TryPatchTmpText(text);
            }
        }
    }

    private TMP_FontAsset GetOrCreateReplacementFont(TMP_FontAsset originalFont, string profileKey)
    {
        if (_replacementFonts.TryGetValue(profileKey, out var existingFont) && existingFont != null)
        {
            return existingFont;
        }

        var originalFontName = SafeObjectName(originalFont);
        var fontPath = GetFontPathForProfile(profileKey);
        var replacementFont =
            TryCreateTmpFontAssetFromOriginalSource(originalFont, profileKey, originalFontName) ??
            TryCreateTmpFontAssetFromLoadedFont(profileKey, originalFontName) ??
            TryCreateTmpFontAssetFromFile(fontPath, profileKey, originalFontName);
        if (replacementFont == null)
        {
            LogMissingProfileOnce(
                profileKey,
                $"No high-resolution replacement could be created for profile '{profileKey}' from embedded source, loaded fonts, or configured path '{fontPath}'.");
            return null;
        }

        _replacementFonts[profileKey] = replacementFont;
        _replacementFontIds.Add(replacementFont.GetInstanceID());
        TryPreloadLocalizationGlyphs(replacementFont);
        return replacementFont;
    }

    private TMP_FontAsset TryCreateTmpFontAssetFromOriginalSource(TMP_FontAsset originalFont, string profileKey, string originalFontName)
    {
        if (!CrispChineseFontPlugin.PreferEmbeddedSourceFont.Value || originalFont == null)
        {
            return null;
        }

        try
        {
            var sourceFont = originalFont.sourceFontFile;
            if (sourceFont == null)
            {
                return null;
            }

            return TryCreateTmpFontAssetFromFont(sourceFont, profileKey, originalFontName, $"embedded source Font '{SafeFontName(sourceFont)}'");
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"Original TMP font '{originalFontName}' has no usable embedded source Font: {GetBaseMessage(ex)}");
            return null;
        }
    }

    private static TMP_FontAsset TryCreateTmpFontAssetFromLoadedFont(string profileKey, string originalFontName)
    {
        var tokens = GetSourceFontNameTokens(profileKey);
        if (tokens.Length == 0)
        {
            return null;
        }

        var fonts = Resources.FindObjectsOfTypeAll<Font>();
        if (fonts == null || fonts.Length == 0)
        {
            return null;
        }

        foreach (var token in tokens)
        {
            foreach (var font in fonts)
            {
                if (font == null)
                {
                    continue;
                }

                var loadedName = SafeFontName(font);
                if (!string.IsNullOrEmpty(loadedName) &&
                    loadedName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return TryCreateTmpFontAssetFromFont(font, profileKey, originalFontName, $"loaded Unity Font '{loadedName}'");
                }
            }
        }

        return null;
    }

    private static TMP_FontAsset TryCreateTmpFontAssetFromFont(Font sourceFont, string profileKey, string originalFontName, string sourceDescription)
    {
        if (sourceFont == null)
        {
            return null;
        }

        try
        {
            var tmpFont = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                CrispChineseFontPlugin.SamplingPointSize.Value,
                CrispChineseFontPlugin.AtlasPadding.Value,
                GlyphRenderMode.SDFAA,
                CrispChineseFontPlugin.AtlasWidth.Value,
                CrispChineseFontPlugin.AtlasHeight.Value,
                AtlasPopulationMode.Dynamic);

            return FinalizeReplacementFont(tmpFont, profileKey, originalFontName, sourceDescription);
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"TMP font creation failed from {sourceDescription} for '{originalFontName}': {GetBaseMessage(ex)}");
            return null;
        }
    }

    private static TMP_FontAsset TryCreateTmpFontAssetFromFile(string configuredPath, string profileKey, string originalFontName)
    {
        var fontPath = ResolveGamePath(configuredPath);
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

            return FinalizeReplacementFont(tmpFont, profileKey, originalFontName, $"file '{fontPath}'");
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning($"TMP font-file creation failed for '{fontPath}': {GetBaseMessage(ex)}");
            return null;
        }
    }

    private static TMP_FontAsset FinalizeReplacementFont(TMP_FontAsset tmpFont, string profileKey, string originalFontName, string sourceDescription)
    {
        if (tmpFont == null)
        {
            return null;
        }

        tmpFont.name = $"LC2_Crisp_{profileKey}";
        tmpFont.isMultiAtlasTexturesEnabled = true;
        TuneMaterial(tmpFont.material);
        CrispChineseFontPlugin.LogSource.LogInfo(
            $"Created TMP replacement '{tmpFont.name}' for original '{originalFontName}' from {sourceDescription}, " +
            $"sampling={CrispChineseFontPlugin.SamplingPointSize.Value}, " +
            $"padding={CrispChineseFontPlugin.AtlasPadding.Value}, " +
            $"atlas={CrispChineseFontPlugin.AtlasWidth.Value}x{CrispChineseFontPlugin.AtlasHeight.Value}.");
        return tmpFont;
    }

    private static Font TryCreateFontFromFile(string configuredPath)
    {
        var fontPath = ResolveGamePath(configuredPath);
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

    private void TryPreloadLocalizationGlyphs(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null || !CrispChineseFontPlugin.PreloadLocalizationGlyphs.Value)
        {
            return;
        }

        var fontId = fontAsset.GetInstanceID();
        if (_preloadedFontIds.Contains(fontId))
        {
            return;
        }

        try
        {
            PreloadLocalizationGlyphs(fontAsset);
            _preloadedFontIds.Add(fontId);
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning($"Localization glyph preload failed: {ex.Message}");
        }
    }

    private static void PreloadLocalizationGlyphs(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null || fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic)
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
                if (IsEastAsian(ch))
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
        if (fontAsset.TryAddCharacters(preloadText, out var missing) && string.IsNullOrEmpty(missing))
        {
            CrispChineseFontPlugin.LogSource.LogInfo($"Preloaded {chars.Count} East Asian glyphs into '{SafeObjectName(fontAsset)}'.");
        }
        else
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"Preloaded {chars.Count - missing.Length}/{chars.Count} East Asian glyphs into '{SafeObjectName(fontAsset)}'; missing={missing.Length}.");
        }
    }

    private void ApplyToLoadedText()
    {
        TryCreateLegacyFont();

        var tmpCount = 0;
        var legacyCount = 0;

        foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (text == null || !TryPatchTmpText(text))
            {
                continue;
            }

            tmpCount++;
        }

        if (CrispChineseFontPlugin.ReplaceLegacyUiText.Value && _legacyFont != null)
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text == null || !ContainsEastAsian(text.text))
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

    private bool TryPatchTmpText(TMP_Text text)
    {
        if (_isPatching)
        {
            return false;
        }

        try
        {
            _isPatching = true;
            return PatchTmpText(text);
        }
        finally
        {
            _isPatching = false;
        }
    }

    private bool PatchTmpText(TMP_Text text)
    {
        var currentFont = text.font;
        if (currentFont == null)
        {
            return false;
        }

        if (IsOurReplacementFont(currentFont))
        {
            TryAddTextCharacters(currentFont, text.text);
            TuneMaterial(text.fontSharedMaterial);
            return false;
        }

        var profileKey = GetFontProfileKey(currentFont);
        if (string.IsNullOrEmpty(profileKey))
        {
            return false;
        }

        var replacementFont = GetOrCreateReplacementFont(currentFont, profileKey);
        if (replacementFont == null)
        {
            return false;
        }

        var originalMaterial = text.fontSharedMaterial;
        TryAddTextCharacters(replacementFont, text.text);
        text.font = replacementFont;
        text.fontSharedMaterial = GetOrCreateReplacementMaterial(originalMaterial, replacementFont, profileKey);
        TuneMaterial(text.fontSharedMaterial);
        text.ForceMeshUpdate(true);
        return true;
    }

    private static void TryAddTextCharacters(TMP_FontAsset fontAsset, string text)
    {
        if (fontAsset == null || string.IsNullOrEmpty(text) || fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic)
        {
            return;
        }

        try
        {
            fontAsset.TryAddCharacters(text, out _);
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"Failed to add glyphs to '{SafeObjectName(fontAsset)}': {GetBaseMessage(ex)}");
        }
    }

    private Material GetOrCreateReplacementMaterial(Material originalMaterial, TMP_FontAsset replacementFont, string profileKey)
    {
        var originalId = originalMaterial == null ? 0 : originalMaterial.GetInstanceID();
        var key = $"{profileKey}:{replacementFont.GetInstanceID()}:{originalId}";
        if (_replacementMaterials.TryGetValue(key, out var material) && material != null)
        {
            return material;
        }

        var sourceMaterial = originalMaterial != null ? originalMaterial : replacementFont.material;
        material = sourceMaterial != null ? new Material(sourceMaterial) : replacementFont.material;
        if (material != null)
        {
            material.name = $"{SafeObjectName(sourceMaterial)} -> {SafeObjectName(replacementFont)}";
            var atlasTexture = GetMainTexture(replacementFont.material);
            if (atlasTexture != null && material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", atlasTexture);
            }

            TuneMaterial(material);
        }

        _replacementMaterials[key] = material;
        return material;
    }

    private static Texture GetMainTexture(Material material)
    {
        if (material == null || !material.HasProperty("_MainTex"))
        {
            return null;
        }

        try
        {
            return material.GetTexture("_MainTex");
        }
        catch
        {
            return null;
        }
    }

    private bool IsOurReplacementFont(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null)
        {
            return false;
        }

        if (_replacementFontIds.Contains(fontAsset.GetInstanceID()))
        {
            return true;
        }

        return SafeObjectName(fontAsset).StartsWith("LC2_Crisp_", StringComparison.Ordinal);
    }

    private static string GetFontPathForProfile(string profileKey)
    {
        return profileKey switch
        {
            "AlibabaPuHuiTi" => CrispChineseFontPlugin.FontFilePath.Value,
            "SourceHanSansCN" => CrispChineseFontPlugin.SourceHanAsciiFontFilePath.Value,
            "BossTitle" => CrispChineseFontPlugin.BossTitleFontFilePath.Value,
            "TraditionalChinese" => CrispChineseFontPlugin.TraditionalChineseFontFilePath.Value,
            "Japanese" => CrispChineseFontPlugin.JapaneseFontFilePath.Value,
            "Korean" => CrispChineseFontPlugin.KoreanFontFilePath.Value,
            _ => string.Empty,
        };
    }

    private static string[] GetSourceFontNameTokens(string profileKey)
    {
        return profileKey switch
        {
            "AlibabaPuHuiTi" => new[] { "AlibabaPuHuiTi", "Alibaba PuHuiTi", "Alibaba-PuHuiTi" },
            "SourceHanSansCN" => new[] { "SourceHanSansCN", "Source Han Sans CN", "Alibaba-PuHuiTi-Bold", "Alibaba PuHuiTi" },
            "BossTitle" => new[] { "AlibabaPuHuiTi", "Alibaba PuHuiTi", "Alibaba-PuHuiTi" },
            "TraditionalChinese" => new[] { "AlibabaSansTC", "Alibaba Sans TC", "Alibaba Sans TCN", "TCN" },
            "Japanese" => new[] { "AlibabaSansJP", "Alibaba Sans JP", "Japanese", "JP" },
            "Korean" => new[] { "AlibabaSansKR", "Alibaba Sans KR", "Korean", "KR" },
            _ => Array.Empty<string>(),
        };
    }

    private static string GetFontProfileKey(TMP_FontAsset fontAsset)
    {
        var name = SafeObjectName(fontAsset);
        if (string.IsNullOrEmpty(name) ||
            name.StartsWith("LC2_Crisp_", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (ContainsAny(name, "SourceHanSansCN-Bold-Hunter SDF"))
        {
            return "SourceHanSansCN";
        }

        if (!ContainsAny(name, "Alibaba-PuHuiTi-Bold SDF"))
        {
            return string.Empty;
        }

        if (ContainsAny(name, "BossTitle"))
        {
            return "BossTitle";
        }

        if (ContainsAny(name, " - TCN"))
        {
            return "TraditionalChinese";
        }

        if (ContainsAny(name, " - JP"))
        {
            return "Japanese";
        }

        if (ContainsAny(name, " - KR"))
        {
            return "Korean";
        }

        return "AlibabaPuHuiTi";
    }

    private static bool ContainsEastAsian(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (IsEastAsian(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEastAsian(char ch)
    {
        return IsCjk(ch)
            || ch is >= '\u3040' and <= '\u309F'
            || ch is >= '\u30A0' and <= '\u30FF'
            || ch is >= '\u31F0' and <= '\u31FF'
            || ch is >= '\u1100' and <= '\u11FF'
            || ch is >= '\u3130' and <= '\u318F'
            || ch is >= '\uAC00' and <= '\uD7AF';
    }

    private void LogMissingProfileOnce(string profileKey, string message)
    {
        if (_loggedMissingProfiles.Add(profileKey))
        {
            CrispChineseFontPlugin.LogSource.LogWarning(message);
        }
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
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

internal static class TextMeshProHooks
{
    private static readonly string[] HookMethodNames =
    {
        "OnEnable",
        "set_text",
        "SetVerticesDirty",
    };

    internal static int Install(Harmony harmony)
    {
        var postfix = AccessTools.Method(typeof(TextMeshProHooks), nameof(Postfix));
        var seen = new HashSet<MethodBase>();
        var patchedCount = 0;

        foreach (var type in new[] { typeof(TMP_Text), typeof(TextMeshProUGUI), typeof(TextMeshPro) })
        {
            foreach (var methodName in HookMethodNames)
            {
                foreach (var original in GetDeclaredMethods(type, methodName))
                {
                    if (!seen.Add(original))
                    {
                        continue;
                    }

                    harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                    patchedCount++;
                }
            }
        }

        return patchedCount;
    }

    private static IEnumerable<MethodInfo> GetDeclaredMethods(Type type, string methodName)
    {
        return type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == methodName);
    }

    private static void Postfix(TMP_Text __instance)
    {
        CrispChineseFontRuntime.QueueFromHook(__instance);
    }
}
