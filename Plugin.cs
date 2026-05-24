using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.EventSystems;

namespace NewMenuLayoutMod
{
    [BepInPlugin("com.newmenulayout", "New Menu Layout", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: {scene.name} (Index: {scene.buildIndex})");
            StartCoroutine(ApplyMenuChangesRoutine());
        }

        private IEnumerator ApplyMenuChangesRoutine()
        {
            // Wait a little bit for UI to be fully built
            yield return new WaitForSeconds(1f);

            bool applied = false;
            foreach (var canvas in FindObjectsOfType<Canvas>())
            {
                if (canvas.rootCanvas == canvas && canvas.name == "MainCanvas")
                {
                    Logger.LogInfo($"Found Root Canvas: {canvas.name}, applying mod...");
                    ApplyModToCanvas(canvas.transform);
                    applied = true;
                }
            }
        }

        private void ApplyModToCanvas(Transform mainCanvas)
        {
            // Find LeftPanel
            Transform leftPanel = RecursiveFind(mainCanvas, "LeftPanel");
            if (leftPanel != null)
            {
                var img = leftPanel.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color;
                    img.color = new Color(c.r, c.g, c.b, 0f);
                    Logger.LogInfo("LeftPanel opacity set to 0.");
                }

                // Try to find Container and playerNameInput
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
                        if (container.GetComponent<ContainerMovedFlag>() == null)
                        {
                            containerRect.anchoredPosition = new Vector2(containerRect.anchoredPosition.x, containerRect.anchoredPosition.y + 200f);
                            container.gameObject.AddComponent<ContainerMovedFlag>();
                            Logger.LogInfo("Container moved up.");
                        }
                    }

                    // Move LAN buttons into MenuButtonsPanel if they exist
                    Transform startLan = leftPanel.Find("StartLanButtonRoot");
                    Transform joinLan = leftPanel.Find("JoinLanButtonRoot");
                    Transform menuBtns = container.Find("MenuButtonsPanel");

                    if (menuBtns != null)
                    {
                        if (startLan != null)
                        {
                            startLan.SetParent(menuBtns, false);
                            startLan.SetAsLastSibling();
                            Logger.LogInfo("Moved StartLanButtonRoot to MenuButtonsPanel bottom.");
                        }
                        if (joinLan != null)
                        {
                            joinLan.SetParent(menuBtns, false);
                            joinLan.SetAsLastSibling();
                            Logger.LogInfo("Moved JoinLanButtonRoot to MenuButtonsPanel bottom.");
                        }
                    }
                }
            }

            // Find NewsPanel
            Transform newsPanel = RecursiveFind(mainCanvas, "NewsPanel");
            if (newsPanel != null)
            {
                var h = newsPanel.gameObject.GetComponent<HoverHideComponent>();
                if (h == null) h = newsPanel.gameObject.AddComponent<HoverHideComponent>();
                h.area = HoverHideComponent.HoverArea.BottomLeft;
                h.hideOffset = new Vector2(0, -300f);
                Logger.LogInfo("HoverHideComponent added to NewsPanel.");
            }

            // Find NewLogo
            Transform newLogo = RecursiveFind(mainCanvas, "NewLogo");
            if (newLogo != null)
            {
                var h = newLogo.gameObject.GetComponent<HoverHideComponent>();
                if (h == null) h = newLogo.gameObject.AddComponent<HoverHideComponent>();
                h.area = HoverHideComponent.HoverArea.TopRight;
                h.hideOffset = new Vector2(0, 300f); // Hide to the top
                Logger.LogInfo("HoverHideComponent added to NewLogo.");
            }

            // Find HintPanel
            Transform hintPanel = RecursiveFind(mainCanvas, "HintPanel");
            if (hintPanel != null)
            {
                var h = hintPanel.gameObject.GetComponent<HoverHideComponent>();
                if (h == null) h = hintPanel.gameObject.AddComponent<HoverHideComponent>();
                h.area = HoverHideComponent.HoverArea.BottomCenter;
                h.hideOffset = new Vector2(0, -300f); // Hide down
                Logger.LogInfo("HoverHideComponent added to HintPanel.");
            }
        }

        private Transform RecursiveFind(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = RecursiveFind(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
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
            
            bool inScreen = mousePos.x >= 0 && mousePos.x <= Screen.width && mousePos.y >= 0 && mousePos.y <= Screen.height;

            if (inScreen)
            {
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
            }

            isHidden = !near;

            Vector2 targetPos = isHidden ? hiddenPos : originalPos;
            rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, targetPos, Time.deltaTime * transitionSpeed);
        }
    }

    public class ContainerMovedFlag : MonoBehaviour
    {
    }
}
