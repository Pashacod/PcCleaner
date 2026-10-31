# PcCleaner v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Утилита для Windows, которая сканирует мусор по категориям (Temp, корзина, кэши браузеров, кэши разработки), удаляет выбранное и находит большие файлы.

**Architecture:** `Cleaner.Core` — вся логика: `PathGuard` (запрещённые пути) → `FileDeleter` (единственное место, где удаляются файлы) → источники `IJunkSource`. `Cleaner.App` — WPF + WPF-UI, MVVM; знает только `IJunkSource` и `BigFilesScanner`. Все источники принимают корневые пути через конструктор, чтобы тесты работали на временных папках.

**Tech Stack:** .NET 8, C#, WPF, WPF-UI 3.x (`WPF-UI` в NuGet), CommunityToolkit.Mvvm 8.x, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-31-pc-cleaner-design.md`

## Global Constraints

- Ничего не ставить на C: .NET SDK — в `D:\Program Files\dotnet`.
- `net8.0` для Core и тестов, `net8.0-windows` + `UseWPF` для App.
- Весь текст интерфейса и причин пропуска — на русском.
- Удаление файлов — только через `FileDeleter`; каждое удаление проверяется `PathGuard`.
- Браузеры: только папки кэша; пароли, история, cookies, закладки не трогаются.
- Манифест App: `requestedExecutionLevel level="requireAdministrator"`.
- Публикация: self-contained, single-file, win-x64.
- `settings.json` и `logs\yyyy-MM-dd.log` — рядом с .exe.

## Review Focus

1. **Junction/симлинк внутри удаляемой папки** (в `%TEMP%` такие бывают) — удаляется сама ссылка, содержимое цели остаётся. Тест в Task 2.
2. **Файл с атрибутом «только чтение»** — снимаем атрибут и удаляем, а не пропускаем. Тест в Task 2.
3. **Файл исчез между сканированием и очисткой** — не ошибка, не исключение, не в Skipped. Тест в Task 2.
4. **Папки источника не существует** (нет Firefox, нет `D:\Work`, нет npm-cache) — пустой результат, без исключения. Тесты в Task 3, 4, 5.
5. **Занятый файл внутри папки** — остальная папка удаляется, занятый в Skipped, сама папка остаётся. Тест в Task 2.

---

### Task 1: Установка SDK, решение, PathGuard

**Files:**
- Create: `PcCleaner.sln`, `src/Cleaner.Core/Cleaner.Core.csproj`, `tests/Cleaner.Core.Tests/Cleaner.Core.Tests.csproj`, `.gitignore` (`dotnet new gitignore`)
- Create: `src/Cleaner.Core/Safety/PathGuard.cs`
- Test: `tests/Cleaner.Core.Tests/PathGuardTests.cs`

**Interfaces:**
- Produces:
  - `record PathGuardRoots(string WindowsDir, IReadOnlyList<string> ProgramDirs, string UserProfile, IReadOnlyList<string> ProtectedProfileDirs)` + `static PathGuardRoots FromSystem()` (ProgramDirs: `C:\Program Files`, `C:\Program Files (x86)`, `D:\Program Files`, `C:\ProgramData`; ProtectedProfileDirs: Documents, Desktop, Pictures, Videos, Music через `Environment.GetFolderPath`)
  - `class PathGuard(PathGuardRoots roots)` с `bool IsAllowed(string path, bool manualSelection = false)`

