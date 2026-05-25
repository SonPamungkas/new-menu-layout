# MainMenu Replacer (Nuclear Option) — Menu Video + Custom Loading Screens

A simple BepInEx plugin for **Nuclear Option** that lets you:
- Replace the **Main Menu background** with a **video** or **image**
- Replace the **Loading Screen backgrounds** with your own images (and optionally **disable default loading screens entirely**)

---

## Requirements
- **BepInEx 5** installed for Nuclear Option

---

## Install
1. Download the latest release ZIP.
2. Open your Nuclear Option install folder (default):
   `C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\`
3. Drag the ZIP contents into your game folder so it merges into:
   `...\Nuclear Option\BepInEx\plugins\`

After install, you should have:
- `...\Nuclear Option\BepInEx\plugins\MainMenuReplacer\MainMenuReplacer.dll`

---

## Main Menu Background (Video or Image)

Put **one** of the following files in:
`...\Nuclear Option\BepInEx\plugins\`

### Menu Video (preferred)
- `custom_menu.mp4` *(or `.webm`)*

### Menu Image (fallback)
- `custom_menu.png` *(or `.jpg`)*

 If `custom_menu.mp4` exists, the mod uses the **video**. Otherwise it uses the **image**.

---

## Loading Screen Images

Put your loading images here:
`...\Nuclear Option\BepInEx\plugins\LoadingScreens\`

Supported formats:
- `.png`
- `.jpg`
- `.jpeg`

 The mod will select from your images instead of the game’s defaults.

---

## Config (Optional)
After you run the game once, the config file is created here:
`...\Nuclear Option\BepInEx\config\com.yourname.mainmenureplacer.cfg`

Recommended settings (defaults):
- `NeverUseDefaultImages = true`  
  Prevents the game’s default loading images from appearing (as long as you have at least 1 valid image in `LoadingScreens`).
- `Randomize = true`  
  Chooses different loading images.
- `AvoidRepeats = true`  
  Cycles through all images before repeating.

---

## Troubleshooting
### Nothing changes
- Confirm the plugin DLL exists at:  
  `...\BepInEx\plugins\MainMenuReplacer\MainMenuReplacer.dll`

### Loading screens don’t change
- Confirm your images are in:  
  `...\BepInEx\plugins\LoadingScreens\`
- Confirm formats are `.png/.jpg/.jpeg`

### Check logs
- `...\Nuclear Option\BepInEx\LogOutput.log`

Look for lines containing `MainMenu Replacer`.

---

## Notes
- This mod replaces the loading image pool when `NeverUseDefaultImages = true`, so default loading images cannot appear.
- Menu video audio is supported and intended to follow the game’s music volume (depending on how your build is configured).

---
