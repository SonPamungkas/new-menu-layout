using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.Audio;

namespace MainMenuReplacerMod
{
    [BepInPlugin("com.neutralobserver.mainmenureplacer", "MainMenu Replacer", "1.2")] //permanent
    public class MainMenuReplacer : BaseUnityPlugin
    {
        internal static MainMenuReplacer Instance;

        private const string DefaultMenuFolderName = "MainMenuMedia";
        private const string DefaultLoadingFolderName = "LoadingScreens";

        // Menu Config
        private ConfigEntry<string> cfgMenuMediaFolder;
        private ConfigEntry<bool> cfgMenuRandomize;
        private ConfigEntry<bool> cfgMenuAvoidRepeats;

        // Loading Config
        private ConfigEntry<string> cfgLoadingFolder;
        private ConfigEntry<bool> cfgLoadingRandomize;
        private ConfigEntry<bool> cfgLoadingAvoidRepeats;
        private ConfigEntry<bool> cfgLoadingNeverUseDefault;

        // Audio Config
        private ConfigEntry<bool> cfgMenuVideoTieToMusic;
        private ConfigEntry<float> cfgMenuVideoVolumeMultiplier;
        private ConfigEntry<float> cfgMenuVideoFallbackVolume;
        private ConfigEntry<bool> cfgMenuVideoMute;

        private ConfigEntry<bool> cfgDebugLogging;
        public ConfigEntry<bool> cfgEverywhere;

        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg" };
        private static readonly string[] VideoExts = { ".mp4", ".webm" };

        private Sprite _menuSpriteCache;
        private AudioSource _menuVideoAudioSource;
        private VideoPlayer _menuVideoPlayer;
        private AudioMixerGroup _musicGroup;
        private bool _isPlayingVideo = false;

        private readonly List<RawImage> _cachedBackgroundRawImages = new List<RawImage>();
        private string _currentMenuMediaPath = null;
        private bool _isMenuSessionActive = false;
        private AudioSource _vanillaMenuMusic;
        private GameObject _currentMenuOwner;

        // Media items
        private class MediaItem
        {
            public string Path;
            public bool IsVideo;
        }

        // Shuffle bags
        private static readonly object BagLock = new object();
        private static List<string> _menuBag = new List<string>();
        private static int _menuBagIndex = 0;
        private static string _menuBagFolder = null;
        private static int _menuBagSig = 0;

        private static List<string> _loadingBag = new List<string>();
        private static int _loadingBagIndex = 0;
        private static string _loadingBagFolder = null;
        private static int _loadingBagSig = 0;

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            Instance = this;
            string pluginRoot = Paths.PluginPath;

            cfgMenuMediaFolder = Config.Bind("MainMenu", "MainMenuMediaFolder",
                Path.Combine(pluginRoot, DefaultMenuFolderName),
                "Folder containing custom main menu images or videos for playlist rotation.");

            cfgMenuRandomize = Config.Bind("MainMenu", "Randomize",
                true,
                "If true, shuffles the main menu media playlist.");

            cfgMenuAvoidRepeats = Config.Bind("MainMenu", "AvoidRepeats",
                true,
                "If true and Randomize=true, cycles through all files before repeating.");

            cfgLoadingFolder = Config.Bind("LoadingScreen", "LoadingMediaFolder",
                Path.Combine(pluginRoot, DefaultLoadingFolderName),
                "Folder containing custom loading screen images.");

            cfgLoadingRandomize = Config.Bind("LoadingScreen", "Randomize",
                true,
                "If true, chooses random/rotating file from LoadingMediaFolder.");

            cfgLoadingAvoidRepeats = Config.Bind("LoadingScreen", "AvoidRepeats",
                true,
                "If true and Randomize=true, cycles through all files before repeating.");

            cfgLoadingNeverUseDefault = Config.Bind("LoadingScreen", "NeverUseDefaultImages",
                true,
                "If true and LoadingMediaFolder has at least one valid image, the game will never show built-in/default loading images.");

            cfgMenuVideoTieToMusic = Config.Bind("MainMenuVideo", "TieToMusicVolume",
                true,
                "If true, tracks the game's PlayerPrefs key 'MusicVolume' (0..1) for menu video audio.");

