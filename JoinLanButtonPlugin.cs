using System;
using System.Collections;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SceneLoading;
using NuclearOption.SavedMission;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace JoinLanButtonMod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class JoinLanButtonPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.ngamingpc.nuclearoption.joinlanbutton";
    public const string PluginName = "Join LAN Button";
    public const string PluginVersion = "1.1.1";

    internal static ManualLogSource LogInstance = null!;
    internal static JoinLanButtonPlugin Instance = null!;

    private Harmony? _harmony;

    private void Awake()
    {
        Instance = this;
        LogInstance = Logger;

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();

        SceneManager.sceneLoaded += OnSceneLoaded;
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony?.UnpatchSelf();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.path == MapLoader.MainMenu)
        {
            StartCoroutine(InstallButtonDeferred());
        }
    }

    private IEnumerator InstallButtonDeferred()
    {
        // Let the menu finish its setup first.
        yield return null;
        yield return null;
        yield return new WaitForSecondsRealtime(0.2f);
        JoinLanUiController.TryInstall();
    }
}

internal static class ReflectionCache
{
    internal static readonly FieldInfo? OverlayMenuLayer = AccessTools.Field(typeof(MainMenu), "overlayMenuLayer");
    internal static readonly FieldInfo? InputPlayerName = AccessTools.Field(typeof(MainMenu), "inputPlayerName");
    internal static readonly FieldInfo? UnableToConnect = AccessTools.Field(typeof(MainMenu), "unableToConnect");
}

[HarmonyPatch(typeof(MainMenu), "Start")]
internal static class MainMenuStartPatch
{
    private static void Postfix()
    {
        if (JoinLanButtonPlugin.Instance != null)
        {
            JoinLanButtonPlugin.Instance.StartCoroutine(InstallSoon());
        }
    }

    private static IEnumerator InstallSoon()
    {
        yield return null;
        JoinLanUiController.TryInstall();
    }
}

internal static class JoinLanUiController
{
    private const string RootName = "JoinLanButtonRoot";
    private const string ModalName = "JoinLanModal";

    private static GameObject? _root;
    private static GameObject? _modal;
    private static TMP_InputField? _ipField;
    private static TextMeshProUGUI? _statusLabel;
    private static bool _pendingLanHostMissionSelection;

    internal static void TryInstall()
    {
        try
        {
            MainMenu? mainMenu = UnityEngine.Object.FindObjectOfType<MainMenu>(true);
            if (mainMenu == null)
            {
                return;
            }

            if (_root != null)
            {
                return;
            }

            Button? exitButton = FindExitButton(mainMenu);
            if (exitButton == null)
            {
                JoinLanButtonPlugin.LogInstance.LogWarning("Could not find EXIT GAME button in main menu.");
                return;
            }

            GameObject startLanRoot = UnityEngine.Object.Instantiate(exitButton.gameObject, exitButton.transform.parent);
            startLanRoot.name = "StartLanButtonRoot";
            startLanRoot.SetActive(true);
            ResetButton(startLanRoot, exitButton.transform as RectTransform, "START LAN GAME", new Vector2(0f, 176f), StartLanGame);

            _root = UnityEngine.Object.Instantiate(exitButton.gameObject, exitButton.transform.parent);
            _root.name = RootName;
            _root.SetActive(true);
            ResetButton(_root, exitButton.transform as RectTransform, "JOIN LAN", new Vector2(0f, 88f), OpenModal);
            CreateModal(mainMenu, exitButton);

            JoinLanButtonPlugin.LogInstance.LogInfo("Join LAN button installed.");
        }
        catch (Exception ex)
        {
            JoinLanButtonPlugin.LogInstance.LogError($"Failed to install Join LAN UI: {ex}");
        }
    }

