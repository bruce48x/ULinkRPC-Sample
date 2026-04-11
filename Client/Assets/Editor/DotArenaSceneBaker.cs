using SampleClient.Gameplay;
using Shared.Gameplay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using System.IO;

internal static class DotArenaSceneBaker
{
    private static readonly Color BoardColor = new(0.08f, 0.1f, 0.14f, 1f);
    private static readonly Color GridColor = new(0.75f, 0.86f, 0.94f, 0.1f);
    private static readonly Color BorderColor = new(1f, 0.84f, 0.31f, 0.24f);
    private static readonly Color DangerColor = new(1f, 0.24f, 0.24f, 0.08f);
    private static readonly Color PanelColor = new(0.04f, 0.06f, 0.08f, 0.92f);
    private static readonly Color ButtonColor = new(0.12f, 0.18f, 0.24f, 0.98f);
    private static readonly Color FieldColor = new(0.12f, 0.15f, 0.2f, 0.98f);
    private static readonly Color TextColor = new(0.9f, 0.94f, 0.98f, 1f);
    private static readonly Color SecondaryTextColor = new(0.76f, 0.84f, 0.92f, 1f);
    private static readonly Color PlaceholderTextColor = new(0.5f, 0.56f, 0.64f, 1f);
    private const string ArenaRootName = "ArenaRoot";
    private const string SceneUiRootName = "SceneUI";
    private const string PixelSpritePath = "Assets/Textures/DotArenaPixel.png";
    private const string TmpChineseFontAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/DotArenaCJK SDF.asset";
    private const string TmpChineseSourceFontPath = "Assets/TextMesh Pro/Fonts/msyh.ttc";
    private const string WindowsChineseFontPath = "C:/Windows/Fonts/msyh.ttc";
    private const string UiCharacterSet =
        "ULinkRPC Dot Arena Tick Buff AI W/A/S/D Space " +
        "\u70b9\u9635\u7ade\u6280\u573a" +
        "\u72b6\u6001" +
        "\u73a9\u5bb6" +
        "\u79ef\u5206" +
        "\u670d\u52a1\u7aef" +
        "\u540c\u6b65\u4eba\u6570" +
        "\u6a21\u5f0f" +
        "\u672c\u5730\u5355\u673a" +
        "\u5730\u5740" +
        "\u63d0\u793a" +
        "\u4e8b\u4ef6" +
        "\u9009\u62e9" +
        "\u5355\u673a" +
        "\u8054\u673a" +
        "\u5339\u914d" +
        "\u8d26\u53f7" +
        "\u5bc6\u7801" +
        "\u8bf7\u8f93\u5165" +
        "\u8fd4\u56de" +
        "\u70b9\u51fb" +
        "\u5f00\u59cb" +
        "\u8865\u8db3" +
        "\u540d" +
        "\u8fde\u63a5\u4e2d" +
        "\u6b63\u5728" +
        "\u7b49\u5f85" +
        "\u52a0\u5165" +
        "\u79fb\u52a8" +
        "\u51b2\u523a" +
        "\u4f4d\u7f6e" +
        "\u4ee5" +
        "\u6743\u5a01" +
        "\u4e3a" +
        "\u51c6" +
        "\u5ba2\u6237\u7aef" +
        "\u53ea\u53d1" +
        "\u5e7f\u64ad";

    [MenuItem("Tools/ULinkRPC/Bake Gameplay Arena Into Scene")]
    private static void BakeGameplayArenaIntoScene()
    {
        var game = Object.FindObjectOfType<DotArenaGame>();
        if (game == null)
        {
            Debug.LogError("[DotArena] DotArenaGame not found in the active scene.");
            return;
        }

        EnsurePixelSpriteImportSettings();
        EnsureChineseTmpFontAsset();
        var pixelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PixelSpritePath);
        if (pixelSprite == null)
        {
            Debug.LogError($"[DotArena] Missing sprite asset at '{PixelSpritePath}'.");
            return;
        }

        var scene = game.gameObject.scene;
        if (!scene.IsValid())
        {
            Debug.LogError("[DotArena] Active scene is not valid.");
            return;
        }