            cfgMenuVideoVolumeMultiplier = Config.Bind("MainMenuVideo", "VolumeMultiplier",
                1.0f,
                "Extra multiplier applied to MusicVolume (0.0 - 2.0).");

            cfgMenuVideoFallbackVolume = Config.Bind("MainMenuVideo", "FallbackVolume",
                0.75f,
                "Used if MusicVolume isn't found (0.0 - 1.0).");

            cfgMenuVideoMute = Config.Bind("MainMenuVideo", "Mute",
                false,
                "If true, mutes the main menu video audio/music in general (volume to 0) independently from other options.");

            cfgEverywhere = Config.Bind("MainMenuVideo", "Everywhere",
                false,
                "[Easter Egg] If enabled, streams the active video background to ALL UI backgrounds in the game (including settings, sub-menus, singleplayer, multiplayer, etc.)!");

            cfgDebugLogging = Config.Bind("Debug", "VerboseLogs",
                true,
                "If true, logs selection + patch activity.");

            EnsureDirectory(cfgMenuMediaFolder.Value);
            EnsureDirectory(cfgLoadingFolder.Value);

            Logger.LogInfo($"[MainMenuReplacer] Loaded. Playlist folder: {cfgMenuMediaFolder.Value}");

            var harmony = new Harmony("com.neutralobserver.mainmenureplacer.harmony");
            harmony.PatchAll(typeof(LoadingScreenPatches));

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void EnsureDirectory(string path)
        {
            try { if (!Directory.Exists(path)) Directory.CreateDirectory(path); }
            catch { /* ignore */ }
        }

        private void Update()
        {
            if (_isPlayingVideo && _menuVideoPlayer != null && _menuVideoAudioSource != null)
            {
                if (_menuVideoPlayer.isPlaying)
                {
                    float volume = cfgMenuVideoMute.Value ? 0f : (ResolveMusicVolume01() * Mathf.Clamp(cfgMenuVideoVolumeMultiplier.Value, 0f, 2f));
                    _menuVideoAudioSource.volume = volume;

                    if (!cfgMenuVideoMute.Value && _vanillaMenuMusic != null)
                    {
                        _vanillaMenuMusic.volume = 0f;
                    }
                }
            }
        }

        private float ResolveMusicVolume01()
        {
            if (!cfgMenuVideoTieToMusic.Value)
                return Mathf.Clamp01(cfgMenuVideoFallbackVolume.Value);

            if (PlayerPrefs.HasKey("MusicVolume"))
                return Mathf.Clamp01(PlayerPrefs.GetFloat("MusicVolume", 0.75f));

            return Mathf.Clamp01(cfgMenuVideoFallbackVolume.Value);
        }

        private void TryBindToGameMusicMixerGroup(AudioSource audioSource)
        {
            if (!cfgMenuVideoTieToMusic.Value) return;
            if (_musicGroup != null) { audioSource.outputAudioMixerGroup = _musicGroup; return; }

            try
            {
                var smType = AccessTools.TypeByName("SoundManager");
                if (smType == null) return;

                var fiI = AccessTools.Field(smType, "i");
                var smInstance = fiI?.GetValue(null);
                if (smInstance == null) return;

                var piVolumes = AccessTools.Property(smType, "Volumes");
                var volumesObj = piVolumes?.GetValue(smInstance);
                if (volumesObj == null) return;

                var mixerField = AccessTools.Field(volumesObj.GetType(), "_mixer");
                var mixer = mixerField?.GetValue(volumesObj) as AudioMixer;
                if (mixer == null) return;

                var groups = mixer.FindMatchingGroups(string.Empty);
                if (groups == null || groups.Length == 0) return;

                _musicGroup = groups.FirstOrDefault(g => g != null && g.name.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0)
                           ?? groups.FirstOrDefault(g => g != null && g.name.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0)
                           ?? groups[0];

                if (_musicGroup != null)
                {
                    audioSource.outputAudioMixerGroup = _musicGroup;
                    if (cfgDebugLogging.Value)
                        Logger.LogInfo($"[MainMenuReplacer] Bound menu video audio to mixer group: {_musicGroup.name}");
                }
            }
            catch { /* swallow */ }
        }

