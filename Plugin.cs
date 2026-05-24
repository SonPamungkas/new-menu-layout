using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

namespace NewMenuLayoutMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.newmenulayoutmod";
        public const string PluginName = "New Menu Layout";
        public const string PluginVersion = "1.0.0";

        public static Plugin Instance { get; private set; }
        internal new ManualLogSource Logger;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Logger.LogInfo($"Plugin {PluginGuid} is loaded!");
        }

        public IEnumerator ApplyMenuChangesRoutine(MainMenu mainMenu)
        {
            // Wait to ensure all UI elements (and other mods like JoinLanButton) are fully loaded
            yield return new WaitForSeconds(1f);

            Transform uiRoot = mainMenu.transform.root;
            Transform mainCanvas = uiRoot.Find("MainCanvas");

            if (mainCanvas == null)
            {
                mainCanvas = mainMenu.transform;
            }

            Transform leftPanel = mainCanvas.Find("Prejoin menu/LeftPanel");
            if (leftPanel != null)
            {
                var img = leftPanel.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color;
                    img.color = new Color(c.r, c.g, c.b, 0f);
                    Logger.LogInfo("LeftPanel opacity set to 0.");
                }

                Transform container = leftPanel.Find("Container");
                Transform playerNameInput = leftPanel.Find("playerNameInput");
                if (container != null && playerNameInput != null)
                {
                    var layout = leftPanel.GetComponent<UnityEngine.UI.LayoutGroup>();
                    if (layout != null) layout.enabled = false;

                    var containerRect = container.GetComponent<RectTransform>();
                    var playerRect = playerNameInput.GetComponent<RectTransform>();
                    
                    if (containerRect != null && playerRect != null)
                    {
                        containerRect.anchoredPosition = new Vector2(containerRect.anchoredPosition.x, containerRect.anchoredPosition.y + 200f);
                        Logger.LogInfo("Container moved up.");
                    }

                    Transform startLan = leftPanel.Find("StartLanButtonRoot");
                    Transform joinLan = leftPanel.Find("JoinLanButtonRoot");
                    Transform menuBtns = container.Find("MenuButtonsPanel");

                    if (menuBtns != null)
                    {
                        if (startLan != null)
                        {
                            startLan.SetParent(menuBtns, false);
                            startLan.SetAsFirstSibling();
                            Logger.LogInfo("Moved StartLanButtonRoot to MenuButtonsPanel top.");
                        }
                        if (joinLan != null)
                        {
                            joinLan.SetParent(menuBtns, false);
                            joinLan.SetSiblingIndex(startLan != null ? 1 : 0);
                            Logger.LogInfo("Moved JoinLanButtonRoot to MenuButtonsPanel top.");
                        }
                    }
                }
            }

            Transform newsPanel = mainCanvas.Find("NewsPanel");
            if (newsPanel != null)
            {
                var h = newsPanel.gameObject.GetComponent<HoverHideComponent>();
                if (h == null) h = newsPanel.gameObject.AddComponent<HoverHideComponent>();
                h.area = HoverHideComponent.HoverArea.BottomLeft;
                h.hideOffset = new Vector2(0, -300f);
                Logger.LogInfo("HoverHideComponent added to NewsPanel.");
            }

            Transform newLogo = mainCanvas.Find("Prejoin menu/NewLogo");
            if (newLogo != null)
            {
                var h = newLogo.gameObject.GetComponent<HoverHideComponent>();
                if (h == null) h = newLogo.gameObject.AddComponent<HoverHideComponent>();
                h.area = HoverHideComponent.HoverArea.TopRight;
                h.hideOffset = new Vector2(0, 300f);
                Logger.LogInfo("HoverHideComponent added to NewLogo.");
            }

            Transform hintPanel = mainCanvas.Find("HintPanel");
            if (hintPanel != null)
            {
                var h = hintPanel.gameObject.GetComponent<HoverHideComponent>();
                if (h == null) h = hintPanel.gameObject.AddComponent<HoverHideComponent>();
                h.area = HoverHideComponent.HoverArea.BottomCenter;
                h.hideOffset = new Vector2(0, -300f);
                Logger.LogInfo("HoverHideComponent added to HintPanel.");
            }
        }
    }

    [HarmonyPatch(typeof(MainMenu), "Start")]
    internal static class MainMenuStartPatch
    {
        private static void Postfix(MainMenu __instance)
        {
            if (Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(Plugin.Instance.ApplyMenuChangesRoutine(__instance));
            }
        }
    }

    public class HoverHideComponent : MonoBehaviour
    {
        public enum HoverArea { BottomLeft, BottomCenter, AnyBottom, TopRight }
        public HoverArea area = HoverArea.BottomLeft;
        public Vector2 hideOffset = new Vector2(0, -300f);

        private RectTransform rect;
        private Vector2 originalPos;
        private Vector2 hiddenPos;
        private float transitionSpeed = 10f;
        private bool isHidden = true;

        void Start()
        {
            rect = GetComponent<RectTransform>();
            originalPos = rect.anchoredPosition;
            hiddenPos = originalPos + hideOffset;
            rect.anchoredPosition = hiddenPos;
        }

        void Update()
        {
            Vector2 mousePos = Input.mousePosition;
            bool near = false;

            if (area == HoverArea.BottomLeft)
            {
                near = (mousePos.x < Screen.width * 0.35f && mousePos.y < Screen.height * 0.35f);
            }
            else if (area == HoverArea.BottomCenter)
            {
                near = (mousePos.x > Screen.width * 0.25f && mousePos.x < Screen.width * 0.75f && mousePos.y < Screen.height * 0.25f);
            }
            else if (area == HoverArea.TopRight)
            {
                near = (mousePos.x > Screen.width * 0.65f && mousePos.y > Screen.height * 0.65f);
            }
            else if (area == HoverArea.AnyBottom)
            {
                near = (mousePos.y < Screen.height * 0.3f);
            }

            isHidden = !near;

            Vector2 targetPos = isHidden ? hiddenPos : originalPos;
            rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, targetPos, Time.deltaTime * transitionSpeed);
        }
    }
}
