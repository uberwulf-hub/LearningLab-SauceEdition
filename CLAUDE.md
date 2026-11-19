# Learning Lab (Sauce Edition): notes for Claude

Read [docs/PLAN.md](docs/PLAN.md) and [README.md](README.md) first. Decided with Andrew on 2026-11-10: App Hub's
learning apps (Learning Games, Emulator Lab, Network Lab, Cert Lab + Cyber Range) move here **one app at a time, each
on Windows and Android before the next**, and each is **retired from App Hub** once its Learning Lab version is released
(App Hub's `$RetiredApps`; App Hub's data stays and Learning Lab reads it on its first start, as Media Player SE does).
Learning Games moved first (2026-11-10), Emulator Lab second (2026-11-10), Network Lab third (started 2026-11-13) and Cert Lab + Cyber Range fourth (engine and screens 2026-11-19) are both in 0.5.0
(not on SauceStore yet; App Hub's copies are retired only once it is). App Hub is `C:\Users\andre\Repos\AppHub` (GitHub `uberwulf-hub/ADocker`, also pushed to
`uberwulf-hub/AppHub` on 2026-11-13 so cloud sessions can read it); its CLAUDE.md has each app's rules
(kids' safety, copyright, simulated-only, fact-checked content) and they still apply here.

## Rules

- **Releases reach everyone who installed the app: never run the Release workflow or push a tag unless Andrew asks
  for that release.** The repository is `uberwulf-hub/LearningLab-SauceEdition` (public since 2026-11-19), listed in SauceStore through
  its latest GitHub release (`saucestore.json`: Windows zip + Android APK; keep `credits` complete for every
  third-party file or library).
- **Releasing: GitHub Actions only, like Bored Games (2026-11-12).** Set the version in `Directory.Build.props` and
  `src\LearningLab.Android\LearningLab.Android.csproj` (`ApplicationVersion` = major*10000 + minor*100 + patch,
  `ApplicationDisplayVersion` = X.Y.Z), move CHANGELOG.md's "Unreleased" lines under `## [X.Y.Z] - date` (they are the
  release notes; `### Added` / `### Fixed` become New / Fixed in SauceStore), push to main, then run the `Release`
  workflow (`release.yml`) with `vX.Y.Z` on main. It checks the version, runs all the checks, builds the Windows zip
  (unsigned, as Bored Games) and the Android APK (`android.yml`), and publishes the release. **Never sign the apk with
  another key:** it is signed with Learning Lab's key from the `ANDROID_KEYSTORE_*` secrets (`backup-android-key.yml` moves the
  key cached by earlier builds there, through a short-lived `SECRETS_TOKEN`, never a download; until then the cached
  key is used, the cache is never saved again, and a new key is never made: on a public repository a key must never
  be an artifact, a log line or a cache entry). 0.1.0-0.2.1 were
  signed on Andrew's PC with an older key (`%APPDATA%\LearningLabSE-Release`, kept as a backup only): a phone with one of
  those must uninstall Learning Lab once before installing 0.3.0 or later. `build.yml` runs the checks and builds
  test copies on every push; it never releases.
- Saved data persists until the person deletes it; every format change loads all older versions (fixtures in
  `tests\LearningLab.Tests\Fixtures`); never overwrite a save that failed to load (`LearningLab.Saves.SafeFile`).
- Tests never touch real data (`Data.UseTestFolder`), never open windows on the desktop (Avalonia headless), never
  play audio (`Platform.PlayWav` stays quiet) and never use the network beyond 127.0.0.1.
- Plain words for families. Touch first: buttons at least 44 px high, no hover-only actions, layouts that work at
  360-400 px wide (`-Snapshot` takes phone-sized pictures of each page; look at them).
- Never put the children's real names in the repository: not in code, tests, fixtures or docs (tests use Theo, Lily,
  Sam, Riley, Grandpa...).
- **The repository is public (2026-11-19, so Actions minutes are free).** Nothing private goes in: no children's
  names, no school, district or address, no keys, no copyrighted study lists. **Spelling Bee** bundles only Learning
  Lab's own lists (`Content\SpellingBee\practice-words.js`); the school's study lists (Scripps) were taken out when it
  went public, and families add them on each device through Add a word list (the old ids p2... come back with them).
- C# 14 note: inside a property, `field` is a keyword (it broke a canvas named `field` once).
- Learning Games' network messages must stay compatible with App Hub's Learning Games (JSON lines in `Family.cs`).

## How it is built

