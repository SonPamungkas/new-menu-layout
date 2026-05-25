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
using UnityEngine.Video;

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

        private static GameObject persistentBackgroundCanvas;
        private static GameObject originalBackgroundReference;

        private static GameObject spCanvas;
        private static GameObject mpCanvas;
        private static Texture videoTexture;
        private static readonly HashSet<Transform> processedTransforms = new HashSet<Transform>();
        private static readonly HashSet<int> movedContainers = new HashSet<int>();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: {scene.name} (Index: {scene.buildIndex})");
            sceneLoadedTime = Time.time;
            processedTransforms.Clear();
            
            // Ensure all three canvases are fully active and pre-generated
            if (persistentBackgroundCanvas != null) persistentBackgroundCanvas.SetActive(true);
            if (spCanvas != null) spCanvas.SetActive(true);
            if (mpCanvas != null) mpCanvas.SetActive(true);

            // Synchronously hide native backgrounds immediately to prevent transition blips
            HideAllNativeBackgroundsImmediately();

            StartCoroutine(ApplyMenuChangesRoutine());
            StartCoroutine(MoveLanButtonsRoutine());
        }

        private void ProcessBackgroundTransform(Transform t)
        {
            if (t == null || t.gameObject == originalBackgroundReference || t.name == "MMR_EverywhereVideo") return;

            try
            {
                if (IsEverywhereEnabled() && videoTexture != null)
                {
                    // Do NOT disable the original Image or RawImage, as it breaks Raycast targets (softlock)
                    var rawImgComp = t.GetComponent<RawImage>();
                    if (rawImgComp != null && rawImgComp.gameObject.name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        rawImgComp.texture = videoTexture;
                        rawImgComp.color = Color.white;
                        rawImgComp.enabled = true;
                    }
                    else
                    {
                        var childTrans = t.Find("MMR_EverywhereVideo");
                        GameObject childGo = childTrans != null ? childTrans.gameObject : new GameObject("MMR_EverywhereVideo");
                        if (childTrans == null)
                        {
                            childGo.transform.SetParent(t, false);
                            var rt = childGo.AddComponent<RectTransform>();
                            rt.anchorMin = Vector2.zero;
                            rt.anchorMax = Vector2.one;
                            rt.offsetMin = Vector2.zero;
                            rt.offsetMax = Vector2.zero;
                            childGo.transform.SetAsFirstSibling();
                        }

                        var rawImg = childGo.GetComponent<RawImage>() ?? childGo.AddComponent<RawImage>();
                        rawImg.texture = videoTexture;
                        rawImg.color = Color.white;
                        rawImg.raycastTarget = false; // CRITICAL: prevent softlock!
                        rawImg.enabled = true;

                        var le = childGo.GetComponent<LayoutElement>() ?? childGo.AddComponent<LayoutElement>();
                        le.ignoreLayout = true;

                        childGo.SetActive(true);
                    }
                }
                else
                {
                    // Regular mode: we hide the backgrounds so the persistent video shows through
                    if (t.name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var img = t.GetComponent<Image>();
                        if (img != null) img.enabled = false;

                        var rawImg = t.GetComponent<RawImage>();
                        if (rawImg != null) rawImg.enabled = false;
                    }

                    var childTrans = t.Find("MMR_EverywhereVideo");
                    if (childTrans != null) childTrans.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error processing background transform {t.name}: {ex.Message}");
            }
        }

        private void HideAllNativeBackgroundsImmediately()
        {
            foreach (var canvas in FindObjectsOfType<Canvas>())
            {
                if (canvas.gameObject == persistentBackgroundCanvas || 
                    canvas.gameObject == spCanvas || 
                    canvas.gameObject == mpCanvas)
                {
                    continue;
                }

                // Hide/Stream the main "background" inside ANY native canvas
                Transform bg = RecursiveFind(canvas.transform, "background");
                if (bg != null && bg.gameObject != originalBackgroundReference)
                {
                    ProcessBackgroundTransform(bg);
                    Logger.LogInfo($"Immediately processed native background in canvas: {canvas.name}");
                }

                // Also hide/stream other major backgrounds
                HideMajorBackgroundsRecursive(canvas.transform);
            }
        }

        private void HideMajorBackgroundsRecursive(Transform t)
        {
            if (t == null) return;
            string name = t.name;

            bool shouldProcess = false;

            bool isMajorBackground = name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isMajorBackground && t.gameObject != originalBackgroundReference)
            {
                var rt = t.GetComponent<RectTransform>();
                if (rt != null && rt.rect.width > 300f && rt.rect.height > 300f)
                {
                    shouldProcess = true;
                }
            }

            if (shouldProcess && t.gameObject != originalBackgroundReference)
            {
                ProcessBackgroundTransform(t);
                Logger.LogInfo($"Immediately processed native background: {name}");
            }

            for (int i = 0; i < t.childCount; i++)
            {
                HideMajorBackgroundsRecursive(t.GetChild(i));
            }
        }


        private void CreateDedicatedCanvas(ref GameObject canvasRef, string canvasName)
        {
            if (canvasRef != null) return;

            Logger.LogInfo($"Synchronously pre-generating dedicated background canvas: {canvasName}");
            canvasRef = new GameObject(canvasName);
            canvasRef.hideFlags = HideFlags.HideAndDontSave;
            
            Canvas c = canvasRef.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = -100;
            
            CanvasScaler cs = canvasRef.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = 0.5f;

            GameObject.DontDestroyOnLoad(canvasRef);

            // Create background RawImage child
            GameObject bgGo = new GameObject("background");
            bgGo.transform.SetParent(canvasRef.transform, false);

            var rawImg = bgGo.AddComponent<RawImage>();
            rawImg.color = Color.white;
            
            if (videoTexture != null)
            {
                rawImg.texture = videoTexture;
                rawImg.enabled = true;
            }
            else if (originalBackgroundReference != null)
            {
                var origRaw = originalBackgroundReference.GetComponent<RawImage>();
                if (origRaw != null && origRaw.texture != null)
                {
                    videoTexture = origRaw.texture;
                    rawImg.texture = videoTexture;
                    rawImg.enabled = true;
                }
                else
                {
                    rawImg.enabled = false;
                }
            }
            else
            {
                rawImg.enabled = false; // Prevent whiteout until texture is assigned
            }

            // Stretch to fill
            RectTransform rt = bgGo.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            canvasRef.SetActive(true);
        }

        private float sceneLoadedTime = 0f;
        private float lastScanTime = 0f;

        private void Update()
        {
            // Force active video players to play reactively (only on persistent original canvas)
            ForcePlayInCanvas(persistentBackgroundCanvas);

            // Fetch/Sync texture from original background using GetComponentInChildren
            if (originalBackgroundReference != null)
            {
                Texture currentTex = null;
                var rawImg = originalBackgroundReference.GetComponentInChildren<RawImage>();
                if (rawImg != null && rawImg.texture != null)
                {
                    currentTex = rawImg.texture;
                }
                else
                {
                    var vp = originalBackgroundReference.GetComponentInChildren<VideoPlayer>();
                    if (vp != null && vp.targetTexture != null)
                    {
                        currentTex = vp.targetTexture;
                    }
                }

                if (currentTex != null && videoTexture != currentTex)
                {
                    videoTexture = currentTex;
                    Logger.LogInfo($"Detected new video/image texture: {videoTexture.name}. Updating canvases...");
                    AssignTextureToCanvasRawImage(spCanvas, force: true);
                    AssignTextureToCanvasRawImage(mpCanvas, force: true);
                }
            }

            // Dynamically assign videoTexture once it becomes available
            AssignTextureToCanvasRawImage(spCanvas);
            AssignTextureToCanvasRawImage(mpCanvas);

            // Apply texture replacement on major backgrounds to resolve any layering issues.
            float timeSinceLoad = Time.time - sceneLoadedTime;
            // Heavily throttle FindObjectsOfType to fix slow startup lag!
            // First 2 seconds: scan every 0.25s. After that: scan every 1.0s.
            float scanInterval = (timeSinceLoad < 2.0f) ? 0.25f : 1.0f;
            bool shouldScan = (Time.time - lastScanTime >= scanInterval);

            if (shouldScan)
            {
                lastScanTime = Time.time;

                foreach (var canvas in FindObjectsOfType<Canvas>())
                {
                    if (canvas.gameObject == persistentBackgroundCanvas || 
                        canvas.gameObject == spCanvas || 
                        canvas.gameObject == mpCanvas)
                    {
                        continue;
                    }
                    ApplyVideoToTargetBackground(canvas.transform);
                }
            }
        }

        private void AssignTextureToCanvasRawImage(GameObject canvasGo, bool force = false)
        {
            if (canvasGo != null && videoTexture != null)
            {
                var rawImg = canvasGo.GetComponentInChildren<RawImage>();
                if (rawImg != null && (rawImg.texture == null || force || rawImg.texture != videoTexture))
                {
                    rawImg.texture = videoTexture;
                    rawImg.enabled = true; // Enable raw image now that texture is assigned
                    Logger.LogInfo($"Assigned/Synchronized video texture to raw image in canvas: {canvasGo.name}");
                }
            }
        }

        private void ForcePlayInCanvas(GameObject canvasGo)
        {
            if (canvasGo != null && canvasGo.activeInHierarchy)
            {
                var vp = canvasGo.GetComponentInChildren<VideoPlayer>();
                if (vp != null && !vp.isPlaying && vp.isActiveAndEnabled)
                {
                    vp.Play();
                }
            }
        }

        private void ApplyVideoToTargetBackground(Transform t)
        {
            if (t == null || t.name == "MMR_EverywhereVideo") return;

            if (processedTransforms.Contains(t))
            {
                // Already evaluated this transform. Just scan its children in case new ones were added
                for (int i = 0; i < t.childCount; i++)
                {
                    ApplyVideoToTargetBackground(t.GetChild(i));
                }
                return;
            }

            string name = t.name;
            bool everywhere = IsEverywhereEnabled();
            bool shouldProcess = false;

            if (everywhere)
            {
                var rt = t.GetComponent<RectTransform>();
                if (rt != null && rt.rect.width > 20f && rt.rect.height > 20f)
                {
                    bool hasGraphic = false;
                    var img = t.GetComponent<Image>();
                    var raw = t.GetComponent<RawImage>();
                    if (img != null && img.color.a > 0.01f) hasGraphic = true;
                    if (raw != null && raw.color.a > 0.01f) hasGraphic = true;

                    if (hasGraphic)
                    {
                        string lowerName = name.ToLower();
                        // Only blacklist transparent fade overlays and blockers
                        bool isBlacklisted = lowerName.Contains("fade") ||
                                             lowerName.Contains("overlay") ||
                                             lowerName.Contains("blocker") ||
                                             lowerName.Contains("border") ||
                                             lowerName.Contains("masker") ||
                                             lowerName.Contains("outline");

                        if (!isBlacklisted)
                        {
                            shouldProcess = true;
                        }
                    }
                }
            }
            else
            {
                bool isMajorBackground = name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isMajorBackground && t.gameObject.activeInHierarchy)
                {
                    var rt = t.GetComponent<RectTransform>();
                    if (rt != null && rt.rect.width > 300f && rt.rect.height > 300f)
                    {
                        shouldProcess = true;
                    }
                }
            }

            if (shouldProcess)
            {
                if (t.GetComponent<VideoBackgroundAppliedFlag>() == null)
                {
                    ProcessBackgroundTransform(t);
                    t.gameObject.AddComponent<VideoBackgroundAppliedFlag>();
                    Logger.LogInfo($"Processed native background to let seamless video show through: {t.name}");
                }
            }

            processedTransforms.Add(t);

            for (int i = 0; i < t.childCount; i++)
            {
                ApplyVideoToTargetBackground(t.GetChild(i));
            }
        }

        private IEnumerator ApplyMenuChangesRoutine()
        {
            // Wait 1 frame for UI to initialize
            yield return null;

            foreach (var canvas in FindObjectsOfType<Canvas>())
            {
                if (canvas.rootCanvas == canvas && canvas.name == "MainCanvas")
                {
                    Logger.LogInfo($"Found Root Canvas: {canvas.name}, applying mod...");
                    ApplyModToCanvas(canvas.transform);
                }
            }
        }

        private IEnumerator MoveLanButtonsRoutine()
        {
            // Wait for LAN mod to spawn its buttons (it waits 0.2s)
            yield return new WaitForSeconds(1.5f);

            foreach (var canvas in FindObjectsOfType<Canvas>())
            {
                if (canvas.rootCanvas == canvas && canvas.name == "MainCanvas")
                {
                    Transform leftPanel = RecursiveFind(canvas.transform, "LeftPanel");
                    if (leftPanel != null)
                    {
                        Transform container = leftPanel.Find("Container");
                        if (container != null)
                        {
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
                }
            }
        }

        private void ApplyModToCanvas(Transform mainCanvas)
        {
            // Persist background
            Transform bg = RecursiveFind(mainCanvas, "background");
            if (bg != null)
            {
                if (persistentBackgroundCanvas == null)
                {
                    persistentBackgroundCanvas = new GameObject("PersistentBackgroundCanvas");
                    persistentBackgroundCanvas.hideFlags = HideFlags.HideAndDontSave;
                    
                    Canvas c = persistentBackgroundCanvas.AddComponent<Canvas>();
                    c.renderMode = RenderMode.ScreenSpaceOverlay;
                    c.sortingOrder = -100;
                    
                    CanvasScaler cs = persistentBackgroundCanvas.AddComponent<CanvasScaler>();
                    var origCs = mainCanvas.GetComponent<CanvasScaler>();
                    if (origCs != null)
                    {
                        cs.uiScaleMode = origCs.uiScaleMode;
                        cs.referenceResolution = origCs.referenceResolution;
                        cs.screenMatchMode = origCs.screenMatchMode;
                        cs.matchWidthOrHeight = origCs.matchWidthOrHeight;
                    }

                    GameObject.DontDestroyOnLoad(persistentBackgroundCanvas);
                    
                    bg.SetParent(persistentBackgroundCanvas.transform, false);
                    bg.SetAsFirstSibling();
                    bg.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    
                    originalBackgroundReference = bg.gameObject;
                    SanitizePersistentBackground(originalBackgroundReference);
                    
                    // Immediately cache the video texture to enable pre-generation
                    var rawImg = originalBackgroundReference.GetComponentInChildren<RawImage>();
                    if (rawImg != null) videoTexture = rawImg.texture;
                    var vp = originalBackgroundReference.GetComponentInChildren<VideoPlayer>();
                    if (vp != null && videoTexture == null) videoTexture = vp.targetTexture;

                    Logger.LogInfo("Persisted background video.");

                    // Synchronously pre-generate SP and MP canvases immediately
                    CreateDedicatedCanvas(ref spCanvas, "SPcanvas");
                    CreateDedicatedCanvas(ref mpCanvas, "MPcanvas");
                }
                else
                {
                    if (bg.gameObject != originalBackgroundReference && bg.parent != persistentBackgroundCanvas.transform)
                    {
                        var vp = bg.GetComponent<VideoPlayer>();
                        if (vp != null)
                        {
                            vp.enabled = false;
                            Destroy(vp);
                        }

                        var rawImg = bg.GetComponent<RawImage>();
                        if (rawImg != null && videoTexture != null)
                        {
                            rawImg.texture = videoTexture;
                            rawImg.color = Color.white;
                            rawImg.enabled = true;
                        }

                        Logger.LogInfo("Redirected duplicate MainMenu background to seamless videoTexture.");
                    }
                }
            }

            // Find LeftPanel
            Transform leftPanel = RecursiveFind(mainCanvas, "LeftPanel");
            if (leftPanel != null)
            {
                var img = leftPanel.GetComponent<Image>();
                if (img != null)
                {
                    var col = img.color;
                    img.color = new Color(col.r, col.g, col.b, 0f);
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

        private void SanitizePersistentBackground(GameObject bgGo)
        {
            if (bgGo == null) return;
            try
            {
                var components = bgGo.GetComponents<MonoBehaviour>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    
                    string typeName = comp.GetType().FullName;
                    if (!typeName.StartsWith("UnityEngine.") && !typeName.StartsWith("UnityEngine.UI."))
                    {
                        Logger.LogInfo($"Sanitizing persistent background: Destroying custom component {typeName}");
                        Destroy(comp);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error sanitizing background: {ex.Message}");
            }
        }

        private bool IsEverywhereEnabled()
        {
            try
            {
                var replacerType = Type.GetType("MainMenuReplacerMod.MainMenuReplacer, MainMenuReplacer");
                if (replacerType == null) return false;
                
                var instanceField = replacerType.GetField("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var instance = instanceField?.GetValue(null);
                if (instance == null) return false;

                var everywhereField = replacerType.GetField("cfgEverywhere", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var configEntry = everywhereField?.GetValue(instance);
                if (configEntry == null) return false;

                var valueProp = configEntry.GetType().GetProperty("Value");
                return (bool)(valueProp?.GetValue(configEntry) ?? false);
            }
            catch
            {
                return false;
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

    public class VideoBackgroundAppliedFlag : MonoBehaviour
    {
    }
}
