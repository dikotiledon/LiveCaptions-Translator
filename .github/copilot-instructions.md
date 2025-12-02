# LiveCaptions-Translator Copilot Guide
## Environment & Build
- Requires Windows 11 22H2+ with Microsoft Live Captions installed; `src/utils/LiveCaptionsHandler.cs` kills and relaunches `LiveCaptions.exe`, so expect the system caption window to restart when debugging.
- Build/run with `dotnet build LiveCaptionsTranslator.sln` or `dotnet run --project LiveCaptionsTranslator.csproj`; ship releases via `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true` (csproj already enables ReadyToRun off and self-extracting single-file layout).
- Working directory stores mutable artifacts (`setting.json`, `translation_history.db`, overlay screenshots); clean them between experiments if you need a fresh profile.
## Runtime Architecture
- `src/Translator.cs` is the singleton orchestrator: static ctor launches/hides Live Captions, loads `models/Setting`, and grabs the shared `Caption` model.
- Three infinite loops (`SyncLoop`, `TranslateLoop`, `DisplayLoop`) run on background tasks. Keep new code non-blocking; all shared state (queues, caption text) must stay thread-safe.
- `SyncLoop` polls the automation tree for `CaptionsTextBlock`, cleans text via `utils/RegexPatterns` and `utils/TextUtil`, and enqueues sentences based on `Setting.MaxSyncInterval/MaxIdleInterval` thresholds.
- `TranslateLoop` dequeues text, respects `Translator.LogOnlyFlag`, and hands work to `models/TranslationTaskQueue`, which cancels superseded tasks so only the freshest translation is displayed/logged.
- `DisplayLoop` pushes results into `models/Caption`, driving bindings across every XAML page and the overlay window; it also applies chokers for complete sentences to keep UX readable.
## Settings & Persistence
- `models/Setting` is a serialized object saved to `setting.json` whenever any property setter fires (`OnPropertyChanged` calls `Translator.Setting?.Save()`). Batch related changes before triggering setters to avoid excessive disk IO.
- The settings file stores window bounds (`WindowBounds`) that `utils/WindowHandler` rehydrates, per-window state (`MainWindowState`, `OverlayWindowState`), API configs, and user prompt text; modify through the provided models so persistence stays consistent.
- `Setting.Configs` is a dictionary keyed by the exact API names in `TranslateAPI.TRANSLATE_FUNCTIONS`. Each entry is a list so the UI can cycle through multiple credentials/endpoints; update `ConfigIndices` when switching.
- `utils/SQLiteHistoryLogger` manages `translation_history.db` (global connection + async commands). Prefer its helpers (`LogTranslation`, `LoadHistoryAsync`, `ExportToCSV`) rather than issuing raw SQL so the `Translator.TranslationLogged` event continues to fire for the UI.
## UI Composition & UX Patterns
- WPF shell lives in `src/windows/MainWindow.xaml` and uses Wpf.Ui navigation; pages under `src/pages/` bind either to `Translator.Caption` (live text) or to data pulled from SQLite.
- `pages/CaptionPage` owns the main transcription display + log cards (`Caption.Contexts` queue). When altering context length, keep `Setting.MainWindow.CaptionLogMax` and `OverlayWindow.HistoryMax` in sync like the existing setters do.
- `windows/OverlayWindow` renders immersive captions over other apps. It reads all styling from `Setting.OverlayWindow`; any new visual option should be added to that model plus persisted via `WindowHandler` to avoid regressions.
- `SettingPage` hosts quick knobs plus an entry point to `windows/SettingWindow`, which auto-builds API-specific forms by reflecting each config’s `SupportedLanguages` static dictionary. Ensure new config types expose that property (override via `new`) so language dropdowns update.
## External Integrations & Extensibility
- Translation providers live in `src/utils/TranslateAPI.cs`. To add one: implement an async method returning a string, register it in `TRANSLATE_FUNCTIONS`, add a config class in `models/TranslateAPIConfig.cs`, seed defaults in `Setting`’s constructor, and (if user-editable) add a section in `SettingWindow.xaml` unless it belongs to `NO_CONFIG_APIS`.
- LLM requests are normalized through `utils/LLMRequestDataFactory` and the request DTOs in `models/RequestData.cs` to consistently disable reasoning/streaming for low latency; reuse those types instead of crafting ad-hoc payloads.
- Live captions automation relies on UIA (`Interop.UIAutomationClient`); `LiveCaptionsHandler.GetCaptions` caches the `CaptionsTextBlock` element and falls back if it becomes invalid. If Microsoft changes automation IDs, update the constants there rather than scattering new automation logic.
- Native window tweaks (topmost, click-through overlays) go through `utils/WindowsAPI`. Use those helpers instead of P/Invoke inlined elsewhere so app-wide behaviors (restore on exit, overlay opacity) keep working.
## Debugging & Productivity Tips
- Toggle log-only mode via the main window button (`Translator.LogOnlyFlag`) to collect transcripts without hitting APIs—useful when diagnosing Live Captions recognition or SQLite logging.
- `Setting.MainWindow.LatencyShow` prepends `[xx ms]` to translations by timing `TranslateAPI` calls; keep that flag in mind when parsing overlay text in logs/tests.
- History/table views refresh through the `Translator.TranslationLogged` event; fire it whenever you add alternative logging paths so `HistoryPage` and log cards stay in sync.
- Startup/exit hooks in `App.xaml.cs` restore the system Live Captions window; if you introduce alternative launch paths, be sure to keep `ProcessExit` callbacks wired so user settings aren’t left hidden.