        BakeArena(game, pixelSprite);
        BakeUi(game, pixelSprite);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[DotArena] Baked static arena and UI into the active scene.");
    }

    private static void BakeArena(DotArenaGame game, Sprite pixelSprite)
    {
        var existingRoot = game.transform.Find(ArenaRootName);
        if (existingRoot != null)
        {
            Undo.DestroyObjectImmediate(existingRoot.gameObject);
        }

        var config = ArenaConfig.CreateDefault();
        var arenaHalfWidth = config.ArenaHalfExtents.x;
        var arenaHalfHeight = config.ArenaHalfExtents.y;

        var arenaRoot = new GameObject(ArenaRootName);
        Undo.RegisterCreatedObjectUndo(arenaRoot, "Bake DotArena Scene");
        arenaRoot.transform.SetParent(game.transform, false);

        CreateRect(arenaRoot.transform, pixelSprite, "DangerZone", Vector2.zero,
            new Vector2((arenaHalfWidth + 1f) * 2f, (arenaHalfHeight + 1f) * 2f), DangerColor, -30);
        CreateRect(arenaRoot.transform, pixelSprite, "Board", Vector2.zero,
            new Vector2(arenaHalfWidth * 2f, arenaHalfHeight * 2f), BoardColor, -20);

        const float gridStep = 2f;
        for (var x = -arenaHalfWidth; x <= arenaHalfWidth + 0.01f; x += gridStep)
        {
            CreateRect(arenaRoot.transform, pixelSprite, $"Vertical-{Mathf.RoundToInt(x)}", new Vector2(x, 0f),
                new Vector2(0.05f, arenaHalfHeight * 2f), GridColor, -10);
        }

        for (var y = -arenaHalfHeight; y <= arenaHalfHeight + 0.01f; y += gridStep)
        {
            CreateRect(arenaRoot.transform, pixelSprite, $"Horizontal-{Mathf.RoundToInt(y)}", new Vector2(0f, y),
                new Vector2(arenaHalfWidth * 2f, 0.05f), GridColor, -10);
        }

        CreateRect(arenaRoot.transform, pixelSprite, "TopBorder", new Vector2(0f, arenaHalfHeight),
            new Vector2(arenaHalfWidth * 2f + 0.18f, 0.18f), BorderColor, -5);
        CreateRect(arenaRoot.transform, pixelSprite, "BottomBorder", new Vector2(0f, -arenaHalfHeight),
            new Vector2(arenaHalfWidth * 2f + 0.18f, 0.18f), BorderColor, -5);
        CreateRect(arenaRoot.transform, pixelSprite, "LeftBorder", new Vector2(-arenaHalfWidth, 0f),
            new Vector2(0.18f, arenaHalfHeight * 2f + 0.18f), BorderColor, -5);
        CreateRect(arenaRoot.transform, pixelSprite, "RightBorder", new Vector2(arenaHalfWidth, 0f),
            new Vector2(0.18f, arenaHalfHeight * 2f + 0.18f), BorderColor, -5);
    }

    private static void BakeUi(DotArenaGame game, Sprite pixelSprite)
    {
        var existingUi = game.transform.Find(SceneUiRootName);
        if (existingUi != null)
        {
            Undo.DestroyObjectImmediate(existingUi.gameObject);
        }

        EnsureEventSystem();

        var sceneUi = new GameObject(SceneUiRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(sceneUi, "Bake DotArena UI");
        sceneUi.transform.SetParent(game.transform, false);

        var canvas = sceneUi.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = sceneUi.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1200f, 600f);
        scaler.matchWidthOrHeight = 0.5f;

        var rootRect = (RectTransform)sceneUi.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        CreateStretchGroup(sceneUi.transform, "OverlayLayer");
        BuildHudPanel(game, sceneUi.transform, pixelSprite);
        BuildEntryPanel(game, sceneUi.transform, pixelSprite);
    }

    private static void BuildHudPanel(DotArenaGame game, Transform parent, Sprite pixelSprite)
    {
        var panel = CreatePanel(parent, pixelSprite, "HUDPanel", PanelColor,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(416f, 176f));
        AddLabel(panel.transform, "TitleText", "ULinkRPC \u70b9\u9635\u7ade\u6280\u573a", 16, FontStyles.Bold, TextAlignmentOptions.TopLeft, TextColor,
            new Vector2(12f, -10f), new Vector2(392f, 24f));
        AddLabel(panel.transform, "StatusText", "\u72b6\u6001", 12, FontStyles.Normal, TextAlignmentOptions.TopLeft, SecondaryTextColor,
            new Vector2(12f, -38f), new Vector2(392f, 18f));
        AddLabel(panel.transform, "PlayerText", "\u73a9\u5bb6", 12, FontStyles.Normal, TextAlignmentOptions.TopLeft, SecondaryTextColor,
            new Vector2(12f, -58f), new Vector2(392f, 18f));
        AddLabel(panel.transform, "TickText", "Tick", 12, FontStyles.Normal, TextAlignmentOptions.TopLeft, SecondaryTextColor,
            new Vector2(12f, -78f), new Vector2(392f, 18f));
        AddLabel(panel.transform, "ModeText", "\u6a21\u5f0f", 12, FontStyles.Normal, TextAlignmentOptions.TopLeft, SecondaryTextColor,
            new Vector2(12f, -98f), new Vector2(392f, 18f));
        AddLabel(panel.transform, "HintText", "\u63d0\u793a", 12, FontStyles.Normal, TextAlignmentOptions.TopLeft, SecondaryTextColor,
            new Vector2(12f, -118f), new Vector2(392f, 18f));
        AddLabel(panel.transform, "EventText", "\u4e8b\u4ef6", 12, FontStyles.Normal, TextAlignmentOptions.TopLeft, SecondaryTextColor,
            new Vector2(12f, -138f), new Vector2(392f, 18f));
    }

    private static void BuildEntryPanel(DotArenaGame game, Transform parent, Sprite pixelSprite)
    {
        var panel = CreatePanel(parent, pixelSprite, "EntryPanel", PanelColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(420f, 280f));
        AddLabel(panel.transform, "TitleText", "\u70b9\u9635\u7ade\u6280\u573a", 22, FontStyles.Bold, TextAlignmentOptions.Top, TextColor,
            new Vector2(0f, -16f), new Vector2(340f, 30f), centered: true);
        AddLabel(panel.transform, "StatusText", "\u72b6\u6001", 13, FontStyles.Normal, TextAlignmentOptions.Top, SecondaryTextColor,
            new Vector2(0f, -52f), new Vector2(340f, 36f), centered: true);

        var modeSelect = CreateStretchGroup(panel.transform, "ModeSelectPanel");
        AddLabel(modeSelect.transform, "DescriptionText", "\u9009\u62e9\u6a21\u5f0f", 13, FontStyles.Normal, TextAlignmentOptions.Top, SecondaryTextColor,
            new Vector2(0f, -96f), new Vector2(320f, 42f), centered: true);
        CreateButton(modeSelect.transform, pixelSprite, "SinglePlayerButton", new Vector2(0f, -154f), new Vector2(260f, 34f),
            "\u5355\u673a");
        CreateButton(modeSelect.transform, pixelSprite, "MultiplayerButton", new Vector2(0f, -198f), new Vector2(260f, 34f),
            "\u8054\u673a");

        var multiplayer = CreateStretchGroup(panel.transform, "MultiplayerPanel");
        AddLabel(multiplayer.transform, "SubtitleText", "\u8054\u673a\u5339\u914d", 13, FontStyles.Normal, TextAlignmentOptions.Top, SecondaryTextColor,
            new Vector2(0f, -96f), new Vector2(320f, 24f), centered: true);
        AddLabel(multiplayer.transform, "AccountLabel", "\u8d26\u53f7", 12, FontStyles.Normal, TextAlignmentOptions.Left, SecondaryTextColor,
            new Vector2(-136f, -132f), new Vector2(60f, 24f));
        AddLabel(multiplayer.transform, "PasswordLabel", "\u5bc6\u7801", 12, FontStyles.Normal, TextAlignmentOptions.Left, SecondaryTextColor,
            new Vector2(-136f, -168f), new Vector2(60f, 24f));

        CreateInputField(multiplayer.transform, pixelSprite, "AccountInput", new Vector2(30f, -132f), new Vector2(212f, 28f),
            "\u8bf7\u8f93\u5165\u8d26\u53f7", false);
        CreateInputField(multiplayer.transform, pixelSprite, "PasswordInput", new Vector2(30f, -168f), new Vector2(212f, 28f),
            "\u8bf7\u8f93\u5165\u5bc6\u7801", true);

        CreateButton(multiplayer.transform, pixelSprite, "MatchButton", new Vector2(-70f, -216f), new Vector2(120f, 30f),
            "\u5339\u914d");
        CreateButton(multiplayer.transform, pixelSprite, "BackButton", new Vector2(70f, -216f), new Vector2(120f, 30f),
            "\u8fd4\u56de");
    }

    private static GameObject CreateStretchGroup(Transform parent, string name)
    {
        var group = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(group, "Bake DotArena UI");
        group.transform.SetParent(parent, false);
        var rect = (RectTransform)group.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return group;
    }

    private static GameObject CreatePanel(Transform parent, Sprite sprite, string name, Color color, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(panel, "Bake DotArena UI");
        panel.transform.SetParent(parent, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0f, 1f);
        if (anchorMin == new Vector2(0.5f, 0.5f) && anchorMax == new Vector2(0.5f, 0.5f))
        {
            rect.pivot = new Vector2(0.5f, 0.5f);
        }
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = panel.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.color = color;
        return panel;
    }

    private static TextMeshProUGUI AddLabel(Transform parent, string name, string text, float fontSize, FontStyles fontStyle,
        TextAlignmentOptions alignment, Color color, Vector2 anchoredPosition, Vector2 size, bool centered = false)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        Undo.RegisterCreatedObjectUndo(label, "Bake DotArena UI");
        label.transform.SetParent(parent, false);
        var rect = (RectTransform)label.transform;
        rect.anchorMin = centered ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = centered ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var uiText = label.GetComponent<TextMeshProUGUI>();
        uiText.font = GetDefaultTmpFontAsset();
        uiText.fontSize = fontSize;
        uiText.fontStyle = fontStyle;
        uiText.alignment = alignment;
        uiText.enableWordWrapping = true;
        uiText.overflowMode = TextOverflowModes.Overflow;
        uiText.color = color;
        uiText.text = text;
        return uiText;
    }

    private static TMP_FontAsset GetDefaultTmpFontAsset()
    {
        return EnsureChineseTmpFontAsset();
    }

    private static TMP_FontAsset EnsureChineseTmpFontAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpChineseFontAssetPath);
        if (existing != null)
        {
            existing.TryAddCharacters(UiCharacterSet, out _);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        var resourcesDirectory = System.IO.Path.GetDirectoryName(TmpChineseFontAssetPath);
        if (!string.IsNullOrEmpty(resourcesDirectory) && !AssetDatabase.IsValidFolder(resourcesDirectory))
        {
            EnsureFolderHierarchy(resourcesDirectory);
        }

        var sourceFont = EnsureChineseSourceFontImported();
        if (sourceFont == null)
        {
            Debug.LogError("[DotArena] Failed to import Chinese source font for TMP.");
            return null;
        }

        var fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            true);

        fontAsset.name = "DotArenaCJK SDF";
        fontAsset.TryAddCharacters(UiCharacterSet, out _);
        AssetDatabase.CreateAsset(fontAsset, TmpChineseFontAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(TmpChineseFontAssetPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpChineseFontAssetPath);
    }

    private static Font EnsureChineseSourceFontImported()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Font>(TmpChineseSourceFontPath);
        if (existing != null)
        {
            return existing;
        }

        if (!File.Exists(WindowsChineseFontPath))
        {
            Debug.LogError($"[DotArena] Missing Windows font at '{WindowsChineseFontPath}'.");
            return null;
        }

        var fontDirectory = Path.GetDirectoryName(TmpChineseSourceFontPath);
        if (!string.IsNullOrEmpty(fontDirectory) && !AssetDatabase.IsValidFolder(fontDirectory))
        {
            EnsureFolderHierarchy(fontDirectory);
        }

        File.Copy(WindowsChineseFontPath, TmpChineseSourceFontPath, true);
        AssetDatabase.ImportAsset(TmpChineseSourceFontPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Font>(TmpChineseSourceFontPath);
    }

    private static void EnsureFolderHierarchy(string assetFolderPath)
    {
        var normalized = assetFolderPath.Replace('\\', '/');
        var segments = normalized.Split('/');
        var current = segments[0];
        for (var i = 1; i < segments.Length; i++)
        {
            var next = $"{current}/{segments[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[i]);
            }

            current = next;
        }
    }

    private static void CreateButton(Transform parent, Sprite sprite, string name, Vector2 anchoredPosition,
        Vector2 size, string label)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        Undo.RegisterCreatedObjectUndo(buttonObject, "Bake DotArena UI");
        buttonObject.transform.SetParent(parent, false);
        var rect = (RectTransform)buttonObject.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = buttonObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = ButtonColor;

        AddLabel(buttonObject.transform, "Label", label, 13, FontStyles.Bold, TextAlignmentOptions.Center, TextColor,
            new Vector2(0f, -6f), size, centered: true);
    }

    private static void CreateInputField(Transform parent, Sprite sprite, string name, Vector2 anchoredPosition,
        Vector2 size, string placeholder, bool password)
    {
        var fieldObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        Undo.RegisterCreatedObjectUndo(fieldObject, "Bake DotArena UI");
        fieldObject.transform.SetParent(parent, false);
        var rect = (RectTransform)fieldObject.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = fieldObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = FieldColor;

        var inputField = fieldObject.GetComponent<TMP_InputField>();
        inputField.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.targetGraphic = image;

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        Undo.RegisterCreatedObjectUndo(textArea, "Bake DotArena UI");
        textArea.transform.SetParent(fieldObject.transform, false);
        var textAreaRect = (RectTransform)textArea.transform;
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10f, 6f);
        textAreaRect.offsetMax = new Vector2(-10f, -6f);

        var text = AddLabel(textArea.transform, "Text", string.Empty, 13, FontStyles.Normal, TextAlignmentOptions.Left, TextColor,
            Vector2.zero, Vector2.zero);
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.enableWordWrapping = false;

        var placeholderText = AddLabel(textArea.transform, "Placeholder", placeholder, 13, FontStyles.Italic,
            TextAlignmentOptions.Left, PlaceholderTextColor, Vector2.zero, Vector2.zero);
        var placeholderRect = placeholderText.rectTransform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;
        placeholderText.enableWordWrapping = false;

        inputField.textViewport = textAreaRect;
        inputField.textComponent = text;
        inputField.placeholder = placeholderText;

    }

    private static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(eventSystem, "Bake DotArena UI");
    }

    private static void EnsurePixelSpriteImportSettings()
    {
        var importer = AssetImporter.GetAtPath(PixelSpritePath) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        var changed = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (!Mathf.Approximately(importer.spritePixelsPerUnit, 1f))
        {
            importer.spritePixelsPerUnit = 1f;
            changed = true;
        }

        if (importer.filterMode != FilterMode.Point)
        {
            importer.filterMode = FilterMode.Point;
            changed = true;
        }

        if (importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static void CreateRect(Transform parent, Sprite sprite, string objectName, Vector2 position, Vector2 size,
        Color color, int sortingOrder)
    {
        var rectangle = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(rectangle, "Bake DotArena Scene");
        rectangle.transform.SetParent(parent, false);
        rectangle.transform.localPosition = new Vector3(position.x, position.y, 0f);
        rectangle.transform.localScale = new Vector3(size.x, size.y, 1f);

        var renderer = rectangle.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
    }
}