        private void StopMenuPlayback()
        {
            if (_menuVideoPlayer != null)
            {
                _menuVideoPlayer.Stop();
                _menuVideoPlayer.targetTexture = null;
                _menuVideoPlayer.enabled = false;
            }
            if (_menuVideoAudioSource != null)
            {
                _menuVideoAudioSource.Stop();
                _menuVideoAudioSource.enabled = false;
            }
            _isPlayingVideo = false;
            _isMenuSessionActive = false;
            _currentMenuMediaPath = null;
            Logger.LogInfo("[MainMenuReplacer] Playback stopped for non-menu scene. Session reset.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _cachedBackgroundRawImages.Clear();

            string sceneName = scene.name.ToLower();
            bool isMenuScene = sceneName.Contains("menu") || 
                               sceneName.Contains("singleplayer") || 
                               sceneName.Contains("multiplayer") ||
                               sceneName.Contains("lobby") ||
                               sceneName.Contains("workshop");

            if (!isMenuScene)
            {
                StopMenuPlayback();
                return;
            }

            // Only initialize the media choice if we actually entered MainMenu natively.
            if (scene.name != "MainMenu")
                return;

            // Cache vanilla menu music
            foreach (var src in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (src.clip != null && src.clip.name.IndexOf("Ignition", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _vanillaMenuMusic = src;
                    break;
                }
            }

            Logger.LogInfo("[MainMenuReplacer] MainMenu scene detected, applying playlist item.");

            Image[] allImages = Resources.FindObjectsOfTypeAll<Image>();
            var candidates = allImages.Where(img =>
                img != null && img.gameObject != null &&
                img.gameObject.scene.name == "MainMenu" &&
                img.rectTransform != null &&
                img.rectTransform.anchorMin == Vector2.zero &&
                img.rectTransform.anchorMax == Vector2.one).ToList();
            Image target = candidates.FirstOrDefault(c => c.gameObject.name.ToLower().Contains("background"))
                        ?? candidates.OrderBy(c => c.rectTransform.GetSiblingIndex()).FirstOrDefault();

            if (target == null) return;

            var media = ChooseNextMenuMedia();
            if (media == null) return; // No media found

            _isMenuSessionActive = true;
            _currentMenuMediaPath = media.Path;

            if (media.IsVideo)
            {
                ApplyMenuVideo(target.gameObject, media.Path);
            }
            else
            {
                ApplyMenuImage(target, media.Path);
            }
        }

        private void OnVideoEnd(VideoPlayer source)
        {
            if (source == _menuVideoPlayer && _currentMenuOwner != null)
            {
                _isMenuSessionActive = false;
                var media = ChooseNextMenuMedia();
                
                if (media != null)
                {
                    _isMenuSessionActive = true;
                    _currentMenuMediaPath = media.Path;
                    if (media.IsVideo)
                    {
                        ApplyMenuVideo(_currentMenuOwner, media.Path);
                    }
                    else
                    {
                        ApplyMenuImage(_currentMenuOwner.GetComponent<Image>(), media.Path);
                        StopMenuPlayback();
                    }
                }
            }
        }

        private MediaItem ChooseNextMenuMedia()
        {
            if (_isMenuSessionActive && _currentMenuMediaPath != null)
            {
                Logger.LogInfo("[MainMenuReplacer] Resuming existing menu session seamlessly.");
                return new MediaItem
                {
                    Path = _currentMenuMediaPath,
                    IsVideo = VideoExts.Contains(Path.GetExtension(_currentMenuMediaPath).ToLowerInvariant())
                };
            }

            string folder = cfgMenuMediaFolder.Value;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;

            var allFiles = Directory.GetFiles(folder)
                .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()) || 
                            VideoExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (allFiles.Count == 0) return null;

            string selectedFile;
            if (!cfgMenuRandomize.Value)
            {
                selectedFile = allFiles[0];
            }
            else if (!cfgMenuAvoidRepeats.Value || allFiles.Count == 1)
            {
                selectedFile = allFiles[UnityEngine.Random.Range(0, allFiles.Count)];
            }
            else
            {
                lock (BagLock)
                {
                    int sig = folder.GetHashCode() ^ allFiles.Count;
                    if (_menuBagFolder == null || !string.Equals(_menuBagFolder, folder, StringComparison.OrdinalIgnoreCase) || _menuBagSig != sig || _menuBag.Count != allFiles.Count)
                    {
                        _menuBagFolder = folder;
                        _menuBagSig = sig;
                        _menuBagIndex = 0;
                        _menuBag = Enumerable.Range(0, allFiles.Count).Select(i => i.ToString()).ToList();
                        Shuffle(_menuBag);

                        // Ensure we never start with the first file (index 0) if there are other options
                        if (_menuBag.Count > 1 && _menuBag[0] == "0")
                        {
                            string temp = _menuBag[0];
                            _menuBag[0] = _menuBag[1];
                            _menuBag[1] = temp;
                        }
                    }

                    if (_menuBagIndex >= _menuBag.Count)
                    {
                        string lastPlayed = _menuBag[_menuBag.Count - 1];
                        Shuffle(_menuBag);
                        
                        // Prevent the same file from playing twice in a row across bag resets
                        if (_menuBag.Count > 1 && _menuBag[0] == lastPlayed)
                        {
                            // Swap first and second element
                            string temp = _menuBag[0];
                            _menuBag[0] = _menuBag[1];
                            _menuBag[1] = temp;
                        }
                        
                        _menuBagIndex = 0;
                    }

                    int idx = int.Parse(_menuBag[_menuBagIndex++]);
                    idx = Mathf.Clamp(idx, 0, allFiles.Count - 1);
                    selectedFile = allFiles[idx];
                }
            }

            if (cfgDebugLogging.Value)
                Logger.LogInfo($"[MainMenuReplacer] Selected Menu Media: {Path.GetFileName(selectedFile)}");

            return new MediaItem
            {
                Path = "file://" + selectedFile.Replace("\\", "/"),
                IsVideo = VideoExts.Contains(Path.GetExtension(selectedFile).ToLowerInvariant())
            };
        }

        private void ApplyMenuImage(Image target, string path)
        {
            if (_isPlayingVideo && _menuVideoPlayer != null)
            {
                _menuVideoPlayer.Stop();
                _isPlayingVideo = false;
            }

            var sp = LoadSpriteFromFile(path);
            if (sp != null)
            {
                target.sprite = sp;
                target.type = Image.Type.Simple;
                target.preserveAspect = true;
                target.enabled = true;
            }
        }

        private void ApplyMenuVideo(GameObject owner, string videoPath)
        {
            // Normalize target filename
            string targetFilename = !string.IsNullOrEmpty(videoPath) ? Path.GetFileName(videoPath).ToLower() : "";

            // Check if we already have a globally playing video player!
            if (_menuVideoPlayer != null)
            {
                string currentFilename = !string.IsNullOrEmpty(_menuVideoPlayer.url) ? Path.GetFileName(_menuVideoPlayer.url).ToLower().Replace("file://", "").Trim('/') : "";
                if (_isPlayingVideo && currentFilename == targetFilename)
                {
                    if (cfgDebugLogging.Value)
                        Logger.LogInfo("[MainMenuReplacer] Video is already playing globally on persistent canvas. Skipping duplicate player to remain perfectly seamless!");
                    
                    var newImg = owner.GetComponent<Image>();
                    if (newImg != null) newImg.enabled = false;
                    
                    return;
                }
                else
                {
                    if (cfgDebugLogging.Value)
                        Logger.LogInfo($"[MainMenuReplacer] Switching video from {currentFilename} to {targetFilename}. Reusing existing VideoPlayer.");
                    
                    // Re-enable and reuse the player!
                    if (_menuVideoAudioSource != null) _menuVideoAudioSource.enabled = true;
                    _menuVideoPlayer.enabled = true;
                    _menuVideoPlayer.Stop();
                    _menuVideoPlayer.url = videoPath;
                    _menuVideoPlayer.loopPointReached -= OnVideoEnd;
                    _menuVideoPlayer.loopPointReached += OnVideoEnd;
                    _menuVideoPlayer.Play();
                    _isPlayingVideo = true;
                    
                    var newImg = owner.GetComponent<Image>();
                    if (newImg != null) newImg.enabled = false;
                    return;
                }
            }

            // Fix vanilla oversized background for perfect fit
            var ownerRt = owner.GetComponent<RectTransform>();
            if (ownerRt != null)
            {
                ownerRt.offsetMin = Vector2.zero;
                ownerRt.offsetMax = Vector2.zero;
            }

            _currentMenuOwner = owner;

            var existing = owner.transform.Find("MMR_VideoRoot");
            GameObject root = existing != null ? existing.gameObject : new GameObject("MMR_VideoRoot");
            if (existing == null)
            {
                root.transform.SetParent(owner.transform, false);
                var rt = root.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
            }

            var raw = root.GetComponent<RawImage>() ?? root.AddComponent<RawImage>();
            var fitter = root.GetComponent<AspectRatioFitter>() ?? root.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1920f / 1080f;
            var audioSrc = root.GetComponent<AudioSource>() ?? root.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.loop = true;
            audioSrc.enabled = true;

            TryBindToGameMusicMixerGroup(audioSrc);

            var vp = root.GetComponent<VideoPlayer>() ?? root.AddComponent<VideoPlayer>();
            vp.enabled = true;
            vp.source = VideoSource.Url;
            vp.url = videoPath;
            vp.isLooping = false;
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.EnableAudioTrack(0, true);
            vp.SetTargetAudioSource(0, audioSrc);
            vp.loopPointReached -= OnVideoEnd;
            vp.loopPointReached += OnVideoEnd;

            var rtTex = raw.texture as RenderTexture;
            if (rtTex == null)
            {
                rtTex = new RenderTexture(1920, 1080, 0);
                raw.texture = rtTex;
            }
            vp.targetTexture = rtTex;

            var img = owner.GetComponent<Image>();
            if (img != null) img.enabled = false;

            _menuVideoAudioSource = audioSrc;
            _menuVideoPlayer = vp;
            _isPlayingVideo = true;

            root.SetActive(true);
            float vol = cfgMenuVideoMute.Value ? 0f : (ResolveMusicVolume01() * Mathf.Clamp(cfgMenuVideoVolumeMultiplier.Value, 0f, 2f));
            audioSrc.volume = vol;
            vp.Stop();
            vp.Play();
        }

        internal Sprite LoadSpriteFromFile(string path)
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(data)) return null;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch { return null; }
        }

