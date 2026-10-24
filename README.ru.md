# Region File Fix (CrossOver) для 7 Days to Die

[English version](README.md)

Небольшой мод для **Windows-версии 7 Days to Die, запущенной через CrossOver / Wine на macOS**. Он исправляет сохранение, которое падает с бесконечными ошибками `FileNotFoundException`, и зависание игры по нажатию **Esc**.

## Проверено на

| Компонент | Версия |
|---|---|
| 7 Days to Die | V 2.6 (b14), Windows-сборка (Unity 2022.3.62f2) |
| CrossOver | 26.3 (26.3.0.39832), шаблон бутылки `win10_64` |
| macOS | 26.5.2 (Apple M1 Max) |
| Undead Legacy | 2.7.40 (+ zHaardvin_Russian_Fixes 2.7.40), работают вместе |
| Ванильная игра | работает |

На других версиях игры мод, возможно, придётся пересобрать (см. [Сборка DLL](#сборка-dll)). На настоящей Windows мод ничего не меняет: там дополнительная проверка всегда согласна с игрой.

## Симптомы

- В консоли каждые пару секунд повторяется ошибка:
  ```
  ERR ERROR: System.IO.FileNotFoundException: Could not find file
  "C:\users\crossover\AppData\Roaming\7DaysToDie\Saves\<Мир>\<Игра>\Region\r.-1.1.7rg"
    at RegionFileSectorBased.Get (...)
    at RegionFileManager.DoSaveChunks ()
  ```
- По **Esc** игра замирает: меню паузы запускает сохранение.
- Папка `Region` в сейве остаётся пустой, изменения мира не сохраняются.

Эту же ошибку сообщали на официальном форуме: [Unable to save region file when playing on MacOS crossover](https://community.thefunpimps.com/threads/unable-to-save-region-file-when-playing-on-macos-crossover.47164/).

## Причина

Игра решает, создать файл или открыть существующий, так:

```csharp
if (!SdFile.Exists(path)) { SdFile.Create(path); ... }        // создать новый файл региона
else SdFile.Open(path, FileMode.Open, ...);                   // открыть существующий
```

Под CrossOver `File.Exists` в Mono игры возвращает **true для файлов, которых нет**. Это происходит на путях со смешанными разделителями `\` и `/`. Такие пути игра строит для файлов регионов и некоторых других файлов сохранения. Диагностика изнутри игры:

```
File.Exists=True  FileInfo.Exists=True  GetFileAttributesW=0xFFFFFFFF  lastError=0  | dir files: []
```

Win32 API правильно сообщает, что файла нет (`INVALID_FILE_ATTRIBUTES`), но с кодом ошибки `0` вместо `2` («файл не найден»). В итоге игра вместо создания файла пытается открыть несуществующий, получает `FileNotFoundException` и повторяет попытку бесконечно.

Кроме файлов регионов `.7rg`, при тестах были затронуты `power.dat`, `vehicles.dat`, `drones.dat`, `turrets.dat` и `blockLimits.dat`.

## Что делает мод

1. **Harmony-постфикс на `SdFile.Exists`**, общий метод игры для проверки «есть ли файл». Когда игра получает ответ «есть», мод переспрашивает `GetFileAttributesW`. Ответ меняется на `false`, только если API говорит «нет» (код `0`, `2` или `3`) **и** файл действительно не открывается (`FileNotFoundException` / `DirectoryNotFoundException`). Тогда игра сама создаёт файл. Существующий файл никогда не объявляется отсутствующим, поэтому сохранения не перезапишутся по ошибке.
2. **Запасной префикс на `RegionFileSectorBased.Get`**: если файл региона всё равно не открывается, мод создаёт его.

Каждый исправленный случай один раз пишется в лог игры, максимум 50 строк:

```
WRN [RegionFileFix] SdFile.Exists ghost -> false: ...\Region/r.0.0.7rg (lastError=0)
```

## Установка

1. Скачайте `RegionFileFix_<версия>.zip` из [Releases](../../releases).
2. Распакуйте архив в папку `Mods` игры, чтобы получилось `7 Days To Die/Mods/1_RegionFileFix/ModInfo.xml` и `RegionFileFix.dll`.

   Если Steam стоит внутри CrossOver, путь по умолчанию такой:
   ```
   ~/Library/Application Support/CrossOver/Bottles/<бутылка>/drive_c/Program Files (x86)/Steam/steamapps/common/7 Days To Die/Mods/
   ```
3. Что нужно для работы:
   - штатная папка `Mods/0_TFP_Harmony` (идёт вместе с игрой, не удаляйте её);
   - запуск **без EasyAntiCheat** (это требование любого DLL-мода).
4. Запустите игру. В логе (`drive_c/users/crossover/AppData/Roaming/7DaysToDie/logs/output_log_client_*.txt`) должна появиться строка:
   ```
   [RegionFileFix] patched RegionFileSectorBased.Get, SdFile.Exists
   ```
   Поиграйте минуту. В папке `Region` сейва должны появиться файлы `.7rg`, а ошибки `Could not find file` должны пропасть.

**Удаление:** удалите папку `Mods/1_RegionFileFix`.

### Если ставите Undead Legacy под CrossOver

Undead Legacy нужен BepInEx, а под CrossOver он по умолчанию не запускается. В бутылке откройте **Wine Configuration -> Библиотеки**, добавьте `winhttp` и выберите **«Сторонняя, затем встроенная»** (Native then Builtin). Без этого UL не загрузится и будет искать свои текстуры в `Data/Bundles/Standalone/Mods/...`.

## Сборка DLL

Собирать самому не обязательно: готовая DLL лежит в Releases. Сборка нужна, только если хотите проверить код или адаптировать мод под другую версию игры.

Компилятору нужны DLL из вашей установленной игры (в этом репозитории их нет):
- файлы из `7 Days To Die/7DaysToDie_Data/Managed/`, которые использует мод: `Assembly-CSharp.dll`, `LogLibrary.dll`, `mscorlib.dll` и другие;
- `7 Days To Die/Mods/0_TFP_Harmony/0Harmony.dll`.

Собрать можно любым из двух способов ниже. Результат: `mod/1_RegionFileFix/RegionFileFix.dll`.

### Способ A: .NET SDK (macOS или Windows)

1. Установите [.NET SDK](https://dotnet.microsoft.com/download) версии 6.0 или новее (проверено на 7.0.101). Проверьте в терминале:
   ```
   dotnet --version
   ```
2. Скачайте репозиторий: кнопка **Code -> Download ZIP** на GitHub, затем распакуйте, или выполните:
   ```
   git clone https://github.com/Kolobok12309/7dtd-crossover-region-fix.git
   cd 7dtd-crossover-region-fix
   ```
3. Соберите, передав путь к папке игры в `GameDir`:

   macOS (CrossOver):
   ```bash
   dotnet build src/RegionFileFix.csproj -c Release \
     -p:GameDir="$HOME/Library/Application Support/CrossOver/Bottles/<бутылка>/drive_c/Program Files (x86)/Steam/steamapps/common/7 Days To Die"
   ```
   Windows (PowerShell или cmd):
   ```
   dotnet build src\RegionFileFix.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
   ```
   При первой сборке `dotnet` скачивает из NuGet пакет `Microsoft.NETFramework.ReferenceAssemblies`, поэтому нужен интернет. Сборка прошла, если в конце видно `Сборка успешно завершена` / `Build succeeded` и `Ошибок: 0` / `0 Error(s)`.

### Способ B: Mono `mcs` (macOS / Linux)

1. Установите Mono (проверено на Mono 6.12): `brew install mono` (через [Homebrew](https://brew.sh)) или установщиком с [mono-project.com](https://www.mono-project.com/download/stable/).
2. В папке репозитория выполните:
   ```bash
   ./build.sh "$HOME/Library/Application Support/CrossOver/Bottles/<бутылка>/drive_c/Program Files (x86)/Steam/steamapps/common/7 Days To Die"
   ```
   Скрипт выведет `Built: .../mod/1_RegionFileFix/RegionFileFix.dll`.

### После сборки

Скопируйте папку `mod/1_RegionFileFix` целиком (`ModInfo.xml` + `RegionFileFix.dll`) в папку `Mods` игры.

Если после обновления игры сборка падает с ошибками про `SdFile`, `RegionFileSectorBased` или `RegionFile`, значит, разработчики изменили эти классы. Тогда патч в `src/RegionFileFix.cs` нужно адаптировать.

## Структура репозитория

```
src/RegionFileFix.cs        - исходник мода (Harmony-патчи)
src/RegionFileFix.csproj    - проект для dotnet build
build.sh                    - сборка через Mono mcs
mod/1_RegionFileFix/        - папка мода (ModInfo.xml; сюда же попадает DLL после сборки)
```