- [ ] **Step 1: Поставить .NET 8 SDK**

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir "D:\Program Files\dotnet"
[Environment]::SetEnvironmentVariable("DOTNET_ROOT","D:\Program Files\dotnet","User")
# дописать D:\Program Files\dotnet в пользовательский PATH
```
Проверка: `& "D:\Program Files\dotnet\dotnet.exe" --list-sdks` → строка `8.0.x`.

- [ ] **Step 2: Создать решение** — `dotnet new sln`, `classlib` → `src/Cleaner.Core`, `xunit` → `tests/Cleaner.Core.Tests`, ссылка тестов на Core, `dotnet new gitignore`. `dotnet test` → 1 тест из шаблона проходит; удалить шаблонный тест.

- [ ] **Step 3: Падающие тесты PathGuard** (roots: `C:\Windows`, `[C:\Program Files, C:\Program Files (x86), D:\Program Files, C:\ProgramData]`, `C:\Users\u`, `[C:\Users\u\Documents, C:\Users\u\Desktop]`)

```csharp
[Theory]
[InlineData(@"C:\")] [InlineData(@"D:\")]
[InlineData(@"C:\Windows")] [InlineData(@"C:\Windows\System32\x.dll")]
[InlineData(@"C:\Program Files\App")] [InlineData(@"D:\Program Files\dotnet")]
[InlineData(@"C:\ProgramData\x")]
[InlineData(@"C:\Users\u")] [InlineData(@"C:\Users\u\Documents")] [InlineData(@"C:\Users\u\Desktop\a.txt")]
[InlineData(@"D:\Work\Proj\.git")] [InlineData(@"D:\Work\Proj\.git\objects\a")]
[InlineData(@"C:\Windows\Temp\..\System32")] [InlineData(@"c:\windows\system32")]
public void Blocked(string p) => Assert.False(guard.IsAllowed(p));

[Theory]
[InlineData(@"C:\Windows\Temp\a.tmp")] [InlineData(@"C:\Users\u\AppData\Local\Temp\x")]
[InlineData(@"D:\Work\Proj\build")]
public void Allowed(string p) => Assert.True(guard.IsAllowed(p));

[Fact] public void WindowsTempItselfBlocked() => Assert.False(guard.IsAllowed(@"C:\Windows\Temp"));
[Fact] public void ManualAllowsProfileFiles() => Assert.True(guard.IsAllowed(@"C:\Users\u\Documents\big.iso", manualSelection: true));
[Fact] public void ManualStillBlocksSystem() => Assert.False(guard.IsAllowed(@"C:\Windows\big.iso", manualSelection: true));
[Fact] public void ManualStillBlocksProfileRoot() => Assert.False(guard.IsAllowed(@"C:\Users\u\Documents", manualSelection: true));
```

- [ ] **Step 4: `dotnet test`** → FAIL (PathGuard не определён).
- [ ] **Step 5: Реализовать PathGuard** — нормализация `Path.GetFullPath` + `TrimEnd('\\')`, сравнение `OrdinalIgnoreCase`, проверка «путь равен корню или начинается с `корень\`». `manualSelection` снимает только запрет на содержимое профиля/ProtectedProfileDirs (сами папки — всё равно запрещены).
- [ ] **Step 6: `dotnet test`** → все PASS.
- [ ] **Step 7: Commit** `feat: решение и PathGuard`

---

### Task 2: Размер папок и FileDeleter

**Files:**
- Create: `src/Cleaner.Core/Model.cs` (records из спецификации), `src/Cleaner.Core/IO/SizeCalculator.cs`, `src/Cleaner.Core/IO/FileDeleter.cs`, `src/Cleaner.Core/Logging/FileLog.cs`
- Test: `tests/Cleaner.Core.Tests/SizeCalculatorTests.cs`, `tests/Cleaner.Core.Tests/FileDeleterTests.cs`, `tests/Cleaner.Core.Tests/TempDir.cs` (хелпер: временная папка + `File(rel, bytes)` + `Dispose` удаляет)

**Interfaces:**
- Consumes: `PathGuard.IsAllowed`
- Produces:
  - `record JunkItem(string Path, long SizeBytes, bool IsDirectory, bool Selected)`
  - `record ScanResult(string SourceId, IReadOnlyList<JunkItem> Items, long TotalBytes, string? Blocker = null)` — `Blocker` (отклонение от спецификации): текст «Закройте Chrome…», если очистка сейчас невозможна
  - `record CleanResult(long FreedBytes, int Deleted, IReadOnlyList<SkippedItem> Skipped)` + `static CleanResult Empty` + `CleanResult Merge(CleanResult other)`
  - `record SkippedItem(string Path, string Reason)`
  - `static long SizeCalculator.GetSize(string path, CancellationToken ct)` — файл или папка рекурсивно; reparse points не обходит (их размер 0); ошибки доступа пропускает
  - `interface ILog { void Write(string line); }`, `class FileLog(string dir) : ILog` (строка с временем в `dir\yyyy-MM-dd.log`), `class NullLog : ILog`
  - `class FileDeleter(PathGuard guard, ILog log)`:
    - `CleanResult DeletePermanently(IEnumerable<JunkItem> items, CancellationToken ct)` — только `Selected`
    - `CleanResult SendToRecycleBin(IEnumerable<JunkItem> items, bool manualSelection, CancellationToken ct)`
  - Причины пропуска (точные строки): `"занят другой программой"` (IOException), `"нет доступа"` (UnauthorizedAccessException), `"запрещённый путь"`

- [ ] **Step 1: Падающие тесты SizeCalculator** — `SumsNestedFiles` (файлы 10+20+30 байт в двух уровнях → 60), `EmptyDirIsZero`, `MissingPathIsZero`, `JunctionNotFollowed` (junction через `cmd /c mklink /J link target`, target с файлом 100 байт → размер папки с link = 0).
- [ ] **Step 2: Падающие тесты FileDeleter** (guard из `PathGuardRoots` с фейковыми путями, не задевающими TempDir):
  - `DeletesSelectedOnly` — 2 файла, один `Selected=false` → удалён 1, `FreedBytes` = его размер
  - `DeletesDirectoryRecursively` — папка с вложенными файлами исчезла, `Deleted` = 1
  - `LockedFileSkipped` — файл открыт `FileShare.None` → `Skipped[0].Reason == "занят другой программой"`, остальные удалены
  - `LockedFileInsideDirKeepsRest` — в папке 3 файла, один занят → 2 удалены, папка и занятый остались, `FreedBytes` = размер двух
  - `ReadOnlyFileDeleted` — `FileAttributes.ReadOnly` → удалён
  - `VanishedFileIsNotError` — путь не существует → `Deleted == 0`, `Skipped` пуст, без исключения
  - `JunctionDeletedTargetKept` — в удаляемой папке junction на внешнюю папку с файлом → удаляемая папка исчезла, файл в цели на месте
  - `BlockedPathSkipped` — guard запрещает → `"запрещённый путь"`, файл на месте, в лог записана строка с путём
  - `CancelStops` — токен отменён до вызова → ничего не удалено
- [ ] **Step 3: `dotnet test`** → FAIL.
- [ ] **Step 4: Реализовать** — обход папки снизу вверх; для каталога с `FileAttributes.ReparsePoint` — `Directory.Delete(path, false)` (удаляет ссылку); перед удалением файла сбрасывать ReadOnly; `FreedBytes` считается по фактически удалённым файлам. `SendToRecycleBin` — P/Invoke `SHFileOperation` (shell32, `FO_DELETE`, флаги `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI`, путь с двойным `\0` в конце) в `IO/RecycleBin.cs`; ненулевой код возврата → Skipped с причиной `"не удалось переместить в корзину"`. На этот метод — один тест `RecycleRemovesFile` (файл исчез с места).
- [ ] **Step 5: `dotnet test`** → все PASS.
- [ ] **Step 6: Commit** `feat: SizeCalculator и FileDeleter`

---

### Task 3: IJunkSource и папочные источники

**Files:**
- Create: `src/Cleaner.Core/IJunkSource.cs`, `src/Cleaner.Core/Sources/FolderContentsSource.cs`, `src/Cleaner.Core/Sources/SourceCatalog.cs`
- Test: `tests/Cleaner.Core.Tests/FolderContentsSourceTests.cs`

**Interfaces:**
- Consumes: `FileDeleter`, `SizeCalculator`, records из Task 2
- Produces:
  - `interface IJunkSource` — ровно как в спецификации (`Id, Title, Category, Warning, CheckedByDefault, ScanAsync(IProgress<long>, CancellationToken), CleanAsync(ScanResult, CancellationToken)`)
  - `class FolderContentsSource(string id, string title, string category, string folder, FileDeleter deleter, bool checkedByDefault = true, string? warning = null)` — элементы = прямые потомки `folder`, каждый со своим размером; `Selected = CheckedByDefault`; очистка — `DeletePermanently`
  - `static class Categories { const string System = "Система", Browsers = "Браузеры", Dev = "Разработка"; }`
  - `static IReadOnlyList<IJunkSource> SourceCatalog.CreateDefault(FileDeleter deleter, AppSettings settings)` — собирает все источники v1 (дополняется в Task 4–6; `AppSettings` из Task 7 — до него параметр `IReadOnlyList<string> projectRoots`)

- [ ] **Step 1: Падающие тесты** — `ItemsAreTopLevelEntries` (папка: 2 файла + подпапка с 1 файлом → 3 элемента, у подпапки размер вложенного), `TotalIsSum`, `MissingFolderGivesEmpty` (без исключения), `CleanDeletesSelectedAndReportsFreed`, `ProgressReportsBytes` (последнее значение прогресса == TotalBytes).
- [ ] **Step 2: `dotnet test`** → FAIL.
- [ ] **Step 3: Реализовать** FolderContentsSource; сканирование в `Task.Run`.
- [ ] **Step 4: Добавить в SourceCatalog** — `user-temp` («Временные файлы», `Path.GetTempPath()`), `windows-temp` («Временные файлы Windows», `%WINDIR%\Temp`), `npm-cache` («Кэш npm», `%LOCALAPPDATA%\npm-cache`), `gradle-cache` («Кэш Gradle», `%USERPROFILE%\.gradle\caches`, `checkedByDefault: false`, warning `"Следующая сборка Android-проектов будет дольше"`).
- [ ] **Step 5: `dotnet test`** → PASS.
- [ ] **Step 6: Commit** `feat: IJunkSource и папочные источники`

---

### Task 4: Кэши браузеров

**Files:**
- Create: `src/Cleaner.Core/Sources/BrowserCacheSource.cs`
- Test: `tests/Cleaner.Core.Tests/BrowserCacheSourceTests.cs`

**Interfaces:**
- Consumes: `IJunkSource`, `FileDeleter`, `SizeCalculator`
- Produces:
  - `class BrowserCacheSource(string id, string title, string browserName, Func<IEnumerable<string>> findCacheDirs, Func<bool> isRunning, FileDeleter deleter)`
  - `static IEnumerable<string> BrowserCacheSource.ChromiumCacheDirs(string userDataDir)` — для каждого профиля (`Default`, `Profile *`) подпапки `Cache`, `Code Cache`, `GPUCache`, если существуют
  - `static IEnumerable<string> BrowserCacheSource.FirefoxCacheDirs(string profilesDir)` — `<profile>\cache2`
  - Если `isRunning()` при сканировании → `ScanResult.Blocker = $"Закройте {browserName} и нажмите «Повторить»"`; `CleanAsync` при запущенном браузере возвращает `CleanResult.Empty`

- [ ] **Step 1: Падающие тесты** на подставном дереве:
  - `ChromiumFindsOnlyCacheDirs` — `User Data\Default\{Cache,Code Cache,GPUCache,Login Data,History}`, `User Data\Profile 1\Cache`, `User Data\System Profile\Cache` → ровно 4 папки; `Login Data` и `History` не найдены
  - `FirefoxFindsCache2` — `Profiles\a.default\{cache2,places.sqlite}` → только `cache2`
  - `MissingBrowserGivesEmpty` — userDataDir не существует → 0 элементов, без исключения
  - `RunningBrowserSetsBlocker` — `isRunning = () => true` → `Blocker` содержит имя браузера
  - `CleanEmptiesCacheDirsOnly` — после очистки `Cache` пуст или удалён, `History` на месте
- [ ] **Step 2: `dotnet test`** → FAIL.
- [ ] **Step 3: Реализовать**; `isRunning` по умолчанию в каталоге — `Process.GetProcessesByName(name).Length > 0`.
- [ ] **Step 4: В SourceCatalog** — `chrome-cache` (`%LOCALAPPDATA%\Google\Chrome\User Data`, процесс `chrome`), `edge-cache` (`%LOCALAPPDATA%\Microsoft\Edge\User Data`, `msedge`), `firefox-cache` (`%LOCALAPPDATA%\Mozilla\Firefox\Profiles`, `firefox`).
- [ ] **Step 5: `dotnet test`** → PASS.
- [ ] **Step 6: Commit** `feat: кэши браузеров`

---

### Task 5: Сборки проектов

**Files:**
- Create: `src/Cleaner.Core/Sources/ProjectBuildsSource.cs`
- Test: `tests/Cleaner.Core.Tests/ProjectBuildsSourceTests.cs`

**Interfaces:**
- Consumes: `IJunkSource`, `FileDeleter`, `SizeCalculator`
- Produces: `class ProjectBuildsSource(IReadOnlyList<string> roots, FileDeleter deleter)`, Id `project-builds`, Title «Сборки проектов», категория Dev

Правила (из спецификации): мусор-кандидаты и их признаки рядом —

| Папка | Признак в той же родительской папке | Selected |
|---|---|---|
| `build` | `build.gradle*`, `settings.gradle*`, `package.json` | true |
| `.gradle` | `build.gradle*`, `settings.gradle*` | true |
| `bin`, `obj` | `*.csproj` | true |
| `node_modules` | `package.json` | **false** |

Обход: внутрь найденного мусора, `node_modules`, `.git` и reparse points не заходим.

- [ ] **Step 1: Падающие тесты** — `GradleBuildFound`, `BuildWithoutMarkerIgnored`, `CsprojBinObjFound`, `NodeModulesFoundUnchecked` (`Selected == false`), `NestedNodeModulesNotReported` (внутри `node_modules\x\node_modules` — найден только внешний), `GitDirSkipped` (`build` внутри `.git` не найден), `MissingRootGivesEmpty`, `MultipleRoots`.
- [ ] **Step 2: `dotnet test`** → FAIL.
- [ ] **Step 3: Реализовать**; добавить в SourceCatalog.
- [ ] **Step 4: `dotnet test`** → PASS.
- [ ] **Step 5: Commit** `feat: сборки проектов`

---

### Task 6: Корзина и большие файлы

**Files:**
- Create: `src/Cleaner.Core/Sources/RecycleBinSource.cs`, `src/Cleaner.Core/BigFiles/BigFilesScanner.cs`
- Test: `tests/Cleaner.Core.Tests/BigFilesScannerTests.cs`

**Interfaces:**
- Consumes: `IJunkSource`, `FileDeleter.SendToRecycleBin(..., manualSelection: true, ...)`
- Produces:
  - `class RecycleBinSource : IJunkSource` — Id `recycle-bin`, «Корзина», категория System. Сканирование: `SHQueryRecycleBin(null, ref SHQUERYRBINFO)` (P/Invoke shell32) → один `JunkItem("Корзина", size, true, true)`, если size > 0. Очистка: `SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND)`; `FreedBytes` = размер из сканирования. Без юнит-тестов (системный вызов) — ручная проверка в Task 9.
  - `record BigFile(string Path, long SizeBytes, DateTime LastWrite)`
  - `static Task<IReadOnlyList<BigFile>> BigFilesScanner.ScanAsync(string root, long minBytes, int top, IProgress<int>? filesSeen, CancellationToken ct)` — по убыванию размера; недоступные папки пропускает; reparse points не обходит; `C:\Windows`, `$Recycle.Bin`, `System Volume Information` не обходит (их файлы удалять всё равно нельзя)

- [ ] **Step 1: Падающие тесты BigFilesScanner** — `ReturnsFilesAboveThresholdSortedDesc` (файлы 5, 50, 30, 1 байт, minBytes 10 → [50, 30]), `RespectsTop` (top 1 → [50]), `MissingRootGivesEmpty`, `CancelThrowsOperationCanceled`.
- [ ] **Step 2: `dotnet test`** → FAIL.
- [ ] **Step 3: Реализовать** оба класса; RecycleBinSource добавить в SourceCatalog.
- [ ] **Step 4: `dotnet test`** → PASS.
- [ ] **Step 5: Commit** `feat: корзина и большие файлы`

---

### Task 7: Настройки

**Files:**
- Create: `src/Cleaner.Core/Settings/AppSettings.cs`
- Modify: `src/Cleaner.Core/Sources/SourceCatalog.cs` (принимает `AppSettings`)
- Test: `tests/Cleaner.Core.Tests/AppSettingsTests.cs`

**Interfaces:**
- Produces: `record AppSettings(IReadOnlyList<string> ProjectRoots, long BigFileMinBytes, int BigFilesTop)` с `static AppSettings Default` = (`[@"D:\Work"]`, `500L * 1024 * 1024`, `50`); `static AppSettings Load(string path)`, `void Save(string path)` — System.Text.Json, с отступами

- [ ] **Step 1: Падающие тесты** — `MissingFileGivesDefault`, `RoundTrip`, `CorruptFileGivesDefault` (мусор в файле → Default, без исключения).
- [ ] **Step 2: `dotnet test`** → FAIL.
- [ ] **Step 3: Реализовать**; SourceCatalog берёт `ProjectRoots` из настроек.
- [ ] **Step 4: `dotnet test`** → PASS.
- [ ] **Step 5: Commit** `feat: настройки`

---

### Task 8: Окно и вкладка «Очистка»

**Files:**
- Create: `src/Cleaner.App/Cleaner.App.csproj` (`net8.0-windows`, `UseWPF`, `WPF-UI`, `CommunityToolkit.Mvvm`, ссылка на Core), `App.xaml(.cs)`, `MainWindow.xaml(.cs)` (`ui:FluentWindow`, Mica, тема по системе, две вкладки), `Views/CleanupView.xaml`, `ViewModels/CleanupViewModel.cs`, `ViewModels/CategoryViewModel.cs`, `ViewModels/SourceViewModel.cs`, `ViewModels/DriveViewModel.cs`, `app.manifest`

**Interfaces:**
- Consumes: `SourceCatalog.CreateDefault`, `IJunkSource`, `ScanResult.Blocker`, `CleanResult`, `AppSettings.Load`, `FileLog`, `PathGuardRoots.FromSystem`
- Produces (для Task 9): `App.Services` — статические готовые `FileDeleter Deleter`, `AppSettings Settings`, `ILog Log`

Поведение (тексты дословно из спецификации):
- `DriveViewModel` — «C: свободно X из Y ГБ» + полоска (`DriveInfo`), обновляется после очистки.
- `CleanupViewModel`: `ScanCommand` (все источники параллельно, `Task.WhenAll`; ошибка одного источника → его `SourceViewModel.Error`), `CancelCommand`, `CleanCommand` (подтверждение `ui:MessageBox` «Будет удалено насовсем N ГБ. Продолжить?»), `SelectedBytes`, итог «Освобождено N ГБ» + свёрнутый список Skipped.
- `CategoryViewModel` — заголовок, сумма, галочка (три состояния от источников), раскрывается до источников; источник раскрывается до элементов. Warning источника — серым текстом под названием. Blocker — жёлтая плашка с «Повторить» (пересканировать источник) / «Пропустить» (снять галочку).
- Размеры — хелпер `Format.Bytes(long)` → «1,2 ГБ» / «350 МБ» / «12 КБ».
- `app.manifest` с `requireAdministrator`, подключён `<ApplicationManifest>`.

- [ ] **Step 1: Создать проект App** и добавить в решение; `dotnet build` → без ошибок.
- [ ] **Step 2: Окно с карточкой диска и кнопкой** — запуск (`dotnet run --project src/Cleaner.App`) → UAC, окно в стиле Windows 11, верные цифры диска.
- [ ] **Step 3: Сканирование** — карточки категорий с размерами и прогрессом; отмена работает; окно не зависает во время сканирования.
- [ ] **Step 4: Очистка** — подтверждение, итог, пропуски; отмеченный только `user-temp` реально уменьшил `%TEMP%`; запись в `logs\`.
- [ ] **Step 5: `dotnet test`** → Core-тесты по-прежнему PASS.
- [ ] **Step 6: Commit** `feat: окно и вкладка Очистка`

---

### Task 9: Вкладка «Большие файлы», публикация, ручная проверка

**Files:**
- Create: `src/Cleaner.App/Views/BigFilesView.xaml`, `src/Cleaner.App/ViewModels/BigFilesViewModel.cs`, `publish.ps1`
- Modify: `MainWindow.xaml` (вторая вкладка)

**Interfaces:**
- Consumes: `BigFilesScanner.ScanAsync`, `FileDeleter.SendToRecycleBin(items, manualSelection: true, ct)`, `App.Services`

- [ ] **Step 1: Вкладка** — выбор диска (`DriveInfo.GetDrives()` типа Fixed), «Найти» (счётчик просмотренных файлов, отмена), таблица имя / папка / размер / дата, галочки, «Открыть папку» (`explorer /select,"path"`), «В корзину» с подтверждением; удалённые строки исчезают из таблицы.
- [ ] **Step 2: `publish.ps1`** — `dotnet publish src/Cleaner.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist`; проверка: `dist\Cleaner.App.exe` существует, запускается двойным кликом с UAC.
- [ ] **Step 3: Ручная проверка на реальной системе** (отчитаться Павлу по пунктам):
  - сканирование находит Temp, браузеры, Gradle/npm, сборки в `D:\Work`; `node_modules` не отмечены, Gradle не отмечен
  - при открытом Chrome — плашка «Закройте Chrome…»
  - корзина: положить тестовый файл, очистить, корзина пуста
  - большие файлы на C: показаны, тестовый большой файл ушёл в корзину
  - свободное место на C выросло на величину «Освобождено» (±5 %)
- [ ] **Step 4: Commit** `feat: большие файлы и публикация`