        private static void Shuffle(List<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // LOADING SCREEN PATCHES
        [HarmonyPatch]
        private static class LoadingScreenPatches
        {
            private static readonly FieldInfo fiLoadingImage = AccessTools.Field(typeof(LoadingScreen), "loadingImage");
            private static readonly FieldInfo fiImagesList = AccessTools.Field(typeof(LoadingScreen), "images");

            [HarmonyPatch(typeof(LoadingScreen), "ShowLoadingScreen")]
            [HarmonyPrefix]
            private static void ShowLoadingScreen_Prefix(LoadingScreen __instance, ref Sprite imageOverride)
            {
                var plugin = Instance;
                if (plugin == null || !plugin.cfgLoadingNeverUseDefault.Value) return;

                var sprites = LoadCustomSprites(plugin);
                if (sprites == null || sprites.Count == 0) return;

                ForceGameImagePool(__instance, sprites, plugin);

                var next = ChooseNextSprite(plugin, sprites);
                if (next != null)
                    imageOverride = next;
            }

            [HarmonyPatch(typeof(LoadingScreen), "ShowLoadingScreen")]
            [HarmonyPostfix]
            private static void ShowLoadingScreen_Postfix(LoadingScreen __instance)
            {
                var plugin = Instance;
                if (plugin == null) return;

                var img = fiLoadingImage.GetValue(__instance) as Image;
                if (img == null) return;

                var rawGo = img.transform.Find("MMR_LoadingVideo");
                if (plugin._isPlayingVideo && plugin._menuVideoPlayer != null && plugin._menuVideoPlayer.targetTexture != null)
                {
                    GameObject go = rawGo != null ? rawGo.gameObject : new GameObject("MMR_LoadingVideo");
                    if (rawGo == null)
                    {
                        go.transform.SetParent(img.transform, false);
                        var rt = go.AddComponent<RectTransform>();
                        rt.anchorMin = new Vector2(0.5f, 0.5f);
                        rt.anchorMax = new Vector2(0.5f, 0.5f);
                        rt.pivot = new Vector2(0.5f, 0.5f);
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                    }
                    
                    var rawImg = go.GetComponent<RawImage>() ?? go.AddComponent<RawImage>();
                    var fitter = go.GetComponent<AspectRatioFitter>() ?? go.AddComponent<AspectRatioFitter>();
                    fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    fitter.aspectRatio = 1920f / 1080f;
                    rawImg.name = "background"; // So StreamToOtherCanvases picks it up!
                    rawImg.texture = plugin._menuVideoPlayer.targetTexture;
                    rawImg.enabled = true;
                    go.SetActive(true);
                    
                    img.enabled = false;
                }
                else
                {
                    if (rawGo != null) rawGo.gameObject.SetActive(false);

                    if (!plugin.cfgLoadingNeverUseDefault.Value) return;

                    var sprites = LoadCustomSprites(plugin);
                    if (sprites == null || sprites.Count == 0) return;

                    var next = ChooseNextSprite(plugin, sprites);
                    if (next == null) return;

                    img.enabled = true;
                    img.sprite = next;
                    img.preserveAspect = true;
                }
            }

            private static void ForceGameImagePool(LoadingScreen ls, List<Sprite> sprites, MainMenuReplacer plugin)
            {
                try
                {
                    var list = fiImagesList.GetValue(ls) as List<Sprite>;
                    if (list == null)
                    {
                        list = new List<Sprite>();
                        fiImagesList.SetValue(ls, list);
                    }
                    list.Clear();
                    list.AddRange(sprites);

                    if (plugin.cfgDebugLogging.Value)
                        plugin.Logger.LogInfo($"[MainMenuReplacer] Replaced LoadingScreen.images pool with {sprites.Count} custom sprites");
                }
                catch { /* ignore */ }
            }

            private static List<Sprite> LoadCustomSprites(MainMenuReplacer plugin)
            {
                try
                {
                    string folder = plugin.cfgLoadingFolder.Value;
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                        return null;

                    var files = Directory.GetFiles(folder)
                        .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (files.Count == 0) return null;

                    var sprites = new List<Sprite>(files.Count);
                    foreach (var f in files)
                    {
                        if (!SpriteCache.TryGetValue(f, out var sp) || sp == null)
                        {
                            sp = plugin.LoadSpriteFromFile(f);
                            SpriteCache[f] = sp;
                        }
                        if (sp != null) sprites.Add(sp);
                    }
                    return sprites;
                }
                catch { return null; }
            }

            private static Sprite ChooseNextSprite(MainMenuReplacer plugin, List<Sprite> sprites)
            {
                if (sprites == null || sprites.Count == 0) return null;
                if (!plugin.cfgLoadingRandomize.Value) return sprites[0];

                if (!plugin.cfgLoadingAvoidRepeats.Value || sprites.Count == 1)
                    return sprites[UnityEngine.Random.Range(0, sprites.Count)];

                lock (BagLock)
                {
                    string folder = plugin.cfgLoadingFolder.Value ?? "";
                    int sig = folder.GetHashCode() ^ sprites.Count;

                    if (_loadingBagFolder == null || !string.Equals(_loadingBagFolder, folder, StringComparison.OrdinalIgnoreCase) || _loadingBagSig != sig || _loadingBag.Count != sprites.Count)
                    {
                        _loadingBagFolder = folder;
                        _loadingBagSig = sig;
                        _loadingBagIndex = 0;
                        _loadingBag = Enumerable.Range(0, sprites.Count).Select(i => i.ToString()).ToList();
                        Shuffle(_loadingBag);
                    }

                    if (_loadingBagIndex >= _loadingBag.Count)
                    {
                        Shuffle(_loadingBag);
                        _loadingBagIndex = 0;
                    }

                    int idx = int.Parse(_loadingBag[_loadingBagIndex++]);
                    idx = Mathf.Clamp(idx, 0, sprites.Count - 1);
                    return sprites[idx];
                }
            }
        }
    }
}
