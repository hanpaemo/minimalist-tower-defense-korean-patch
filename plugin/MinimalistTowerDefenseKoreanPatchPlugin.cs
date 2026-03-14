using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using I2.Loc;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Hanpaemo.MinimalistTowerDefense;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class MinimalistTowerDefenseKoreanPatchPlugin : BaseUnityPlugin
{
    internal static bool LockLanguageToKorean;
    public const string PluginGuid = "hanpaemo.minimalisttowerdefense.koreanpatch";
    public const string PluginName = "Minimalist Tower Defense Korean Patch";
    public const string PluginVersion = "1.0.0";

    private static readonly string[] FontCandidates =
    {
        "Malgun Gothic",
        "Microsoft YaHei UI",
        "Arial Unicode MS",
        "Segoe UI Symbol"
    };

    private readonly HashSet<int> _patchedFontAssetIds = new();
    private readonly HashSet<int> _patchedTmpTextIds = new();
    private readonly HashSet<int> _patchedLegacyTextIds = new();

    private TMP_FontAsset? _runtimeTmpFont;
    private Font? _runtimeLegacyFont;
    private bool _isBootstrapping;
    private bool _hasDumped;
    private Harmony? _harmony;
    private string _translationCsvPath = string.Empty;
    private string _templateCsvPath = string.Empty;
    private string _dumpCsvPath = string.Empty;
    private string _languageSummaryPath = string.Empty;

    private void Awake()
    {
        _translationCsvPath = Path.Combine(Paths.BepInExRootPath, "Translation", "ko", "MinimalistTowerDefense", "localization-ko.csv");
        _templateCsvPath = Path.Combine(Paths.BepInExRootPath, "Translation", "dump", "MinimalistTowerDefense", "localization-ko-template.csv");
        _dumpCsvPath = Path.Combine(Paths.BepInExRootPath, "Translation", "dump", "MinimalistTowerDefense", "localization-all-locales.csv");
        _languageSummaryPath = Path.Combine(Paths.BepInExRootPath, "Translation", "dump", "MinimalistTowerDefense", "languages.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(_translationCsvPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_dumpCsvPath)!);

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");

        PlayerPrefs.SetString("I2 Language", "Korean");
        PlayerPrefs.Save();

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());
        Logger.LogInfo("Harmony patches applied.");

        CreateRuntimeFonts();
        SceneManager.sceneLoaded += OnSceneLoaded;
        StartCoroutine(BootstrapCoroutine());
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony?.UnpatchSelf();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _patchedFontAssetIds.Clear();
        _patchedTmpTextIds.Clear();
        _patchedLegacyTextIds.Clear();

        Logger.LogInfo($"Scene loaded: {scene.name}");
        StartCoroutine(RefreshFontsCoroutine());

        if (!_hasDumped || File.Exists(_translationCsvPath))
        {
            StartCoroutine(BootstrapCoroutine());
        }
    }

    private IEnumerator BootstrapCoroutine()
    {
        if (_isBootstrapping)
        {
            yield break;
        }

        _isBootstrapping = true;
        try
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                LocalizationManager.InitializeIfNeeded();
                LanguageSourceData? source = GetPrimarySource();
                if (source != null)
                {
                    if (!_hasDumped)
                    {
                        DumpSource(source);
                        _hasDumped = true;
                    }

                    ApplyKoreanTranslations(source);
                    RefreshFonts();
                    yield break;
                }

                yield return new WaitForSeconds(0.5f);
            }

            Logger.LogWarning("Localization source was not found within 30 seconds.");
        }
        finally
        {
            _isBootstrapping = false;
        }
    }

    private IEnumerator RefreshFontsCoroutine()
    {
        yield return null;
        RefreshFonts();

        yield return new WaitForSeconds(0.5f);
        RefreshFonts();

        yield return new WaitForSeconds(1.5f);
        RefreshFonts();
    }

    private LanguageSourceData? GetPrimarySource()
    {
        if (LocalizationManager.Sources == null || LocalizationManager.Sources.Count == 0)
        {
            return null;
        }

        return LocalizationManager.Sources
            .Where(source => source != null)
            .OrderByDescending(source => source.mTerms?.Count ?? 0)
            .FirstOrDefault();
    }

    private void DumpSource(LanguageSourceData source)
    {
        List<LanguageData> languages = source.mLanguages ?? new List<LanguageData>();
        List<TermData> terms = source.mTerms ?? new List<TermData>();

        File.WriteAllText(_dumpCsvPath, BuildAllLocalesCsv(languages, terms), Encoding.UTF8);
        File.WriteAllText(_templateCsvPath, BuildTemplateCsv(languages, terms), Encoding.UTF8);
        File.WriteAllText(_languageSummaryPath, BuildLanguageSummary(languages, terms.Count), Encoding.UTF8);

        Logger.LogInfo($"Wrote localization dump: {_dumpCsvPath}");
        Logger.LogInfo($"Wrote Korean template: {_templateCsvPath}");
    }

    private void ApplyKoreanTranslations(LanguageSourceData source)
    {
        if (!File.Exists(_translationCsvPath))
        {
            return;
        }

        Dictionary<string, string> translations = LoadTranslationMap(_translationCsvPath);
        if (translations.Count == 0)
        {
            Logger.LogWarning($"Translation CSV is empty: {_translationCsvPath}");
            return;
        }

        EnsureLanguageExists(source, "Korean", "ko");
        int koreanIndex = source.GetLanguageIndex("Korean");
        if (koreanIndex < 0)
        {
            Logger.LogWarning("Korean language slot could not be created.");
            return;
        }

        int applied = 0;
        foreach (TermData term in source.mTerms ?? Enumerable.Empty<TermData>())
        {
            if (term == null || string.IsNullOrWhiteSpace(term.Term))
            {
                continue;
            }

            if (!translations.TryGetValue(term.Term, out string translated))
            {
                continue;
            }

            translated = translated.Trim();
            if (translated.Length == 0)
            {
                continue;
            }

            EnsureTermCapacity(term, source.mLanguages.Count);
            term.Languages[koreanIndex] = NormalizeNewlines(translated);
            applied++;
        }

        source.UpdateDictionary();

        LockLanguageToKorean = true;
        LocalizationManager.CurrentLanguage = "Korean";
        LocalizationManager.LocalizeAll(true);

        Logger.LogInfo($"Applied Korean translations: {applied} rows.");
    }

    private void EnsureLanguageExists(LanguageSourceData source, string name, string code)
    {
        bool hasLanguage = source.mLanguages != null &&
            source.mLanguages.Any(language =>
                string.Equals(language.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));

        if (hasLanguage)
        {
            return;
        }

        source.AddLanguage(name, code);
        Logger.LogInfo($"Added language slot: {name} ({code})");
    }

    private static void EnsureTermCapacity(TermData term, int languageCount)
    {
        if (term.Languages == null)
        {
            term.Languages = new string[languageCount];
        }
        else if (term.Languages.Length < languageCount)
        {
            Array.Resize(ref term.Languages, languageCount);
        }

        if (term.Flags == null)
        {
            term.Flags = new byte[languageCount];
        }
        else if (term.Flags.Length < languageCount)
        {
            Array.Resize(ref term.Flags, languageCount);
        }
    }

    private Dictionary<string, string> LoadTranslationMap(string csvPath)
    {
        Dictionary<string, string> translations = new(StringComparer.OrdinalIgnoreCase);

        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (lines.Length == 0)
        {
            return translations;
        }

        string[] header = ParseCsvLine(lines[0]);
        int termIndex = Array.FindIndex(header, field => string.Equals(field.Trim(), "term", StringComparison.OrdinalIgnoreCase));
        int translationIndex = Array.FindIndex(header, field => string.Equals(field.Trim(), "translation", StringComparison.OrdinalIgnoreCase));
        if (termIndex < 0 || translationIndex < 0)
        {
            Logger.LogWarning("Translation CSV must contain 'term' and 'translation' columns.");
            return translations;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] row = ParseCsvLine(line);
            if (termIndex >= row.Length || translationIndex >= row.Length)
            {
                continue;
            }

            string term = row[termIndex]?.Trim() ?? string.Empty;
            string translation = row[translationIndex] ?? string.Empty;
            if (term.Length == 0)
            {
                continue;
            }

            translations[term] = translation;
        }

        return translations;
    }

    private static string[] ParseCsvLine(string line)
    {
        List<string> fields = new();
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                i++;
                StringBuilder sb = new();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++;
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }

                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',')
                {
                    i++;
                }
            }
            else
            {
                int start = i;
                while (i < line.Length && line[i] != ',')
                {
                    i++;
                }

                fields.Add(line.Substring(start, i - start));
                if (i < line.Length)
                {
                    i++;
                }
            }
        }

        return fields.ToArray();
    }

    private void CreateRuntimeFonts()
    {
        try
        {
            _runtimeLegacyFont = Font.CreateDynamicFontFromOSFont(FontCandidates, 32);
            Logger.LogInfo($"Created legacy runtime font: {_runtimeLegacyFont.name}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to create legacy runtime font: {ex.Message}");
            _runtimeLegacyFont = null;
        }
    }

    private void RefreshFonts()
    {
        EnsureTmpFont();
        InjectTmpFallbacks();
        ApplyFontsToTmpText();
        ApplyFontsToLegacyText();
    }

    private void EnsureTmpFont()
    {
        if (_runtimeTmpFont != null || _runtimeLegacyFont == null)
        {
            return;
        }

        _runtimeTmpFont = TMP_FontAsset.CreateFontAsset(
            _runtimeLegacyFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            2048,
            2048,
            AtlasPopulationMode.Dynamic,
            true);

        _runtimeTmpFont.name = "Hanpaemo Runtime Korean TMP Font";

        IList<TMP_FontAsset>? fallbackFonts = TMP_Settings.fallbackFontAssets;
        if (fallbackFonts != null && !fallbackFonts.Contains(_runtimeTmpFont))
        {
            fallbackFonts.Add(_runtimeTmpFont);
        }

        Logger.LogInfo("Created TMP Korean fallback font.");
    }

    private void InjectTmpFallbacks()
    {
        if (_runtimeTmpFont == null)
        {
            return;
        }

        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == null || font == _runtimeTmpFont)
            {
                continue;
            }

            int id = font.GetInstanceID();
            if (_patchedFontAssetIds.Contains(id))
            {
                continue;
            }

            if (font.fallbackFontAssetTable == null)
            {
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }

            if (!font.fallbackFontAssetTable.Contains(_runtimeTmpFont))
            {
                font.fallbackFontAssetTable.Add(_runtimeTmpFont);
            }

            _patchedFontAssetIds.Add(id);
        }
    }

    private void ApplyFontsToTmpText()
    {
        if (_runtimeTmpFont == null)
        {
            return;
        }

        foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (text == null || !ContainsHangul(text.text))
            {
                continue;
            }

            int id = text.GetInstanceID();
            if (_patchedTmpTextIds.Contains(id))
            {
                continue;
            }

            if (text.font == null)
            {
                text.font = _runtimeTmpFont;
            }
            else
            {
                if (text.font.fallbackFontAssetTable == null)
                {
                    text.font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                }

                if (!text.font.fallbackFontAssetTable.Contains(_runtimeTmpFont))
                {
                    text.font.fallbackFontAssetTable.Add(_runtimeTmpFont);
                }
            }

            text.havePropertiesChanged = true;
            text.SetAllDirty();
            _patchedTmpTextIds.Add(id);
        }
    }

    private void ApplyFontsToLegacyText()
    {
        if (_runtimeLegacyFont == null)
        {
            return;
        }

        foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (text == null || !ContainsHangul(text.text))
            {
                continue;
            }

            int id = text.GetInstanceID();
            if (_patchedLegacyTextIds.Contains(id))
            {
                continue;
            }

            text.font = _runtimeLegacyFont;
            text.SetAllDirty();
            _patchedLegacyTextIds.Add(id);
        }
    }

    private static bool ContainsHangul(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string content = text!;
        foreach (char c in content)
        {
            if ((c >= '\u1100' && c <= '\u11FF') ||
                (c >= '\u3130' && c <= '\u318F') ||
                (c >= '\uAC00' && c <= '\uD7AF'))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string BuildLanguageSummary(IReadOnlyList<LanguageData> languages, int termCount)
    {
        StringBuilder sb = new();
        sb.AppendLine($"Language count: {languages.Count}");
        sb.AppendLine($"Term count: {termCount}");
        sb.AppendLine();

        for (int index = 0; index < languages.Count; index++)
        {
            LanguageData language = languages[index];
            sb.AppendLine($"{index}: {language.Name} ({language.Code})");
        }

        return sb.ToString();
    }

    private static string BuildAllLocalesCsv(IReadOnlyList<LanguageData> languages, IReadOnlyList<TermData> terms)
    {
        StringBuilder sb = new();
        List<string> header = new() { "term", "description" };
        header.AddRange(languages.Select(language => language.Code ?? language.Name ?? "lang"));
        sb.AppendLine(string.Join(",", header.Select(EscapeCsv)));

        foreach (TermData term in terms)
        {
            string[] termLanguages = term.Languages ?? Array.Empty<string>();
            List<string> row = new()
            {
                term.Term ?? string.Empty,
                term.Description ?? string.Empty
            };

            for (int index = 0; index < languages.Count; index++)
            {
                row.Add(index < termLanguages.Length ? termLanguages[index] ?? string.Empty : string.Empty);
            }

            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return sb.ToString();
    }

    private static string BuildTemplateCsv(IReadOnlyList<LanguageData> languages, IReadOnlyList<TermData> terms)
    {
        int englishIndex = FindLanguageIndex(languages, "en", "English");
        if (englishIndex < 0)
        {
            englishIndex = 0;
        }

        StringBuilder sb = new();
        sb.AppendLine("term,description,english,translation");

        foreach (TermData term in terms)
        {
            string[] termLanguages = term.Languages ?? Array.Empty<string>();
            string english = englishIndex < termLanguages.Length
                ? termLanguages[englishIndex] ?? string.Empty
                : string.Empty;

            sb.AppendLine(string.Join(",",
                EscapeCsv(term.Term ?? string.Empty),
                EscapeCsv(term.Description ?? string.Empty),
                EscapeCsv(english),
                EscapeCsv(string.Empty)));
        }

        return sb.ToString();
    }

    private static int FindLanguageIndex(IReadOnlyList<LanguageData> languages, string expectedCode, string expectedName)
    {
        for (int index = 0; index < languages.Count; index++)
        {
            LanguageData language = languages[index];
            if (string.Equals(language.Code, expectedCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(language.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) == -1)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

[HarmonyPatch(typeof(LocalizationManager))]
internal static class ForceKoreanLanguagePatch
{
    [HarmonyPatch(nameof(LocalizationManager.CurrentLanguage), MethodType.Setter)]
    [HarmonyPrefix]
    static void Prefix(ref string value)
    {
        if (MinimalistTowerDefenseKoreanPatchPlugin.LockLanguageToKorean && value != "Korean")
        {
            value = "Korean";
        }
    }
}