    private static Button? FindExitButton(MainMenu mainMenu)
    {
        foreach (Button button in mainMenu.GetComponentsInChildren<Button>(true))
        {
            string text = GetButtonText(button);
            if (string.Equals(text.Trim(), "EXIT GAME", StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        // Fallback: try name-based match.
        return mainMenu.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(b => b.name.Contains("exit", StringComparison.OrdinalIgnoreCase) ||
                                 b.name.Contains("quit", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetButtonText(Button button)
    {
        TMP_Text? tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            return tmp.text ?? string.Empty;
        }

        Text? legacy = button.GetComponentInChildren<Text>(true);
        return legacy != null ? legacy.text ?? string.Empty : string.Empty;
    }

    private static void SetButtonText(GameObject buttonObj, string text)
    {
        foreach (TMP_Text tmp in buttonObj.GetComponentsInChildren<TMP_Text>(true))
        {
            tmp.text = text;
        }

        foreach (Text legacy in buttonObj.GetComponentsInChildren<Text>(true))
        {
            legacy.text = text;
        }
    }

    private static void ResetButton(GameObject buttonObj, RectTransform? sourceRect, string label, Vector2 offset, UnityEngine.Events.UnityAction onClick)
    {
        SetButtonText(buttonObj, label);

        Button? button = buttonObj.GetComponent<Button>();
        if (button == null)
        {
            button = buttonObj.GetComponentInChildren<Button>(true);
        }

        if (button == null)
        {
            throw new InvalidOperationException("Cloned Join LAN object did not contain a Button component.");
        }

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(onClick);

        if (buttonObj.transform is RectTransform rect && sourceRect != null)
        {
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.pivot = sourceRect.pivot;
            rect.sizeDelta = sourceRect.sizeDelta;
            rect.anchoredPosition = sourceRect.anchoredPosition + offset;
            rect.localScale = sourceRect.localScale;
            rect.localRotation = sourceRect.localRotation;
        }
    }

    private static void CreateModal(MainMenu mainMenu, Button styleButton)
    {
        Transform? overlayLayer = ReflectionCache.OverlayMenuLayer?.GetValue(mainMenu) as Transform;
        TMP_InputField? inputTemplate = ReflectionCache.InputPlayerName?.GetValue(mainMenu) as TMP_InputField;

        Transform parent = overlayLayer != null ? overlayLayer : mainMenu.transform;

        _modal = new GameObject(ModalName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _modal.transform.SetParent(parent, false);
        _modal.SetActive(false);

        RectTransform modalRect = _modal.GetComponent<RectTransform>();
        modalRect.anchorMin = new Vector2(0.5f, 0.5f);
        modalRect.anchorMax = new Vector2(0.5f, 0.5f);
        modalRect.pivot = new Vector2(0.5f, 0.5f);
        modalRect.sizeDelta = new Vector2(620f, 260f);

        Image modalBg = _modal.GetComponent<Image>();
        modalBg.color = new Color(0.08f, 0.1f, 0.14f, 0.96f);
        modalBg.raycastTarget = true;

        VerticalLayoutGroup layout = _modal.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 12;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        ContentSizeFitter fitter = _modal.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        TMP_FontAsset? font = inputTemplate != null ? inputTemplate.textComponent.font : null;

        CreateLabel(_modal.transform, "Join a LAN host", 28, FontStyles.Bold, font, TextAlignmentOptions.Center);
        CreateLabel(_modal.transform,
            "Enter the host LAN IP. You can also use IP:PORT. Default port is 7777.",
            18,
            FontStyles.Normal,
            font,
            TextAlignmentOptions.Center);

        _ipField = CreateInputField(_modal.transform, inputTemplate, font);
        _ipField.text = "127.0.0.1";
        _ipField.placeholder.GetComponent<TMP_Text>().text = "Example: 192.168.1.50 or 192.168.1.50:7777";

        _statusLabel = CreateLabel(_modal.transform, string.Empty, 16, FontStyles.Normal, font, TextAlignmentOptions.Center);
        _statusLabel.color = new Color(1f, 0.82f, 0.3f, 1f);

        GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        buttonRow.transform.SetParent(_modal.transform, false);
        HorizontalLayoutGroup row = buttonRow.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 14;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = false;
        row.childControlWidth = true;
        row.childControlHeight = true;

        Button joinButton = CloneStyledButton(styleButton, buttonRow.transform, "JOIN");
        joinButton.onClick = new Button.ButtonClickedEvent();
        joinButton.onClick.AddListener(JoinLan);

        Button cancelButton = CloneStyledButton(styleButton, buttonRow.transform, "CANCEL");
        cancelButton.onClick = new Button.ButtonClickedEvent();
        cancelButton.onClick.AddListener(CloseModal);
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string text, float size, FontStyles styles, TMP_FontAsset? font, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = size + 12f;

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = styles;
        label.alignment = alignment;
        label.enableWordWrapping = true;
        label.color = Color.white;
        if (font != null)
        {
            label.font = font;
        }
        return label;
    }

    private static TMP_InputField CreateInputField(Transform parent, TMP_InputField? template, TMP_FontAsset? font)
    {
        if (template != null)
        {
            TMP_InputField input = UnityEngine.Object.Instantiate(template, parent);
            input.name = "LanIpInput";
            input.text = string.Empty;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterValidation = TMP_InputField.CharacterValidation.None;
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            LayoutElement le = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 46f;
            return input;
        }

        GameObject root = new GameObject("LanIpInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        root.transform.SetParent(parent, false);
        root.GetComponent<Image>().color = new Color(0.17f, 0.19f, 0.24f, 0.95f);
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.minHeight = 46f;

        GameObject textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(root.transform, false);
        TextMeshProUGUI text = textGO.AddComponent<TextMeshProUGUI>();
        text.fontSize = 22f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.margin = new Vector4(12f, 6f, 12f, 6f);
        if (font != null)
        {
            text.font = font;
        }

        GameObject placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGO.transform.SetParent(root.transform, false);
        TextMeshProUGUI placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
        placeholder.fontSize = 22f;
        placeholder.color = new Color(1f, 1f, 1f, 0.38f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.margin = new Vector4(12f, 6f, 12f, 6f);
        placeholder.text = "Enter LAN IP";
        if (font != null)
        {
            placeholder.font = font;
        }

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        RectTransform placeholderRect = placeholder.rectTransform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        TMP_InputField inputField = root.GetComponent<TMP_InputField>();
        inputField.textViewport = root.GetComponent<RectTransform>();
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.contentType = TMP_InputField.ContentType.Standard;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.characterValidation = TMP_InputField.CharacterValidation.None;
        return inputField;
    }

    private static Button CloneStyledButton(Button template, Transform parent, string text)
    {
        Button button = UnityEngine.Object.Instantiate(template, parent);
        button.name = text + "Button";
        button.onClick = new Button.ButtonClickedEvent();
        SetButtonText(button.gameObject, text);

        RectTransform rect = button.transform as RectTransform;
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(rect.sizeDelta.y, 52f));
        }

        return button;
    }

    private static void StartLanGame()
    {
        try
        {
            if (!IsSteamRunning())
            {
                SetStatus("Steam must be running before starting a LAN host.");
                return;
            }

            _pendingLanHostMissionSelection = true;
            MainMenu? mainMenu = UnityEngine.Object.FindObjectOfType<MainMenu>(true);
            if (mainMenu == null)
            {
                _pendingLanHostMissionSelection = false;
                return;
            }

            JoinLanButtonPlugin.LogInstance.LogInfo("Opening mission picker for LAN host selection.");
            mainMenu.SelectMissions();
        }
        catch (Exception ex)
        {
            _pendingLanHostMissionSelection = false;
            JoinLanButtonPlugin.LogInstance.LogError($"Failed to open LAN host mission picker: {ex}");
        }
    }

    internal static bool TryConsumePendingLanHostMission(Mission mission)
    {
        if (!_pendingLanHostMissionSelection)
        {
            return false;
        }

        _pendingLanHostMissionSelection = false;
        return LaunchLanHost(mission);
    }

    private static bool LaunchLanHost(Mission mission)
    {
        try
        {
            if (mission == null)
            {
                return false;
            }

            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                JoinLanButtonPlugin.LogInstance.LogError("Could not determine Nuclear Option executable path.");
                return false;
            }

            string missionArg = mission.Name?.Replace("\"", "\\\"") ?? string.Empty;
            string args = $"-socket udp -mission \"{missionArg}\" -autoHost";
            JoinLanButtonPlugin.LogInstance.LogInfo($"Launching LAN host with args: {args}");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
                UseShellExecute = false
            };

            Process.Start(psi);
            Application.Quit();
            return true;
        }
        catch (Exception ex)
        {
            JoinLanButtonPlugin.LogInstance.LogError($"Failed to launch LAN host: {ex}");
            return false;
        }
    }

    private static void OpenModal()
    {
        if (_modal == null)
        {
            return;
        }

        SetStatus(string.Empty);
        _modal.SetActive(true);
        if (_ipField != null)
        {
            _ipField.interactable = true;
            _ipField.readOnly = false;
            _ipField.Select();
            _ipField.ActivateInputField();
            _ipField.MoveTextEnd(false);
        }

        if (JoinLanButtonPlugin.Instance != null)
        {
            JoinLanButtonPlugin.Instance.StartCoroutine(FocusInputField());
        }
    }

    private static IEnumerator FocusInputField()
    {
        for (int i = 0; i < 8; i++)
        {
            yield return null;
            if (_ipField == null)
            {
                yield break;
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_ipField.gameObject);
            }

            _ipField.interactable = true;
            _ipField.readOnly = false;
            _ipField.Select();
            _ipField.ActivateInputField();
            _ipField.MoveTextEnd(false);
        }
    }

    private static void CloseModal()
    {
        if (_modal != null)
        {
            _modal.SetActive(false);
        }
    }

    private static void SetStatus(string text)
    {
        if (_statusLabel != null)
        {
            _statusLabel.text = text;
        }
    }

    private static void JoinLan()
    {
        try
        {
            string raw = _ipField != null ? (_ipField.text ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                SetStatus("Enter a LAN IP first.");
                return;
            }

            if (!IsSteamRunning())
            {
                SetStatus("Steam must be running before joining a LAN host.");
                return;
            }

            if (!TryParseEndpoint(raw, out string host, out int? port, out string error))
            {
                SetStatus(error);
                return;
            }

            if (NetworkManagerNuclearOption.i == null)
            {
                SetStatus("Network manager was not ready yet.");
                return;
            }

            ConnectOptions options = new ConnectOptions(SocketType.UDP, host, port);
            JoinLanButtonPlugin.LogInstance.LogInfo($"Joining LAN host {host}:{(port ?? 7777)}");
            NetworkManagerNuclearOption.i.StartClient(options);
            CloseModal();
        }
        catch (Exception ex)
        {
            JoinLanButtonPlugin.LogInstance.LogError($"Join LAN failed: {ex}");
            SetStatus("Join failed. Check the BepInEx log for details.");
        }
    }


    private static bool IsSteamRunning()
    {
        try
        {
            return Process.GetProcessesByName("steam").Length > 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool TryParseEndpoint(string raw, out string host, out int? port, out string error)
    {
        host = string.Empty;
        port = null;
        error = string.Empty;

        string value = raw.Trim();
        if (value.Length == 0)
        {
            error = "Enter a LAN IP first.";
            return false;
        }

        // IPv6 in [addr]:port form.
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            int end = value.IndexOf(']');
            if (end <= 0)
            {
                error = "Invalid IPv6 format.";
                return false;
            }

            host = value.Substring(1, end - 1);
            string remainder = value.Substring(end + 1).Trim();
            if (remainder.StartsWith(":", StringComparison.Ordinal))
            {
                string portText = remainder.Substring(1);
                if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
                {
                    error = "Port must be between 1 and 65535.";
                    return false;
                }
                port = parsedPort;
            }
            return true;
        }

        int colonCount = value.Count(c => c == ':');
        if (colonCount == 1)
        {
            string[] parts = value.Split(':');
            host = parts[0].Trim();
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
            {
                error = "Port must be between 1 and 65535.";
                return false;
            }
            port = parsedPort;
            return !string.IsNullOrWhiteSpace(host);
        }

        host = value;
        return true;
    }
}


[HarmonyPatch(typeof(SinglePlayerMenu), "StartMission")]
internal static class SinglePlayerMenuStartMissionPatch
{
    private static bool Prefix(Mission mission)
    {
        if (!JoinLanUiController.TryConsumePendingLanHostMission(mission))
        {
            return true;
        }

        return false;
    }
}
