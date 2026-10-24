# Region File Fix (CrossOver) for 7 Days to Die

[Русская версия](README.ru.md)

A small mod for the **Windows build of 7 Days to Die running under CrossOver / Wine on macOS**. It fixes the save system failing with an endless stream of `FileNotFoundException` errors and the game freezing when you press **Esc**.

## Tested with

| Component | Version |
|---|---|
| 7 Days to Die | V 2.6 (b14), Windows build (Unity 2022.3.62f2) |
| CrossOver | 26.3 (26.3.0.39832), bottle template `win10_64` |
| macOS | 26.5.2 (Apple M1 Max) |
| Undead Legacy | 2.7.40 (+ zHaardvin_Russian_Fixes 2.7.40) - works together |
| Vanilla | works |

Other game versions may need a rebuild (see [Building](#building-the-dll)). On real Windows the mod does nothing: the extra check always agrees with the game there.

## Symptoms

- The console fills up with errors like this, repeating every couple of seconds:
  ```
  ERR ERROR: System.IO.FileNotFoundException: Could not find file
  "C:\users\crossover\AppData\Roaming\7DaysToDie\Saves\<World>\<Game>\Region\r.-1.1.7rg"
    at RegionFileSectorBased.Get (...)
    at RegionFileManager.DoSaveChunks ()
  ```
- The game freezes when you press **Esc** (opening the pause menu forces a save).
- The `Region` folder of the save stays empty, so world changes are not saved.

The same bug has been reported on the official forum: [Unable to save region file when playing on MacOS crossover](https://community.thefunpimps.com/threads/unable-to-save-region-file-when-playing-on-macos-crossover.47164/).

## Cause

The game decides whether to create or open a file like this:

```csharp
if (!SdFile.Exists(path)) { SdFile.Create(path); ... }        // create a new region file
else SdFile.Open(path, FileMode.Open, ...);                   // open the existing one
```

Under CrossOver, `File.Exists` in the game's Mono runtime returns **true for files that do not exist**. It happens with paths that mix `\` and `/` separators, which the game builds for region files and some other save files. Diagnostics from inside the game:

```
File.Exists=True  FileInfo.Exists=True  GetFileAttributesW=0xFFFFFFFF  lastError=0  | dir files: []
```

The Win32 API correctly reports that the file is missing (`INVALID_FILE_ATTRIBUTES`), but with error code `0` instead of `2` (file not found). The game then opens a file that isn't there instead of creating it, gets `FileNotFoundException`, and retries forever.

Besides `.7rg` region files, these were also affected in testing: `power.dat`, `vehicles.dat`, `drones.dat`, `turrets.dat`, `blockLimits.dat`.

## What the mod does

1. **Harmony postfix on `SdFile.Exists`** (the game's common "does the file exist" helper). When the game gets "exists", the mod asks `GetFileAttributesW` again. Only if the API says "not found" (error `0`, `2` or `3`) **and** the file really cannot be opened (`FileNotFoundException` / `DirectoryNotFoundException`) is the result changed to `false`, so the game creates the file itself. A file that exists is never reported as missing, so saves cannot be overwritten by mistake.
2. **Fallback prefix on `RegionFileSectorBased.Get`**: if a region file still cannot be opened, the mod creates it.

Each corrected case is written to the game log once, at most 50 lines:

```
WRN [RegionFileFix] SdFile.Exists ghost -> false: ...\Region/r.0.0.7rg (lastError=0)
```

## Installation

1. Download `RegionFileFix_<version>.zip` from [Releases](../../releases).
2. Unpack it into the game's `Mods` folder so that you get `7 Days To Die/Mods/1_RegionFileFix/ModInfo.xml` and `RegionFileFix.dll`.

   With Steam inside CrossOver the default path is:
   ```
   ~/Library/Application Support/CrossOver/Bottles/<bottle>/drive_c/Program Files (x86)/Steam/steamapps/common/7 Days To Die/Mods/
   ```
3. Requirements:
   - the stock `Mods/0_TFP_Harmony` folder (ships with the game, don't delete it);
   - launch **without EasyAntiCheat** (any DLL mod requires this).
4. Start the game. The log (`drive_c/users/crossover/AppData/Roaming/7DaysToDie/logs/output_log_client_*.txt`) should contain:
   ```
   [RegionFileFix] patched RegionFileSectorBased.Get, SdFile.Exists
   ```
   After playing for a minute, `.7rg` files should appear in the save's `Region` folder, and `Could not find file` errors should be gone.

**Removal:** delete the `Mods/1_RegionFileFix` folder.

### Note for Undead Legacy under CrossOver

Undead Legacy needs BepInEx, and under CrossOver BepInEx doesn't start by default. In the bottle, open **Wine Configuration -> Libraries**, add `winhttp`, and set it to **Native then Builtin** ("Сторонняя, затем встроенная" in the Russian UI). Otherwise UL fails to load and tries to read its textures from `Data/Bundles/Standalone/Mods/...`.

## Building the DLL

You don't need to build it yourself: a ready-made DLL is in Releases. Build it only if you want to check the code or adapt the mod to a different game version.

The compiler needs some DLLs from your installed copy of the game (they are not included in this repository):
- everything from `7 Days To Die/7DaysToDie_Data/Managed/` that the mod uses: `Assembly-CSharp.dll`, `LogLibrary.dll`, `mscorlib.dll` and others;
- `7 Days To Die/Mods/0_TFP_Harmony/0Harmony.dll`.

You can build with either option below. The output is `mod/1_RegionFileFix/RegionFileFix.dll`.

### Option A: .NET SDK (macOS or Windows)

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) 6.0 or newer (tested with 7.0.101). Check it in a terminal:
   ```
   dotnet --version
   ```
2. Download the repository: the **Code -> Download ZIP** button on GitHub, then unpack it, or run:
   ```
   git clone https://github.com/Kolobok12309/7dtd-crossover-region-fix.git
   cd 7dtd-crossover-region-fix
   ```
3. Build it, passing the path to the game folder in `GameDir`:

   macOS (CrossOver):
   ```bash
   dotnet build src/RegionFileFix.csproj -c Release \
     -p:GameDir="$HOME/Library/Application Support/CrossOver/Bottles/<bottle>/drive_c/Program Files (x86)/Steam/steamapps/common/7 Days To Die"
   ```
   Windows (PowerShell or cmd):
   ```
   dotnet build src\RegionFileFix.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
   ```
   On the first build `dotnet` downloads the `Microsoft.NETFramework.ReferenceAssemblies` package from NuGet, so you need internet. The build has succeeded when you see `Build succeeded` and `0 Error(s)`.

### Option B: Mono `mcs` (macOS / Linux)

1. Install Mono (tested with Mono 6.12): `brew install mono` (via [Homebrew](https://brew.sh)) or the installer from [mono-project.com](https://www.mono-project.com/download/stable/).
2. From the repository folder, run:
   ```bash
   ./build.sh "$HOME/Library/Application Support/CrossOver/Bottles/<bottle>/drive_c/Program Files (x86)/Steam/steamapps/common/7 Days To Die"
   ```
   The script prints `Built: .../mod/1_RegionFileFix/RegionFileFix.dll`.

### After building

Copy the whole `mod/1_RegionFileFix` folder (`ModInfo.xml` + `RegionFileFix.dll`) into the game's `Mods` folder.

If the game version changed and the build fails with errors about `SdFile`, `RegionFileSectorBased` or `RegionFile`, the developers changed those classes. The patch in `src/RegionFileFix.cs` then needs to be adapted.

## Repository layout

```
src/RegionFileFix.cs        - mod source (Harmony patches)
src/RegionFileFix.csproj    - project for dotnet build
build.sh                    - build with Mono mcs
mod/1_RegionFileFix/        - mod folder (ModInfo.xml; the DLL appears here after building)
```