- `src\LearningLab.Engine` (net10.0, no UI): engines copied from App Hub (namespaces kept, e.g. `LearningGames`), plus
  `Data` (where data lives) and `Saves\SafeFile.cs` (App Hub's safe-save helper).
- `src\LearningLab` (Avalonia 12): `Views\MainView.cs` is a stack of `IPage`s (Back = phone's Back button, Esc on
  Windows; `Message(...)` replaces message boxes; `IKeyPage` gets keys before text boxes), `Views\Ui.cs` the look
  (App Hub's dark theme). Each app has its folder of pages. `Services\Platform.cs` is what each platform fills in.
- **Languages** (2026-11-10, Andrew: "a Language learning section, starting with spanish ... listen in the car ... decent
  free voices"): course data src\LearningLab\Content\Languages\<id>.json (format in FORMAT.md there; Spanish =
  Latin American / Mexican, 24 units in 4 levels, 509 phrases, written and then fact-checked by a separate pass; new or
  changed phrases must be fact-checked too; an item's id is MD5("es|en")'s first 6 bytes, so changing either text makes
  it a new item). Engine: Engine\Languages\ (course, LangProgress = <data>\Languages\progress.json through
  SafeFile with unknown fields kept and a blocked state for an unreadable file, Lesson = steps for the styles learn /
  recall / listen) and Engine\Audio\Wav.cs. App: Languages\ (pages, LessonAudio makes a whole lesson into one
  24 kHz WAV in Data.Cache("Languages") with item marks, so it plays with the screen off and can be saved).
  **Voices are the device's own, free and offline**: Windows through Windows.Media.SpeechSynthesis (the Desktop
  project targets net10.0-windows10.0.19041.0; SAPI can't see the free Spanish voices from Settings > Speech),
  Android's TextToSpeech synthesizeToFile (offline voices only) and MediaPlayer with a partial wake lock
  (WAKE_LOCK); saving goes to Music\Learning Lab (Android 10+ through MediaStore). Never ship or download a voice
  without Andrew's OK (Piper's neural voices were considered: each voice has its own licence).
  LearningLabSE.exe --check-voices <file.wav> lists Windows' voices and writes one phrase (no window, no sound).
  **Lessons, stories, verbs (2026-11-11, docs\LANGUAGES.md is the plan and the formats):** content files beside the
  course: `<course>-lessons*.json` (all read; 71 lessons), `<course>-stories.json` (20), `<course>-verbs.json` (83);
  each may be missing (buttons hide) and a file that can't be read is left out and named (`LangContent.LoadProblems`),
  never a crash. **Content is written, then fact-checked by a separate pass; never change Spanish text without a new
  fact-check** (mechanical fixes only). Engine: `Content.cs` (models, `LangContent.Load` / `Problems(course)`: the
  rules the checks hold every file to: units and items exist, each item of a unit with lessons is "new" in exactly one
  lesson, 8-14 exercises with each kind's rules, one `___`, build extras not in the sentence, unique choices, ¿ ¡,
  stories 6-20 lines with "after" units, verb lists of 5; `LangStory.Meaning` = tap-to-translate: the story's "words"
  incl. two-word keys like "se ríe", else one- / two-word course phrases), `Answers.cs` (compare ignoring case, spaces,
  commas, end marks; typed answers forgive accents / ñ / ¿ ¡ with a note; build tiles; word-by-word speech),
  `Conjugator.cs` (**verb answers are computed, never typed by hand**: present, preterite, near future; regular, stem
  changes ie / ue / i / u-ue, spelling changes, -uir, vowel-stem preterites, reflexives; `LangVerb` reads the list:
  `inf`, `en`, `level`, `stem`, optional `tenses`, `persons`, `irregular` = 5 per tense with null = the rule;
  `LangVerb.OnlyThird` keeps weather verbs to él and gustar-type verbs to él / ellos), `VerbQuiz.cs` (questions,
  choices; an accent is forgiven only when the plain form isn't another form of the verb: hablo is not habló),
  `LessonRun.cs` (wrong ones come back once at the end; points = right first time + 5; `Finish` meets / grades items
  and saves), `Lesson.cs` (+ `BuildDrill`: "How would you say", backchaining by syllables / words, returns after 2 / 5 /
  11 asks; `BuildStory`), `LangProgress` (+ lessons, points per day, goal, stories, verbs; still format 1: older files
  just lack them; fixture `Fixtures\Languages\progress-2026-11-10.json`). App: `CoursePage.cs` (path, goal ring,
  Continue, Settings), `LessonPage.cs`, `VerbPage.cs`, `StoryPages.cs`, `CarPage.cs` (drill and story styles).
  **Speech recognition is offline only** (`Services\IRecognizer`, `PlatformAudio.Recognizer`): Windows
  (`DesktopRecognizer.cs`) uses only a `SpeechRecognitionListConstraint` with an installed recognizer
  (`SupportedGrammarLanguages`); never dictation or topic constraints (they go online). Android
  (`AndroidRecognizer.cs`) only API 31+ `CreateOnDeviceSpeechRecognizer` (+ EXTRA_PREFER_OFFLINE); RECORD_AUDIO is
  asked when someone taps "Tap and speak", after the page explains. Otherwise `Available` is false and people mark
  themselves. Checks use a fake; never the microphone. Checks: `LangChecksNext.cs` (fixtures in
  `Fixtures\Languages` through `Lx.UseContent`, a conjugator table of over 150 forms checked by hand, every real lesson / story / the verb
  trainer played through when the real files exist, snapshots wide and phone-sized).
- **Cartoons (2026-11-13, Andrew: silly animated conversations of 20-30 seconds to work out "what's going on"):**
  `Engine\Languages\Cartoons.cs` (format in docs\LANGUAGES.md; `CartoonTimeline` from the audio's line marks or estimates;
  `Lesson.BuildCartoon` for the page and the car; `LessonStep.Voice` / `Pitch` = a character's second voice and pitch,
  `Wav.PitchShift`), app `Languages\CartoonStage.cs` (everything drawn in code at a `Time`, so pictures are exact) and
  `CartoonPages.cs`. Content is written, then fact-checked by a separate pass, like lessons. Checks in
  `LangChecksNext.CartoonEngine` / `CartoonPages`. **Feedback** (same day): `Services\Feel.cs` soft sounds + phone haptics
  (`Platform.Haptic`) for answers in Languages; Learning Games keeps its own sounds.
- **Home by subject (2026-11-11):** `Views\HomePage.cs` lists `Categories` (Language Arts, Math, Science, Social
  Studies, World Languages, Computers & Technology), each with `Entry`s (`Open` null = coming soon; `Player` = the
  question asked when the page needs a Learning Games player); `CategoryPage` shows them as tiles. `HomePage.Open` ->
  `Lg.OpenWithPlayer`: with a player picked the page opens at once, else `ProfilesPage(title, heading, then)` comes
  first and `Choose` replaces itself with the page (Back goes to the subject). The player chip (`Lg.ChangePlayer`)
  goes back to that "Who's playing?". Learning Games' pages that end with `BackTo<...>` must check `MainView.Has<T>()`
  first (the Workshop's results do), since its hub may not be in the stack. "All of Learning Games" on the home page
  opens the old flow (hub, family games, parent view).
- **Math: mastery and worked steps** (2026-11-11, Andrew: "Can we make [math] as good as" IXL; `docs\MATH.md` is the
  plan and its order of work). **We take IXL's ways of teaching, never its skill names, codes, question texts,
  explanations, art or look** (our own dark theme, Math blue). Every question is made by code and every answer
  computed, never typed by hand. Engine (`Engine\LearningGames\`): `Mastery.cs` = the score 0-100 per player per skill
  with **every number of the curve in that file** (`BandStart`, `Gain`, `Loss`, `LevelStart`; zones Getting started
  0-39, Building 40-69, Strong 70-89, Challenge zone 90-99, Mastered 100; a mastered skill stays 100), `LevelFor` maps
  the score to the generators' level 1-5, `UpNext` (started-not-mastered by score, then new skills in standard order =
  `Problems.SkillsFor` order). **Change the curve only with the simulations** in `MasteryChecks.Curve` passing: 90%
  right masters in a median of 15-25 questions (now 18), 70% right reaches Strong (>= 95% within 40) but masters
  within 30 at most 1 in 5 (now 15%), 50% right almost never, three misses in a row from 90-99 drop below 90.
  `Worked.cs` = Learn from it: `Worked.Steps(p)` = the generator's own `Problem.Steps` (set with `W(p, ...)` in
  `Problems.cs` / `MoreProblems.cs`; helpers `ColumnAdd`, `ColumnSub` (trades across zeros), `LongDiv`, `Repeat`,
  `Count`) or else its hints with "= ?" / "your answer" filled in, always ending with `Worked.Final` (states the
  answer). **Steps never draw random numbers** (`R()`), so the problems stay the same for a seed. **A new skill or a
  changed generator needs steps computed from its own numbers**; the checks generate every 2nd / 6th grade skill at
  every level for 2,000 seeds and recompute every `a op b = c` written in the steps (+ - × ÷, chains, parentheses,
  fractions as division, `%`, `q R r`; a line that isn't plain arithmetic is skipped, so write sums as "a + b = c"),
  require the answer in the last line, numbers in the steps of every non-choice question, and recomputed sums in every
  explanation of the column / division / equation skills (`MustAddUp`). Saving: `Player.Mastery(id)` /
  `RecordMastery` in `Profiles.cs`: `"Mastery": { skill: { Score, Answered, Right, Seconds, Last, MasteredOn } }` in
  profiles.json (new fields only; it also calls `RecordAnswer`, so levels, mistakes, days and reward points go on as
  before; fixture `Fixtures\LearningGames\mastery-2026-11-11.json`). App `LearningGames\MasteryPages.cs`: `MasteryUi`
  (ring, zone colours, skill tile, Up next panel), `SkillMapPage` (grade tabs 1-7, grades without skills say
  "coming"; areas with "x of y mastered"), `MasteryPracticePage` (question at `LevelFor(score)`, `GamePage.PictureFor`
  in a Viewbox, typed answers with the games' number pad or choice buttons; right = next question, wrong = Not quite +
  the answer + Learn from it + Got it; `Scratch` = `Views\ScratchPad` under the answer (2026-11-13, cleared each question, Hide is remembered while the app runs); 100 = Mastered card, `Sound.Win`, `MasteryReward` bonus points). The Math
  subject's first entry is the skill map, and `CategoryPage` shows Up next for the player on the Math page. The games,
  practice in Learning Games and the Long Division Workshop are unchanged.
- **Spelling Bee** (2026-11-11, Andrew: a school spelling bee on Friday; modelled on his spelling-bee.html, our own
  code and look): engine `Engine\SpellingBee\` (`Words.cs`: `BeeWord` "a|b" alternatives and "_" spaces,
  `BeeImport.ParseHtml` reads a page's `const LISTS = [ {id, name, emoji, note, words} ]`, `ParseText` .txt / .csv /
  pasted words with "# Name" headers; `Check.cs`: `BeeCheck.Judge` exactly like the web page's judge (accents and
  capitals ignored, any alternative, spaces / dashes / apostrophes forgiven as Close; text with spaces is also read as
  spoken letters), `Wrong` marks, `Hint` levels 1-3, `SpellOut` ("C, A, B, I, N", "space", "dash"), `ParseSpoken`
  = the page's parseSpoken (letter names, "double X", "double u" = w, "dash", skip words); `Run.cs`: `BeeRun` (first
  try right = right and leaves Tricky Words, one retry, a second miss or "I don't know" = shown, both missed);
  `Store.cs`: `BeePlayers` = `<data>\SpellingBee\players.json` by Learning Games player name (tricky, lists,
  howMany, shuffle, autoSay, history; JsonObject so unknown fields stay; blocked when unreadable until Start fresh)
  and `BeeLists` = `<data>\SpellingBee\lists\<id>.json`, all through SafeFile). The bundled lists are
  `Content\SpellingBee\*.js` (the page's LISTS block, AvaloniaResource, read with ParseHtml). App
  `SpellingBee\`: `Bee.cs` (lists, players, importing, `Bee.Say` through `Platform.Speech.Speak(text, "en", rate)`,
  `Bee.Said` for the checks), `BeeHomePage`, `BeePracticePage` + `BeeResultsPage`, `BeeListPages` (add: the
  StorageProvider file picker, never in the checks; rename / delete). `ISpeech.Speak(text, lang, rate)` and
  `IRecognizer.ListenLetters` are default interface methods: Windows listens with an SRGS grammar of letters
  (`SpeechRecognitionGrammarFileConstraint`, offline, file in `Data.Cache("SpellingBee")`), Android with the
  on-device recognizer and longer pauses. Checks: group `spelling` (`SpellingChecks.cs`, fixtures in
  `Fixtures\SpellingBee`: a made-up practice page, a .txt, players and list files).
- **Emulator Lab** (moved 2026-11-10; App Hub's CLAUDE.md has its section): the engine is App Hub's
  `Apps\EmulatorLab\Engine` in `Engine\EmulatorLab\` (namespace `EmulatorLab`, otherwise unchanged except: `Display`
  no longer draws its font or owns a bitmap (`Display.SetFont` from the app's `ScreenFont`, pixels shown by
  `ScreenView`), `KeyMap.Special` takes the key's name (Avalonia's `Key.ToString()`), saves through
  `LearningLab.Saves.SafeFile`; Editor.cs, Shell.cs and SafeSave.cs were left behind). **Every byte the machine runs
  stays ours**: the boot ROM, LabOS (`os\*.s`), the demos (`os\programs\`), the lessons (`os\examples\`) and the
  conformance programs (`tests\*.s`) are embedded resources (`LabSources`), unpacked into
  `Data.Cache("EmulatorLab")\sources` and assembled at start; never add downloaded ROMs, images or kernels. Keep
  `key_wait_wfi` on the wfi in `getkey` (`Computer.RunVirtual` stops there when it waits for keys) and `.align 2`
  before word tables after strings. `Computer.RunVirtual` is the deterministic headless run (tests, snapshots);
  `Runner` is the background thread that only the page's DispatcherTimer reads (30 ms; nothing on it touches
  Avalonia). `0x80000000` is `Map.RamBase` (a uint in C#). App: `EmulatorLab\EmulatorPage.cs` (control bar; tabs
  Computer / Look inside / Assembler / What's happening?; Look inside sits beside the monitor at 1100 px and wider,
  `Compact` under 700 px puts less used buttons behind `...` / More...; the machine gets keys while the screen or the
  phone's type box (`typeBox`, a one-space sentinel: an empty box = Backspace) has the focus, through `IKeyPage`
  before MainView's Esc = Back; F5 / F10 / F11 / F12 always), `AsmEditor.cs` (a TextBox with transparent text, a
  colour layer and a gutter placed from the TextPresenter's TextLayout), `ScreenView.cs`, `EmuSettings.cs`
  (settings.json in App Hub's format as a JsonObject, unknown fields kept; `BringAcrossFromAppHub` copies
  settings.json, disk.img and their .bak files once on Windows; fixtures in `Fixtures\EmulatorLab`). Open / Save as go
  through the TopLevel's StorageProvider (never in the checks). App Hub's `KeptForUpdate` is still read: a program
  kept for an App Hub update opens in the Assembler with a note.
- **Network Lab** (moving since 2026-11-13; App Hub's CLAUDE.md has its section and rules: simulated only, no vendor
  names, the brand only in `brand.json`, routers and switches saved as their running-config and rebuilt by typing it
  back, every lab fails at the start and passes with its answer). The engine is App Hub's `Apps\NetworkLab\Engine` minus
  its WPF files, in `Engine\NetworkLab\` (namespace `NetworkLab`, unchanged except: `Geometry.cs` holds what it took
  from WPF and the console view (`Rect`, `Completion`, `DrawColor.Lighter` / `IsColor` for `DrawArt`'s), `PortLayout.cs`
  is `PortPanel.Layout` / `PanelSize` and `TopologyView.AutoPorts`, `brand.json` is embedded and read by
  `Brand.LoadBuiltIn`). **The screens (2026-11-18, touch first)** are in
  `src\LearningLab\NetworkLab\`: `NetLabPage` (toolbar Move / Cable / Ping / Delete / Undo / Redo / Fit / Simulation /
  Labs / More, icons only under 640 px; the canvas; the side panel = the lab (story, goals checked 1.2 s after an edit,
  Hint, the answer, Do it for me) and in Simulation mode the steps, events and `NetSim.Details`; under 820 px the panel
  takes the screen and Back closes it; the palette of `Catalog.Kinds` along the bottom adds at `NetCanvas.NewSpot`;
  settings.json (brand, labels, last lab) and `autosave.netlab` in `Data.For("NetworkLab")` through SafeFile, App Hub's
  `%APPDATA%\NetworkLab\autosave.netlab` and brand brought across once; Open / Save .netlab through the StorageProvider),
  `NetCanvas` (App Hub's TopologyView: one finger drags a device or pans, two pinch, wheel zooms, a tap opens a device,
  hold or right-click = `MenuAsked`; tools select / connect (two taps, `PortLayout.AutoPorts`; `PortFrom` + `PortAsked`
  when a port was picked from the menu) / pdu / delete), `NetSim` (App Hub's SimDriver), `NetTerminal` (App Hub's
  ConsoleView with a real TextBox for the typed line so phone keyboards work, a key row Tab ? Up Down Ctrl+C Ctrl+Z
  Clear, selectable output kept to 3000 lines, the nano editor over it; `TypeLine`, `Typed`, `Tab` for the checks),
  `DevicePage` (App Hub's DeviceWindow as a page: CLI / Command Prompt / PowerShell / Terminal / Console, Config with
  `IpForms` for the checks, the home router's and access point's Setup, the cloud's About, Physical with power and SFP
  modules; the tab row scrolls sideways on a phone), `NetIcons` (App Hub's Icons). **The device screens (2026-11-19):**
  an end device's first tab is its screen (`Desktops.Create` / `TabName`: Desktop, Screen for phones / TV / NAS / camera,
  Front panel for printers; a Linux server has none and its Terminal tab is `ServerScreen`, the tty). `DeskKit.cs` =
  App Hub's DesktopFrame + DesktopApps: `DeskArea` (the pretend windows: dragged by their 44 px title bar when the area is
  700 px wide or more, else every window fills the area, newest on top; `InnerWindow`), `Lt` (the light pretend-window
  pieces: `Lt.Scope` = a light `ThemeVariantScope` with dark text, buttons that keep their colour on hover, `Row`, `Link`,
  `FieldRow`, `ApplyManual`), `Glyphs` (app pictures in code), `DesktopApps` (`Prompt` / `Terminal` = `NetTerminal`s,
  `PrintersApp`, `WifiList` (rows tagged `wifi:<ssid>`), `Join`). `NetBrowser.cs` = `HtmlView.Render` (headings,
  paragraphs, lists, bold / italic, br / hr, links, simple forms; a paragraph with links is drawn as a row of words,
  because Avalonia drops the runs before an `InlineUIContainer`; `Page.Text` / `Links` / `Fields` / `Buttons` for the
  checks) and `MiniBrowser` (pages only from `Request.Http`). `NetDesktops.cs` (`IDesktop.OpenApp(id)`,
  `GenericDesktop`, `ServerScreen`, `ApplianceScreen`), `NetWinDesktop.cs` (`WinDesktop`, `WinSettings` with
  `LastEditor` for the checks, `WinControlPanel`, `WinExplorer`), `NetOtherDesktops.cs` (`LinuxDesktop` /
  `LinuxSettings`, `MacDesktop` / `MacSettings`, `PhoneScreen`, `PrinterPanel` (`Press`), `TvScreen`), `NetServices.cs`
  (the Services tab of servers and NAS: web pages edited in place, DNS, DHCP, shared folders; `Dk` = the dark form
  pieces and section buttons) and `NetIosConfig.cs` (the routers' and switches' Config tab after CLI: forms typing into
  their own `CliSession` (kept out of `Ios.Sessions`, so a reload doesn't touch it) and the Equivalent commands box;
  `Boxes` / `Actions` / `Log` for the checks). **The physical view and the drawings (2026-11-19, touch first):**
  `PhysicalView` (App Hub's PhysicalView: campus, floor plans, rack fronts; toolbar Physical / Logical, a second bar with
  the layout and floor pickers, the cable kind, Room / Wall / Door / Label / Rack (floor only), Wi-Fi range, Plan
  picture; one finger drags or pans, two pinch, a tap opens a device or goes into a building, double tap zooms into a
  room or opens a rack, hold = `MenuItems` (mount in a rack at the top free U, move to another floor, rooms, labels);
  `PressAt` / `MoveTo` / `ReleaseAt` / `Tap` / `DragFinger` are the gesture path the checks drive; `UseLayout` with
  Undo; new devices from the palette go mid-view on the floor shown), `PortPanel` (App Hub's PortPanel drawn at 1.3x:
  the Cable tool's picker in a flyout (`Pick`, `TapAt`, `PortCenter`; hold an SFP slot for its module), the picture on
  DevicePage's Physical tab (`ModuleOnTap`), and racks (`PortPanel.Draw`)), `DrawTool` (App Hub's DrawTool through
  `IDrawHost`, shared by `NetCanvas` and `PhysicalView`: tools draw:rect / roundrect / ellipse / line / arrow /
  polyline (tap the last corner again or Finish) / freehand / text / note / area / image / label / calibrate and
  obj:<art>; handles reach 18 px; `MenuItems`; questions through `NetAsk.Text`, a flyout, answered by `NetAsk.Auto` in
  the checks; copy and paste through `DrawTool.LastClip` only (Avalonia 12's clipboard API isn't used yet)),
  `DrawArt` (shapes, map, the library's clip-art, `ObjectIcon`), `DrawPanels` (properties with finger-sized swatches,
  layers, `ObjectLibrary`) in NetLabPage's side panel (`SideMode` "draw", whole screen under 820 px) with the map
  styles, map / plan picture (StorageProvider, never in the checks), Set the scale and the labels (IP addresses, port
  names, VLANs on links, Wi-Fi range); the Draw button swaps the palette for the drawing bar. **Not moved yet:** Settings beyond brand and labels, dragging from the palette / library onto a view, the rubber
  band selection, Picture (PNG of the view), the system clipboard for drawings. Checks: group `network`
  (`tests\LearningLab.Tests\NetworkLab\NetChecks.cs`, App Hub's `Tests.cs` plus the drawing model checks; the brand check
  scans `src\LearningLab.Engine\NetworkLab` and `src\LearningLab\NetworkLab`; `NetPages.cs` drives the screens:
  building and cabling by taps, undo, a PC's IP form and ping, the router CLI with Tab, Simulation mode, the autosave,
  a lab to Do it for me, phone-sized) and group `network-desktops` (`NetDesktopPages.cs`: every screen wide and
  phone-sized; Settings' static address, the browser by address / link / DNS name, adding a printer and printing,
  File Explorer on a share, Services' DNS record and page, the router's interface / hostname / route forms seen in the
  CLI, the switch's VLAN and Vlan1, the phone joining Wi-Fi, the printer's menu; buttons at least 44 px; App Hub's IP
  window check and the desktops' terminals' Tab checks); group `network-physical` (`NetPhysicalPages.cs`: App Hub's `TestsDrawing.cs` view
  checks (turned hit testing, labels following devices, VLANs, layers, maps, the scale, saving, a version 1 file,
  drawings never changing a lab, the picture of a view, every library object drawn), then each layout and its campus,
  placing / moving / cabling with the port picker and the lengths, rooms, walls, doors, labels, racks and mounting, the
  SFP picture, every drawing kind on both views with handles, turning, groups, lining up, copy / paste / undo, maps,
  the scale, plan pictures, layers, the autosave; wide and phone-sized).
- **Cert Lab and Cyber Range** (moving since 2026-11-19; App Hub's CLAUDE.md has their section and rules: **copyright**
  (never Ascendient / Red Hat / Microsoft / CompTIA text; the disclaimers in course.json and About stay), **simulated
  only** (nothing reaches a real network, machine or service; nothing signs in anywhere), **seeded** challenges (flags
  and details from the seed, so answers can't be shared), every try-it / lab / exam fails at the start and passes with
  its solution, exam explanations secret until taken). **Step 1, the engine:** App Hub's `Apps\CertLab\Engine` minus
  its screens, in `src\LearningLab.Engine\CertLab\` (namespace `CertLab`, C# 5 style kept). Split from App Hub files
  that mixed in WPF: `VimEngine.cs` (Editor.cs without VimView / EditorHost), `TermTypes.cs` (`ITerm`, `Completion`
  from TermView.cs), `Clip.cs` (`IClipboard`, `MemoryClipboard`, `ClipText`; `ClipText.Current` is in memory until the
  app sets the platform's clipboard), `ReadText.cs` (`ReadDoc` with `object` elements and `ReadDoc.ParentOf` for the
  screens' tree, `Pronounce`, `SpeechSettings`; `ReadText.FromTree` stays behind), `Speech.cs` (`ReadAloud`, `ISpeaker`,
  `FakeSpeaker`; no SAPI), `RangeGlossary.cs` (partial: load and match; `Linkify` / `Tip` / `Card` stay behind),
  `RangeLook.cs` (RangeStyle.cs's data: category styles, levels, the icons' path data `IconPaths`, `Unwrap`),
  `Resume.cs` (the file formats `CertResume`, `RangeResume`; `SaveForUpdate` / `ResumeNow` were parts of MainView /
  ExamRunPage / RangeView). Moved out of screens: `SearchIndex.Allows` (was `MainView.SearchAllows`),
  `CertLabData.OpenMachines` (was `SimPage.OpenMachines`). **Platform fixes (App Hub ran on .NET Framework with
  Windows' NLS; .NET 10 uses ICU and other runtime rules):** every culture-sensitive `StartsWith` / `EndsWith` /
  `IndexOf(string)` says `StringComparison.Ordinal` (ICU ignores control characters: `"abc".StartsWith("\u0001")` is
  true, which made grep call every text binary); `CertLab\.editorconfig` turns CA1310 on so new ones warn.
  `FwHash.Of` = .NET Framework's x64 string hash (string hash codes are random per process in .NET 10, and seeds,
  device ids, serials and hardware hashes came from them; never call `GetHashCode()` on a string here).
  `PsKeys` = the pretend PowerShell hashtables' key comparer (fixed hash codes, so keys list in the same order every
  run). `PsConv.Us` = en-US as 5.1 prints it (ICU puts U+202F before AM / PM). `FwDouble` / `PsConv.FwNum` = doubles
  in format strings rounded as .NET Framework did (2.25 -> 2.3); `DoubleStr` = 15 digits (0.1 + 0.2 = 0.3).
  `PsConv.WordSort` / `NameOrder` = Windows' word sort (hyphens count last: WEB2 before WEB-TEST) for Sort-Object and
  Get-VM. `NetFxSurface` = the .NET members 5.1 shows in Get-Member (no spans, Microsecond, newer overloads).
  `WinPath` = file names in the pretend Windows paths (System.IO.Path splits only at / off Windows). `Ripemd160.cs`
  (Get-FileHash RIPEMD160; .NET 10 has none). `Encoding.GetEncoding(1252)` through the code pages provider. **Data:**
  the courses (`CertLab\Courses\<course>\`) and the range (`CertLab\Range\`) are EmbeddedResources
  `CertLab/Courses/...`, `CertLab/Range/...`; `CertLabContent.Root` unpacks them once per build into
  `Data.Cache("CertLab")\content` (emptied first when the build changes) and is the folder App Hub called its app root
  (`CoursesDir`, `RangeDir`, `Man.Dir`, `PsHelp.Dir`). Saved data: `CertLabData.Dir` = `Data.For("CertLab")`, laid out
  as App Hub's `%APPDATA%\CertLab` (profiles.json, machines-<name>.json, speech.json, resume.json, range\progress.json,
  range\resume.json), all through `LearningLab.Saves.SafeFile`; `CertLabData.BringAcrossFromAppHub()` copies App
  Hub's files and backups once on Windows (only names we don't have; `apphub-brought-across.txt` marks it done).
  Checks: App Hub's `Engine\Tests*.cs` in `tests\LearningLab.Tests\CertLab\` (`Tests.Run(quick, only)`, every
  check to `Program.Check` as "certlab <group>: ..."); group `certlab` = App Hub's `-Test -Quick` (one seed per
  challenge, ~35 s), `certlab-full` = every seed and exam seed (~60 s), `-Only certlab:<group>[:file]` one group of
  the full set (`certlab:range-puzzles`, `certlab:labs:lab-05`); new groups `unpack`, `bring-across`
  (`TestsLearningLab.cs`); fixtures `Fixtures\CertLab` (resume v1). **Left for the screens step** (step 2 below did Cert Lab's): every `Ui*.cs`
  (MainView, pages, the MD-102 portal `UiEp*`, the AZ-800 consoles `UiWs*`, PBQ views, exams, Cyber Range's
  `UiRange*`), TermView / TermSurface / GrubView, VimView / EditorHost, SelText / CodeCopy / `ClipText.Route`, the
  drawing kit of RangeStyle.cs (`RangeGfx`, `RangeCard`, `RangeRing`, `RangeFx`, `RangeWalk`), `ReadText.FromTree`,
  SAPI / WinRT voices (use `Platform.Speech`), `ShellIntegration`, and the screen checks: groups `clip`, `ep-ui`,
  `ws-ui`, `range-ui`, `sec-ui`, `sec-window`, and the window parts of `speech`, `search`, `grub` (window keys),
  `resume` (keeping and bringing back an exam / lab / run) and `range-look` (walkthrough layout, every page builds).
  **Step 2, Cert Lab's screens (2026-11-19, touch first)** in `src\LearningLab\CertLab\` (namespace `LearningLab.CertLab`):
  App Hub's rail became pages on the stack. `CertApp` (= App Hub's MainView state: courses, `ProfileStore` "who is
  learning" (Cert Lab's own people, not Learning Games players), course, progress, search index, status line, reading
  aloud, `KeepWork` / `ResumeOffer` / `ResumeNow` / `DismissResume` with `CertResume`; `CertApp.Open(false)` from the home
  entry, `CertApp.Fresh(true)` in the checks = in-memory clipboard, FakeSpeaker, seed 1, no timers). `CertPage` = the
  frame of every page (status line, optional `ReadBar`). Pages: `CertHomePage` (resume banner, people, five course cards
  with progress, About), `CourseHubPage` (search box + tiles), `LessonsPage` / `LessonPage` (IReadable; `Reveal(part,
  index)` for search; quiz; try-it -> `SimPage`), `LabsPage` (`StartLab`), `SimPage` (practice / lab / try: toolbar, Skip
  time flyout, `Consoles`, task panel beside it at 820 px+, else "Your tasks" over it and Back closes it; Save / Open saved
  through `CertLabData.OpenMachines`), `ExamsPage` / `ExamRunPage` (tasks + consoles side by side at 900 px+, else a
  Questions / Machines switch; questions-only exams one item at a time with navigator, flags, review; `SetAnswer`,
  `RestoreAnswers`; Back asks "Leave the exam?" and keeps it) / `ExamResultsPage`, `ExamItems` (choice, multi, order by
  taps with up / down, yesno, case study section buttons), `PbqView` / `PbqListPage` / `PbqPracticePage` (tap-to-mark
  log lines, tap a card then a slot, firewall rule cards of drop-downs, tap a device then "Put it here"), `CardsPage`,
  `CheatPage` (Copy instead of Print), `SearchPage` (`SearchIndex.Allows` keeps exam secrets until taken; `Open(doc)`),
  `TaskSetReviewPage`, `ManViewPage`, `CertAboutPage` (every course's disclaimer), `RevealUi`. `CertUi` = the pieces
  (rich text with `code` / **bold**, `CodeCopy` chips as a 44 px row under the text, `TermBlock` with Copy, `Find` /
  `Press` for the checks) and `AppClipboard` (ClipText on Avalonia's clipboard; reading is async: `AppClipboard.Read()`).
  `ReadBar` + `PlatformSpeaker` (ReadAloud over `Platform.Speech`, which has no "finished" event: each sentence gets the
  time it takes at that speed). **The terminal `CertTerminal`** (reused by Cyber Range): `new CertTerminal(term)` for an
  `ITerm`; `TypeLine`, `Typed`, `Tab()`, `Text`, `Poll()`, `SendKey(key, mods)` / `SendText` (to the line, GRUB or vi),
  `KeyByName("Up"|"Ctrl+X"...)` / `TextToScreen` (GRUB and vi), `PasteText` / `PasteClipboard()`, `SelectAll` /
  `Select` / `CopySelection` / `SelectedText`, `Mode` ("shell" | "grub" | "vim" | "nano"), `GrubShown`, `Vim`, `Nano`,
  `Attach(term)`, event `Activity`, statics `QuietRun`, `ConfirmPaste`, `GrubAutoSeconds`. The typed line is a real
  TextBox under the selectable output (3000 lines); the key row changes with the mode; GRUB (`GrubView`, 80 x 25,
  scrolls sideways on a phone) and vi (`VimView` in `EditorViews.cs`) get a "Type" box (one-space sentinel like Emulator
  Lab's) so a phone's keyboard types into them; nano (`EditorHost`) is a TextBox with Save / Exit / Cut line and Yes /
  No / Cancel buttons. `Consoles` = a simulator's terminals with heads (Reboot / Power, or Restart in exams): side by side
  when two fit at 1000 px, else a tab per machine. **The MD-102 portal and AZ-800 consoles** (`CertLab\Portal`, written
  separately) plug in through `CertWorkspaces.Make` (sim, inExam) -> Control, `CertWorkspaces.Tick` and
  `CertWorkspaces.Terminals`; until registered, those courses show their machines' PowerShell consoles with a note.
  **Keeping work:** `MainView.AppClosing` (raised by MainWindow.Closing and Android's OnStop) -> `CertApp.KeepWork`;
  pages closed normally mark the resume used. Cyber Range has its own home entry `range` (step 3 below). **Not moved:**
  printing cheat sheets, voice choice (the device's default voice), dragging (order / match / place are taps), Ctrl+F,
  the read-aloud right-click "from here". Checks: group `certlab-screens` (`tests\LearningLab.Tests\CertLab\CertScreens.cs`,
  ~18 s): home and every course, a lesson read aloud and its try-it typed into the terminal, the terminal (keys, Tab,
  history, Ctrl+C, Ctrl+D, copy, paste and pastes that ask or stop at an editor, Save / Open saved, Skip time), GRUB by
  keys to rd.break, vi and nano, a lab typed into the terminals, the PowerShell, MD-102 and Security+ exams to full
  marks, search and its secrets, cards, cheat sheets, every PBQ practice scenario, resume of a lab and an exam, an
  unreadable resume file, phone-sized pages, buttons at least 44 px.
- **Step 3, Cyber Range's screens (2026-11-19, touch first)** in `src\LearningLab\CyberRange\` (namespace
  `LearningLab.CyberRange`; not `Range`, which clashes with System.Range): App Hub's `UiRange*.cs` and RangeStyle.cs's
  drawing kit for Avalonia. App Hub's one window with breadcrumbs became pages on the stack: `RangeHomePage` (home entry
  `range`) -> `RangeCategoryPage` (the levels as a path, never locked) -> `RangeLevelPage` (challenge cards with chips,
  puzzles grouped by topic) -> `RangeBriefingPage` (scenarios; puzzles start at once) -> `RangeRunPage` (replaces the
  briefing) -> `RangeResultsPage` (replaces the run; Play again replaces the report). `RangeApp` = App Hub's RangeView
  state (scenarios, puzzles, glossary, `RangeStore` progress in `CertLabData.Dir\range\progress.json`, the run,
  status line, `Open` / `StartScenario` / `StartPuzzle` / `FinishRun` / `EndRun`; `KeepWork` on `MainView.AppClosing`
  -> `RangeResume` in `range\resume.json`, `ResumeOffer` / `ResumeNow` / `DismissResume` / `RestoreRun` as App Hub's
  SaveForUpdate; `RangeApp.Fresh(true)` in the checks = in-memory clipboard, seed 1, no animations or timers, the
  report at once). `RangePage` = every page's frame: status line, the rank chip in the top bar (opens Badges and
  ranks), and the layer the glossary card is drawn on (`ShowTerm`, `TapTerm` for the checks; Back, Close or a tap
  outside closes it). `GlossText` = text with the glossary's words dotted; a tap finds the word through
  `TextLayout.HitTestPoint` (no hover-only tips); `RangeExplain` = "Explain it simply" with the words to know as 44 px
  buttons. `RangeKit.cs`: `Rg` (icons from `RangeLook.IconPaths`, icon tiles, chips, tags, bars, segments, panels,
  callouts, banners, medals, insignia, gradients and glows), `RangeCard` (a Button that keeps its card look),
  `RangeSwitch` (a 44 px toggle button), `RangeRing`, `FlowGrid` (equal columns that fit: two cards wide, one on a
  phone), `RangeSplit` (main + side, stacked when narrow), `RangeFx` (animations only when `On`, never in the checks).
  Every category's look comes from `RangeLook`; never hard-code one. `RangeRunPage`: `CertTerminal` in a frame titled
  with the machine, the objective card (answer box, meter, celebration; the report 1.6 s after the last capture),
  `RangeTabs` Brief / Findings / Map / Notes / Loot / Hints / Defender (44 px headers that scroll sideways); beside the
  terminal at 900 px+, else a meter strip, Mission opens the card and tabs over the terminal (Back closes them) and
  More holds Skip 1 min / Give up. Back during a run asks "Leave this challenge?" (no score). The walkthrough
  (`RangeWalk`) is only ever on the report. **Not moved:** hover animations of cards (lift, glow), keyboard focus
  rings of App Hub's cards, the hero's dot grid. Checks: group `range-screens` (`tests\LearningLab.Tests\CertLab\RangeScreens.cs`,
  ~16 s): App Hub's range-look data and walkthrough checks, home new and with progress, tour, badges, glossary and its
  search, every category and level page, a briefing with Explain it simply and a glossary word's card, by taps and the
  terminal: Open Door to every flag, a SOC Analyst scenario's questions, a Blue Team scenario's questions and fixes, a CTF
  puzzle with hints and Learning mode, a Linux Ladder step, the Defender tab, giving up, the report (score, badges, the
  walkthrough only afterwards), progress read back, a run kept and brought back, an unreadable resume file, Back during
  a run, and phone-sized pages (buttons >= 44 px, nothing wider than the screen).
- **Cert Lab's practice portal and consoles (2026-11-19, touch first)** are in `src\LearningLab\CertLab\Portal\`
  (namespace `LearningLab.CertLab.Portal`): App Hub's `UiEp*.cs` / `UiWs*.cs` for Avalonia. `PortalWorkspaces.Make(sim,
  exam, status)` gives `EpWorkspace` for an `EpSim` (MD-102), `WsWorkspace` for a `WsSim` (AZ-800), else null; the lab and
  exam pages host it in place of terminals and call `PortalWorkspaces.Tick(w)` each second (it polls the consoles) and
  `PortalWorkspaces.OnBack(w)` from their `OnBack`. The PowerShell consoles inside are made by
  `PortalWorkspaces.TerminalMaker` (Cert Lab's terminal view, polled through `TerminalPoll`) or else the plain
  `PortalConsole`. `EpWorkspace` (+ `EpBlades`, `EpForm` = schema forms / wizard / editors / settings catalog,
  `EpAssign` = `AssignEditor`, `RuleBox`, `WhatIfView`, `EpDevices` = device pages, reports, enrolment, `EpDeviceTab` =
  the Devices tab, the light pretend Settings app through Network Lab's `Lt`, Company Portal, setup): every change goes
  through `EpOps` / `EpSim.Directive`, as in App Hub. `WsWorkspace` (+ `WsAduc`, `WsGpmc`, `WsDns`, `WsWac`, `WsAzure`;
  `WsForm` builds `key=value` pairs) runs every tap as a `WsOps` console line (`LastLine` for the checks). `Pk`
  (`PortalKit.cs`) is the kit: 44 px buttons and links, `Table` (cards under 760 px), `Pair` (label above value),
  `Capped` (a width cap that stays on the left; MaxWidth alone centres), `OnText` (text changes at once:
  `TextBox.TextChanged` comes later from the dispatcher), `Detach` (forms kept between redraws), `Strip` (a row that
  scrolls sideways). Under 760 px (`NarrowBelow`, from the control's own width) the portal's menu is a drawer behind the
  menu button (with Close the menu), the device list a picker, and the consoles' trees fold behind "Show the tree".
  App Hub's hover tips became visible text (the catalog's descriptions). Checks: group `certlab-portal`
  (`tests\LearningLab.Tests\CertLab\Portal\PortalScreens.cs`: App Hub's ep-ui and ws-ui checks on the simulators of
  the MD-102 and AZ-800 courses' first labs, the main flows through the controls, the drawer / tree / Back on a phone,
  44 px buttons, and App Hub's scenes wide and phone-sized).
- Pages are built in C# code (no XAML besides App and MainWindow), like App Hub's C# apps.
- `tests\LearningLab.Tests`: groups `shell`, `games-math` (App Hub's 3 million math checks), `games-net`,
  `games-pictures`, `games-pages`, `languages`, `spelling`, `math-mastery` (the curve, worked steps, saves, the skill
  map and practice wide and phone-sized), `emulator` (App Hub's `Engine\Tests.cs` + `TestsSaves.cs`, copied to
  `tests\LearningLab.Tests\EmulatorLab\`, plus settings, keys and the pages with App Hub's scenes Shell, Graphics,
  Snake, Inside, AsmError, Learn wide and phone-sized), `network`, `network-desktops`, `network-physical`, `certlab` / `certlab-full` / `certlab-screens` / `certlab-portal` / `range-screens` (see Cert Lab above); `-Only a,b`, `-Snapshot dir`, `-Seed n`.

## Not yet tried

The Android app builds (2026-11-10: .NET Android workload 36.1, SDK platform 36 and build-tools 36.0.0 in
`%USERPROFILE%\DevTools\android-sdk`, JDK 17), but it has not run on a phone or an emulator yet.

Everything on a real screen and a real phone: touch, the soft keyboard, the number pads, sounds and voices by ear,
the block games in Edge, family games between real devices (a phone and a PC, a phone and App Hub's Learning Games),
the Windows firewall prompt when hosting, Android's Back button, rotating the phone; Languages with real voices by ear (a
Spanish voice on Windows, Google's voices on a phone), a lesson playing on with the phone's screen off and through a
car, saving a lesson to Music on a phone; the lessons, verb trainer and stories by touch (word tiles, matching, typing
with the soft keyboard and its accents), "Say it" with real speech recognition (an installed Spanish recognizer on
Windows, the on-device recognizer and the microphone permission on Android 12+; which phones have Spanish on-device),
the conversation drill and stories by ear in a car, whether lessons feel about 10 minutes; Emulator Lab on a real screen and phone: typing into the machine (focus, the
F keys, Ctrl+C, the phone's keyboard through the type box, especially Backspace and Enter on different keyboards), the
key row, the Examples menu and the More flyouts, Open / Save as with the file pickers (Android's .s files and their
types), the questions before replacing the editor's text, the colour editor with the soft keyboard (caret, selection,
scrolling, the colours staying on their lines), double-tapping breakpoints and tapping errors, the speed slider and Look
inside while running, the speed on a phone, bringing App Hub's real Emulator Lab data across; Spelling Bee on a real
screen and phone: the voice reading words and spelling them slowly (Windows' English voice, the phone's), spelling out
loud with Windows' English recognizer and its letter grammar and with Android's on-device recognizer (how well each
hears letter names, the microphone permission), the keyboard microphone tip, the file picker importing a page or
.txt on Windows and Android, Enter and the soft keyboard, the celebration's look and the chime; the math skill map and
mastery practice on a real screen and phone (tapping tiles and grade tabs, the number pad and soft keyboard, the time
counter, the Mastered card and its sound), and whether the score curve feels fair to real 2nd and 6th graders; Network Lab's device screens on a real screen and phone (dragging the pretend windows, the soft keyboard in their forms, the tab row scrolling sideways, tapping links in the pretend browser); Network Lab's Physical view and drawings by touch (dragging and pinching on floor plans, the port picker
and SFP module flyouts, the drawing handles with a finger, the drawing bar and the properties panel on a phone, picking
a plan picture with the file picker); Cert Lab on a real screen and phone (the terminal with the soft keyboard and its key row, the GRUB and vi type box, selecting and copying output by touch, Avalonia's real clipboard, reading aloud with the device's voice and whether the estimated sentence timing feels right, the exam timer, leaving and resuming on Android when the app goes to the background); Cyber Range on a real screen and phone (tapping dotted glossary words with a finger, the Mission panel and More flyout, typing flags with the soft keyboard, the 1.6 s celebration and the fade-ins, whether the challenges feel the right length on a phone).
