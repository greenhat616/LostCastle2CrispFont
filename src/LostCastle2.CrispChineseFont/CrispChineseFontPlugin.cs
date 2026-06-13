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
    public const string PluginVersion = "0.4.2";

    private const string FontDirPrefix = "BepInEx/plugins/LostCastle2.CrispChineseFont/fonts/";

    internal static ManualLogSource LogSource = null!;

    // Global toggles and shared SDF parameters.
    internal static ConfigEntry<bool> EnableFontReplacer = null!;
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
    internal static ConfigEntry<bool> LogTextHierarchy = null!;
    internal static ConfigEntry<bool> EnableGameFontFallback = null!;

    private static readonly Dictionary<string, FontProfile> ProfilesByKey = new(StringComparer.Ordinal);
    internal static IReadOnlyList<FontProfile> Profiles { get; private set; } = Array.Empty<FontProfile>();

    private Harmony _harmony;

    public override void Load()
    {
        LogSource = Log;
        BindConfig();
        ClassInjector.RegisterTypeInIl2Cpp<CrispChineseFontRuntime>();
        InstallHooks();
        AddComponent<CrispChineseFontRuntime>();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded. FontReplacer={(EnableFontReplacer.Value ? "on" : "off")}.");
    }

    internal static FontProfile GetProfile(string profileKey)
    {
        if (string.IsNullOrEmpty(profileKey))
        {
            return null;
        }

        return ProfilesByKey.TryGetValue(profileKey, out var profile) ? profile : null;
    }

    private void BindConfig()
    {
        EnableFontReplacer = Config.Bind(
            "General",
            "EnableFontReplacer",
            true,
            "Master switch. When on, each enabled profile below replaces the game font with its configured file. " +
            "When off, every profile keeps the original game font and only gets the crisp high-sampling re-render.");

        SamplingPointSize = Config.Bind("Font", "SamplingPointSize", 56, "Global TMP dynamic SDF sampling point size. Profiles can override this.");
        AtlasPadding = Config.Bind("Font", "AtlasPadding", 9, "TMP dynamic SDF atlas padding.");
        AtlasWidth = Config.Bind("Font", "AtlasWidth", 4096, "TMP dynamic SDF atlas width.");
        AtlasHeight = Config.Bind("Font", "AtlasHeight", 4096, "TMP dynamic SDF atlas height.");
        Sharpness = Config.Bind("Material", "Sharpness", 0.08f, "Global TMP material sharpness. Try 0.05 to 0.15. Profiles can override this.");

        ReplaceLegacyUiText = Config.Bind("Apply", "ReplaceLegacyUiText", true, "Also replace legacy UnityEngine.UI.Text when it contains East Asian characters.");
        PreloadLocalizationGlyphs = Config.Bind("Apply", "PreloadLocalizationGlyphs", true, "Preload East Asian glyphs found in StreamingAssets/Localization JSON files.");
        ScanIntervalSeconds = Config.Bind("Apply", "ScanIntervalSeconds", 1.5f, "How often loaded text objects are scanned as a fallback.");
        MaxPreloadChars = Config.Bind("Apply", "MaxPreloadChars", 5000, "Maximum unique East Asian characters to preload from localization files.");
        PreferEmbeddedSourceFont = Config.Bind("Apply", "PreferEmbeddedSourceFont", true, "When a profile is NOT replacing, prefer the original TMP font asset's embedded source Font before any fallback file.");
        EnableImmediateHooks = Config.Bind("Apply", "EnableImmediateHooks", true, "Patch TMP_Text immediately from Harmony hooks. Periodic scanning remains as a fallback.");
        LogTextHierarchy = Config.Bind("Apply", "LogTextHierarchy", false, "Diagnostic: log the GameObject hierarchy and font asset of each patched text once. Enable this, open the dialogue/menu you want to target, then read BepInEx/LogOutput.log to find the right NameMatchKeywords. Disable when done.");
        EnableGameFontFallback = Config.Bind("Apply", "EnableGameFontFallback", true, "Add the original game font asset as a glyph fallback on each replacement font. Characters missing from a decorative replacement font then fall back to the game's own (full-coverage) font instead of TMP's default tofu boxes.");

        RegisterProfiles();
    }

    private void RegisterProfiles()
    {
        // Definition: key, human description, source-font name tokens, default replacement font, default enabled.
        var definitions = new[]
        {
            new FontProfile(
                "AlibabaPuHuiTi",
                "main UI / body text",
                new[] { "AlibabaPuHuiTi", "Alibaba PuHuiTi", "Alibaba-PuHuiTi" }),
            new FontProfile(
                "Chat",
                "chat / dialogue text",
                new[] { "AlibabaPuHuiTi", "Alibaba PuHuiTi", "Alibaba-PuHuiTi" }),
            new FontProfile(
                "BossTitle",
                "boss title big text",
                new[] { "AlibabaPuHuiTi", "Alibaba PuHuiTi", "Alibaba-PuHuiTi" }),
            new FontProfile(
                "SourceHanSansCN",
                "ASCII / number profile",
                new[] { "SourceHanSansCN", "Source Han Sans CN", "Alibaba-PuHuiTi-Bold", "Alibaba PuHuiTi" }),
            new FontProfile(
                "TraditionalChinese",
                "Traditional Chinese profile",
                new[] { "AlibabaSansTC", "Alibaba Sans TC", "Alibaba Sans TCN", "TCN" }),
            new FontProfile(
                "Japanese",
                "Japanese profile",
                new[] { "AlibabaSansJP", "Alibaba Sans JP", "Japanese", "JP" }),
            new FontProfile(
                "Korean",
                "Korean profile",
                new[] { "AlibabaSansKR", "Alibaba Sans KR", "Korean", "KR" }),
        };

        // (key, defaultEnabled, defaultFontFile, defaultNameKeywords)
        var defaults = new Dictionary<string, (bool Enabled, string Font, string Keywords)>(StringComparer.Ordinal)
        {
            ["AlibabaPuHuiTi"] = (true, FontDirPrefix + "华康圆体W7.ttf", string.Empty),
            ["Chat"] = (true, FontDirPrefix + "站酷快乐体2016修订版.ttf", "Dialog,Dialogue,Talk,Story,Subtitle,Speech,Conversation,Bubble,Narrat"),
            ["BossTitle"] = (true, FontDirPrefix + "HYYakuHei-75W.ttf", string.Empty),
            ["SourceHanSansCN"] = (false, FontDirPrefix + "YouSheBiaoTiHei-2.ttf", string.Empty),
            ["TraditionalChinese"] = (false, string.Empty, string.Empty),
            ["Japanese"] = (false, string.Empty, string.Empty),
            ["Korean"] = (false, FontDirPrefix + "AlibabaSansKR-Bold.ttf", string.Empty),
        };

        foreach (var profile in definitions)
        {
            var def = defaults.TryGetValue(profile.Key, out var d) ? d : (false, string.Empty, string.Empty);
            profile.Bind(Config, def.Item1, def.Item2, def.Item3);
            ProfilesByKey[profile.Key] = profile;
        }

        Profiles = definitions;
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

/// <summary>
/// One replaceable font category. Holds its detection tokens and per-profile config entries.
/// </summary>
public sealed class FontProfile
{
    public string Key { get; }
    public string Description { get; }
    public string[] SourceTokens { get; }

    public ConfigEntry<bool> Enabled { get; private set; }
    public ConfigEntry<string> FontFilePath { get; private set; }
    public ConfigEntry<float> Sharpness { get; private set; }
    public ConfigEntry<int> SamplingPointSize { get; private set; }
    public ConfigEntry<float> LineHeightScale { get; private set; }
    public ConfigEntry<string> NameMatchKeywords { get; private set; }

    public FontProfile(string key, string description, string[] sourceTokens)
    {
        Key = key;
        Description = description;
        SourceTokens = sourceTokens ?? Array.Empty<string>();
    }

    public void Bind(ConfigFile config, bool defaultEnabled, string defaultFont, string defaultKeywords)
    {
        var section = $"Profile.{Key}";
        Enabled = config.Bind(
            section,
            "Enabled",
            defaultEnabled,
            $"Replace the {Description} font with FontFilePath. When false, the original game font is reused (crisp re-render only). " +
            "Requires the General/EnableFontReplacer master switch to be on.");
        FontFilePath = config.Bind(
            section,
            "FontFilePath",
            defaultFont ?? string.Empty,
            "Replacement font file (.ttf/.otf). Relative paths are resolved from the game directory.");
        Sharpness = config.Bind(
            section,
            "Sharpness",
            -1f,
            "TMP material sharpness for this profile. Negative means use the global Material/Sharpness value.");
        SamplingPointSize = config.Bind(
            section,
            "SamplingPointSize",
            0,
            "TMP SDF sampling point size for this profile. Zero or negative means use the global Font/SamplingPointSize value.");
        LineHeightScale = config.Bind(
            section,
            "LineHeightScale",
            1.0f,
            "Multiplier on line height after it is normalized to the original game font's metrics. " +
            "Use >1 to loosen cramped lines, <1 to tighten. 1.0 keeps the game's original line spacing.");
        NameMatchKeywords = config.Bind(
            section,
            "NameMatchKeywords",
            defaultKeywords ?? string.Empty,
            "Comma-separated GameObject name keywords. Any TMP text whose object (or a near ancestor) name contains one of these " +
            "is routed to THIS profile regardless of its font asset. Use it to target text that shares a font asset with other UI, " +
            "such as story dialogue. Only applies when this profile is actively replacing. Empty disables name routing.");
    }

    public bool IsReplacing()
    {
        return CrispChineseFontPlugin.EnableFontReplacer.Value
            && Enabled.Value
            && !string.IsNullOrWhiteSpace(FontFilePath.Value);
    }

    public float ResolveSharpness()
    {
        return Sharpness != null && Sharpness.Value >= 0f
            ? Sharpness.Value
            : CrispChineseFontPlugin.Sharpness.Value;
    }

    public int ResolveSamplingPointSize()
    {
        return SamplingPointSize != null && SamplingPointSize.Value > 0
            ? SamplingPointSize.Value
            : CrispChineseFontPlugin.SamplingPointSize.Value;
    }
}

public sealed class CrispChineseFontRuntime : MonoBehaviour
{
    private static CrispChineseFontRuntime _instance;

    private readonly Dictionary<string, TMP_FontAsset> _replacementFonts = new();
    private readonly Dictionary<string, Material> _replacementMaterials = new();
    private readonly Dictionary<int, TMP_Text> _pendingHookTexts = new();
    private readonly HashSet<int> _replacementFontIds = new();
    private readonly Dictionary<int, string> _fontProfileByFontId = new();
    private readonly HashSet<int> _preloadedFontIds = new();
    private readonly HashSet<string> _loggedMissingProfiles = new();
    private readonly HashSet<string> _loggedHierarchies = new();
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

        // Legacy UnityEngine.UI.Text uses a real Unity Font. Drive it from the main body profile so the
        // replacement font follows the AlibabaPuHuiTi (UI) configuration.
        var bodyProfile = CrispChineseFontPlugin.GetProfile("AlibabaPuHuiTi");
        var path = bodyProfile?.FontFilePath.Value;
        if (!string.IsNullOrWhiteSpace(path))
        {
            _legacyFont = TryCreateFontFromFile(path);
        }
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

        var profile = CrispChineseFontPlugin.GetProfile(profileKey);
        var originalFontName = SafeObjectName(originalFont);
        var replacing = profile != null && profile.IsReplacing();

        TMP_FontAsset replacementFont = null;

        // Active replacement: force the configured font file for this profile.
        if (replacing)
        {
            replacementFont = TryCreateTmpFontAssetFromFile(profile.FontFilePath.Value, profile, originalFontName);
            if (replacementFont == null)
            {
                LogMissingProfileOnce(
                    profileKey,
                    $"Profile '{profileKey}' replacement is enabled but font '{profile.FontFilePath.Value}' could not be loaded; using the original game font instead.");
            }
        }

        // Default behavior (or replacement file failed): keep the game font, just crisp re-render it.
        if (replacementFont == null)
        {
            replacementFont =
                TryCreateTmpFontAssetFromOriginalSource(originalFont, profile, originalFontName) ??
                TryCreateTmpFontAssetFromLoadedFont(profile, originalFontName);

            // Last-resort fallback to a configured file only when we are not already replacing.
            if (replacementFont == null && !replacing && profile != null)
            {
                replacementFont = TryCreateTmpFontAssetFromFile(profile.FontFilePath.Value, profile, originalFontName);
            }
        }

        if (replacementFont == null)
        {
            LogMissingProfileOnce(
                profileKey,
                $"No high-resolution replacement could be created for profile '{profileKey}' from embedded source, loaded fonts, or configured file.");
            return null;
        }

        NormalizeLineMetrics(replacementFont, originalFont, profile);
        TryAddGameFontFallback(replacementFont, originalFont);
        _replacementFonts[profileKey] = replacementFont;
        _replacementFontIds.Add(replacementFont.GetInstanceID());
        _fontProfileByFontId[replacementFont.GetInstanceID()] = profileKey;
        TryPreloadLocalizationGlyphs(replacementFont);
        return replacementFont;
    }

    /// <summary>
    /// Aligns the replacement font's vertical line metric to the original game font, so swapped fonts keep
    /// the layout's intended line spacing instead of using their own (often tighter) TTF metrics.
    /// </summary>
    private static void NormalizeLineMetrics(TMP_FontAsset replacementFont, TMP_FontAsset originalFont, FontProfile profile)
    {
        if (replacementFont == null || originalFont == null)
        {
            return;
        }

        try
        {
            var original = originalFont.faceInfo;
            var replacement = replacementFont.faceInfo;
            if (original.pointSize <= 0f || replacement.pointSize <= 0f || original.lineHeight <= 0f)
            {
                return;
            }

            var scale = profile != null ? profile.LineHeightScale.Value : 1.0f;
            if (scale <= 0f)
            {
                scale = 1.0f;
            }

            // Match line height per em to the original, then apply the user multiplier.
            var targetLineHeight = replacement.pointSize * (original.lineHeight / original.pointSize) * scale;
            if (targetLineHeight <= 0f || Math.Abs(targetLineHeight - replacement.lineHeight) < 0.01f)
            {
                return;
            }

            replacement.lineHeight = targetLineHeight;
            replacementFont.faceInfo = replacement;
            CrispChineseFontPlugin.LogSource.LogInfo(
                $"Normalized line height for '{SafeObjectName(replacementFont)}': {replacement.lineHeight:0.#} " +
                $"(original ratio {original.lineHeight / original.pointSize:0.###}, scale {scale:0.##}).");
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"Line-height normalization failed for '{SafeObjectName(replacementFont)}': {GetBaseMessage(ex)}");
        }
    }

    /// <summary>
    /// Adds the original game font asset (full glyph coverage) to the replacement's fallback table, so
    /// characters a decorative replacement font lacks fall back to the game font instead of tofu boxes.
    /// </summary>
    private static void TryAddGameFontFallback(TMP_FontAsset replacementFont, TMP_FontAsset originalFont)
    {
        if (!CrispChineseFontPlugin.EnableGameFontFallback.Value || replacementFont == null || originalFont == null)
        {
            return;
        }

        if (replacementFont.GetInstanceID() == originalFont.GetInstanceID())
        {
            return;
        }

        try
        {
            var table = replacementFont.fallbackFontAssetTable;
            if (table == null)
            {
                table = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                replacementFont.fallbackFontAssetTable = table;
            }

            for (var i = 0; i < table.Count; i++)
            {
                var existing = table[i];
                if (existing != null && existing.GetInstanceID() == originalFont.GetInstanceID())
                {
                    return;
                }
            }

            table.Add(originalFont);
            CrispChineseFontPlugin.LogSource.LogInfo(
                $"Added '{SafeObjectName(originalFont)}' as glyph fallback for '{SafeObjectName(replacementFont)}'.");
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"Failed to set fallback font for '{SafeObjectName(replacementFont)}': {GetBaseMessage(ex)}");
        }
    }

    private static TMP_FontAsset TryCreateTmpFontAssetFromOriginalSource(TMP_FontAsset originalFont, FontProfile profile, string originalFontName)
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

            return TryCreateTmpFontAssetFromFont(sourceFont, profile, originalFontName, $"embedded source Font '{SafeFontName(sourceFont)}'");
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"Original TMP font '{originalFontName}' has no usable embedded source Font: {GetBaseMessage(ex)}");
            return null;
        }
    }

    private static TMP_FontAsset TryCreateTmpFontAssetFromLoadedFont(FontProfile profile, string originalFontName)
    {
        var tokens = profile?.SourceTokens ?? Array.Empty<string>();
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
                    return TryCreateTmpFontAssetFromFont(font, profile, originalFontName, $"loaded Unity Font '{loadedName}'");
                }
            }
        }

        return null;
    }

    private static TMP_FontAsset TryCreateTmpFontAssetFromFont(Font sourceFont, FontProfile profile, string originalFontName, string sourceDescription)
    {
        if (sourceFont == null)
        {
            return null;
        }

        try
        {
            var tmpFont = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                ResolveSampling(profile),
                CrispChineseFontPlugin.AtlasPadding.Value,
                GlyphRenderMode.SDFAA,
                CrispChineseFontPlugin.AtlasWidth.Value,
                CrispChineseFontPlugin.AtlasHeight.Value,
                AtlasPopulationMode.Dynamic);

            return FinalizeReplacementFont(tmpFont, profile, originalFontName, sourceDescription);
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning(
                $"TMP font creation failed from {sourceDescription} for '{originalFontName}': {GetBaseMessage(ex)}");
            return null;
        }
    }

    private static TMP_FontAsset TryCreateTmpFontAssetFromFile(string configuredPath, FontProfile profile, string originalFontName)
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
                ResolveSampling(profile),
                CrispChineseFontPlugin.AtlasPadding.Value,
                GlyphRenderMode.SDFAA,
                CrispChineseFontPlugin.AtlasWidth.Value,
                CrispChineseFontPlugin.AtlasHeight.Value);

            if (tmpFont == null)
            {
                CrispChineseFontPlugin.LogSource.LogWarning($"TMP failed to create a font asset from file '{fontPath}'.");
                return null;
            }

            return FinalizeReplacementFont(tmpFont, profile, originalFontName, $"file '{fontPath}'");
        }
        catch (Exception ex)
        {
            CrispChineseFontPlugin.LogSource.LogWarning($"TMP font-file creation failed for '{fontPath}': {GetBaseMessage(ex)}");
            return null;
        }
    }

    private static TMP_FontAsset FinalizeReplacementFont(TMP_FontAsset tmpFont, FontProfile profile, string originalFontName, string sourceDescription)
    {
        if (tmpFont == null)
        {
            return null;
        }

        var profileKey = profile?.Key ?? "Unknown";
        tmpFont.name = $"LC2_Crisp_{profileKey}";
        tmpFont.isMultiAtlasTexturesEnabled = true;
        TuneMaterial(tmpFont.material, ResolveSharpness(profile), tmpFont);
        CrispChineseFontPlugin.LogSource.LogInfo(
            $"Created TMP replacement '{tmpFont.name}' for original '{originalFontName}' from {sourceDescription}, " +
            $"sampling={ResolveSampling(profile)}, " +
            $"padding={CrispChineseFontPlugin.AtlasPadding.Value}, " +
            $"atlas={CrispChineseFontPlugin.AtlasWidth.Value}x{CrispChineseFontPlugin.AtlasHeight.Value}, " +
            $"sharpness={ResolveSharpness(profile):0.###}.");
        return tmpFont;
    }

    private static int ResolveSampling(FontProfile profile)
    {
        return profile?.ResolveSamplingPointSize() ?? CrispChineseFontPlugin.SamplingPointSize.Value;
    }

    private static float ResolveSharpness(FontProfile profile)
    {
        return profile?.ResolveSharpness() ?? CrispChineseFontPlugin.Sharpness.Value;
    }

    private float ResolveSharpnessForFont(TMP_FontAsset fontAsset)
    {
        if (fontAsset != null && _fontProfileByFontId.TryGetValue(fontAsset.GetInstanceID(), out var profileKey))
        {
            var profile = CrispChineseFontPlugin.GetProfile(profileKey);
            if (profile != null)
            {
                return profile.ResolveSharpness();
            }
        }

        return CrispChineseFontPlugin.Sharpness.Value;
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
                $"Preloaded {chars.Count - missing.Length}/{chars.Count} East Asian glyphs into '{SafeObjectName(fontAsset)}'; missing={missing.Length}. " +
                "A decorative replacement font may be missing glyphs and will fall back to TMP's default font for those characters.");
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
            TuneMaterial(text.fontSharedMaterial, ResolveSharpnessForFont(currentFont), currentFont);
            return false;
        }

        var profileKey = GetFontProfileKey(currentFont);
        if (string.IsNullOrEmpty(profileKey))
        {
            return false;
        }

        // Name-based routing can move text to a different profile (e.g. story dialogue that shares the
        // main body font asset but should use the Chat font).
        profileKey = ResolveProfileWithOverrides(text, profileKey);
        LogTextHierarchyOnce(text, currentFont, profileKey);

        var replacementFont = GetOrCreateReplacementFont(currentFont, profileKey);
        if (replacementFont == null)
        {
            return false;
        }

        var profile = CrispChineseFontPlugin.GetProfile(profileKey);
        var sharpness = profile?.ResolveSharpness() ?? CrispChineseFontPlugin.Sharpness.Value;

        var originalMaterial = text.fontSharedMaterial;
        TryAddTextCharacters(replacementFont, text.text);
        text.font = replacementFont;
        text.fontSharedMaterial = GetOrCreateReplacementMaterial(originalMaterial, replacementFont, profileKey, sharpness);
        TuneMaterial(text.fontSharedMaterial, sharpness, replacementFont);
        text.ForceMeshUpdate(true);
        return true;
    }

    private static string ResolveProfileWithOverrides(TMP_Text text, string baseProfileKey)
    {
        var hierarchy = GetHierarchyName(text, 5);
        if (string.IsNullOrEmpty(hierarchy))
        {
            return baseProfileKey;
        }

        foreach (var profile in CrispChineseFontPlugin.Profiles)
        {
            if (profile == null || !profile.IsReplacing())
            {
                continue;
            }

            var keywords = profile.NameMatchKeywords.Value;
            if (string.IsNullOrWhiteSpace(keywords))
            {
                continue;
            }

            foreach (var keyword in keywords.Split(','))
            {
                var trimmed = keyword.Trim();
                if (trimmed.Length > 0 && hierarchy.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return profile.Key;
                }
            }
        }

        return baseProfileKey;
    }

    private static string GetHierarchyName(TMP_Text text, int depth)
    {
        try
        {
            var transform = text != null ? text.transform : null;
            if (transform == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < depth && transform != null; i++)
            {
                if (sb.Length > 0)
                {
                    sb.Append('/');
                }

                sb.Append(SafeObjectName(transform.gameObject));
                transform = transform.parent;
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void LogTextHierarchyOnce(TMP_Text text, TMP_FontAsset originalFont, string profileKey)
    {
        if (!CrispChineseFontPlugin.LogTextHierarchy.Value || _loggedHierarchies.Count >= 1000)
        {
            return;
        }

        var hierarchy = GetHierarchyName(text, 6);
        var line = $"[{SafeObjectName(originalFont)}] -> {profileKey} | {hierarchy}";
        if (_loggedHierarchies.Add(line))
        {
            CrispChineseFontPlugin.LogSource.LogInfo($"TextMap {line}");
        }
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

    private Material GetOrCreateReplacementMaterial(Material originalMaterial, TMP_FontAsset replacementFont, string profileKey, float sharpness)
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

            TuneMaterial(material, sharpness, replacementFont);
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

        if (ContainsAny(name, "Chat"))
        {
            return "Chat";
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
        int c = ch;
        return IsCjk(ch)
            || (c >= 0x3040 && c <= 0x309F)   // Hiragana
            || (c >= 0x30A0 && c <= 0x30FF)   // Katakana
            || (c >= 0x31F0 && c <= 0x31FF)   // Katakana Phonetic Extensions
            || (c >= 0x1100 && c <= 0x11FF)   // Hangul Jamo
            || (c >= 0x3130 && c <= 0x318F)   // Hangul Compatibility Jamo
            || (c >= 0xAC00 && c <= 0xD7AF);  // Hangul Syllables
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
        int c = ch;
        return (c >= 0x3400 && c <= 0x4DBF)   // CJK Extension A
            || (c >= 0x4E00 && c <= 0x9FFF)   // CJK Unified Ideographs
            || (c >= 0xF900 && c <= 0xFAFF);  // CJK Compatibility Ideographs
    }

    // SDF geometry parameters that must match the atlas the glyphs were rendered into.
    // When a material is cloned from the game's original font (built with a different padding,
    // sampling size, and atlas dimensions), these values stay wrong and the shader decodes the
    // distance field over the wrong spread, smearing the antialiased edges -> blurry text.
    private static readonly string[] SdfGeometryProperties =
    {
        "_GradientScale",
        "_TextureWidth",
        "_TextureHeight",
        "_ScaleRatioA",
        "_ScaleRatioB",
        "_ScaleRatioC",
    };

    // Copy the SDF geometry parameters from the replacement font's own (correctly generated)
    // material onto a material that was cloned from the original game font.
    private static void CopySdfGeometry(Material material, TMP_FontAsset replacementFont)
    {
        if (material == null || replacementFont == null)
        {
            return;
        }

        var reference = replacementFont.material;
        if (reference == null || ReferenceEquals(reference, material))
        {
            return;
        }

        foreach (var property in SdfGeometryProperties)
        {
            if (material.HasProperty(property) && reference.HasProperty(property))
            {
                material.SetFloat(property, reference.GetFloat(property));
            }
        }
    }

    private static void TuneMaterial(Material material, float sharpness, TMP_FontAsset replacementFont = null)
    {
        if (material == null)
        {
            return;
        }

        CopySdfGeometry(material, replacementFont);

        if (material.HasProperty("_Sharpness"))
        {
            material.SetFloat("_Sharpness", sharpness);
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
