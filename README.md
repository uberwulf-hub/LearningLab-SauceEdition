# Learning Lab (Sauce Edition)

Learning for the whole family on **Windows and Android**, from SauceStore. It brings App Hub's learning apps into one
app ("Learning Lab SE" for short), one at a time:

| App | Status |
|---|---|
| **Spelling Bee** | New: hear a word, spell it out loud or type it; five lists of our own, your school's lists, and My Tricky Words. |
| **Learning Games** | Here: math games for 2nd and 6th grade, the Long Division Workshop, rewards, progress, six subjects with Quiz Quest, Flash Cards, Lightning Round and Memory Match, the parent view and family games on the home network. |
| **Languages** | New: Spanish (as spoken in Mexico and the US Southwest), with lessons to listen to in the car. |
| **Emulator Lab** | Here: a small RISC-V computer to look inside, with its own operating system (LabOS), games, an assembler, and the processor's registers, code and memory as it runs. |
| Network Lab | Coming |
| Cert Lab and Cyber Range | Coming |

Everything works offline. Nothing leaves the device except family games on the home network, which only start when
someone hosts or joins one.

## Home: by school subject

The home page is laid out like school: **Language Arts** (English, with the Spelling Bee practice inside), **Math** (the skill map and
practice, the math games, the Long Division Workshop), **Science**, **Social Studies** (history, US citizenship), **World Languages** (the Spanish
lessons, the Spanish quizzes) and **Computers & Technology** (coding questions, Emulator Lab; Network Lab and Cert Lab +
Cyber Range are coming). Tap a subject to see what's inside. Anything that keeps a player's progress asks "Who's
playing?" first (only when nobody is picked yet; tap your name at the top to change player). **All of Learning
Games** at the bottom of the home page opens Learning Games as before, with the family games and the parent view.

## Spelling Bee

Practice for a school spelling bee, working like Andrew's spelling practice web page:

- **Pick word lists** (tick as many as you like; each shows its number of words), **how many words** (10, 25, 50 or
  All), **mix up the order**, and **say each word automatically**.
- **Practice**: tap the big speaker to hear the word, **Say it slowly**, **Hint** (blanks and the number of letters,
  then the first letter, then the last). **Spell out loud** (say each letter: "C, A, B, I, N"; "double L", "dash" and
  "space" work too) or type it, then **Check** (or Enter).
- Capitals and accents don't count (éclair can be typed eclair); where a list gives two spellings (fertilizer /
  fertiliser) both are right; a missing space, dash or apostrophe is forgiven with a note.
- The first miss says **Not quite**: try again or show the answer. A second miss (or **I don't know**) shows the word
  with the wrong letters marked, and **Spell it for me** says the word, its letters and the word again, slowly.
- Right answers get a chime and a little celebration; the score pills, a progress bar, and Enter goes on.
- **Results**: the score, a line of encouragement, the missed words (tap the speaker to hear one spelled) and
  **Practice these again**.
- **My Tricky Words**: every word missed goes there for that player; spelling it right the first time takes it out.
- Each player has their own ticked lists, settings, tricky words and a history of scores, kept in
  `%APPDATA%\LearningLabSE\SpellingBee\players.json` (Android: the app's private storage) with five backups; a file
  that can't be read is kept aside and never saved over.

**Word lists.** Learning Lab's own five lists (Easy, Medium, Hard, Harder and Hardest Words) come with the app; a
school's study list is never bundled (it is someone else's work): add it on each device. **Add a word list** brings in more: **Import from a file** (a
spelling practice web page like Andrew's, whose word lists come in with their names; or a `.txt` / `.csv` with one
word per line, or words separated by spaces or commas, a line starting with `#` naming the list, `color|colour` for
two right spellings and `ice_cream` for a space) or **type or paste words** with a name. Added lists are kept on the
device for everyone in `SpellingBee\lists`, and can be renamed or deleted (Change).

**Voice and microphone.** Words are read by the device's own English voice (on Windows the English voice picked in
Languages > Voices when there is one). Spelling out loud uses offline speech recognition only: on Windows an
installed English speech recognizer (Settings > Time & language > Speech), on Android 12 and later the phone's
on-device recognizer (it asks for the microphone the first time). Elsewhere, on a phone, the page suggests the
keyboard's own microphone button. Nothing you say is recorded or sent anywhere.

## Math: skill map and practice

Math works like a good tutor (`docs\MATH.md` is the plan). The **skill map** (Math > Skill map and practice) shows a
grade's skills by area with their Arizona standard and the player's **mastery score** from 0 to 100: Getting started
(0-39), Building (40-69), Strong (70-89), the Challenge zone (90-99) and Mastered (100). The score rises quickly at
first and slowly near the top; a miss costs little at the start and more in the Challenge zone, and the questions get
harder as the score rises. A player who gets about 9 in 10 right masters a skill in about 15-25 questions; about 7 in
10 right reaches Strong but rarely Mastered. Grades 2 and 6 have skills now; the other grades say "coming".

**Practice** asks one skill's questions (typed with a number pad, or picked, with a picture where it helps) and shows
the score ring, the questions answered and the time. A wrong answer shows **Not quite**, the right answer and **Learn
from it**: a worked explanation of exactly that question, step by step with its own numbers. **Got it** carries on.
Reaching 100 celebrates and gives bonus reward points; a mastered skill stays mastered. **Up next** (on the skill map
and the Math page) suggests the skills started but not mastered, then the next new skills in standard order. Scores
are kept with the player in `profiles.json`.

## Learning Games

Pick a player (their age picks where each subject starts), then a subject. Math has five games and a practice page
for every skill (Arizona standards for 2nd and 6th grade), pictures for each kind of
problem, hints that teach, and rewards: a block build for younger players, a science lab for older ones. The other
subjects use the question banks in `src\LearningLab\Content\LearningGames` (format in `FORMAT.md` there). Grown-ups
get the parent view: everyone's progress, and the family room where kids' devices join so the parent sees their
answers live, sends hints and starts a race or a boss battle for everyone.

On Windows, the first start copies App Hub's Learning Games players (`%APPDATA%\LearningGames\profiles.json`) and
leaves App Hub's copy alone. Players are kept in `%APPDATA%\LearningLabSE\LearningGames\profiles.json` (Android: the
app's private storage), with the five versions before each save kept as backups; a file that can't be read is copied
aside and never saved over.

The block games (Block Quest, Mob Siege, Word Obby) are Windows only for now: they open in a Microsoft Edge app window
with their own profile in `%LOCALAPPDATA%\LearningLabSE\WebGames`.

Family games use App Hub's network format (discovery on UDP 47800, games on TCP 47801), so phones, Learning Lab on a
PC and App Hub's Learning Games can play together. On Windows, the first time someone hosts, Windows may ask whether
to let Learning Lab through the firewall.

## Languages

Spanish to start: 24 units in four levels (Starter, Beginner, Getting around, Building sentences), 509 short
everyday phrases with notes on what's different for a girl or a boy, formal or friendly, Mexico or elsewhere. Each
unit has its phrases to hear, flashcards (what you didn't know comes back sooner, what you knew comes back later), a
quiz by ear and by sight, and a spoken lesson.

**Lessons** (71, two to four per unit, about 10 minutes each): the course page is a path of the units with their
lessons (done, next, not started) and a big **Continue**. A lesson: meet the new phrases (tap to hear), a short
conversation line by line (tap for the English), then exercises one at a time: pick the Spanish, listen and pick the
meaning, fill the gap, put word tiles in order, type it (missing accents and ¿ ¡ are forgiven but pointed out: "Watch
the accent: está"), match pairs, say it, and choose a reply. The right answer is always said out loud; anything wrong
comes back once at the end. A tip card and a recap with the points finish it.

**Daily goal:** points for lessons (one per exercise right the first time, five for finishing), flashcards, quizzes,
stories, the verb trainer and car lessons; a ring shows today's points against the goal (Settings: 10, 20, 30 or 50
a day) beside the streak. No hearts, no penalties.

**Verb trainer:** 83 everyday verbs in the present, the preterite and the near future (ir a): "nosotros · comer ·
preterite", typed or picked from four, then the whole tense with the irregular forms marked. The forms are worked out
by the app's own conjugator; only the irregular ones are written in the verb list. Weather verbs and verbs like gustar
are only asked in the forms people use (llueve; gusta, gustan).

**Stories:** 20 short graded stories (levels 1-4) that open as you reach their unit: a line at a time or "Read it all",
every word can be tapped for its meaning, a line's English on a tap, and a few questions after.

**Say it:** where the device can recognise Spanish speech offline, "Tap and speak" checks what you said word by word
(Windows: an installed Spanish speech recognizer, Settings > Time & language > Language & region > Spanish > Speech;
Android 12 and later: the phone's on-device recognizer, asked for the microphone only when you first tap it).
Everywhere else: say it out loud, listen, and mark yourself. Nothing you say is recorded or sent anywhere.

**In the car:** a lesson talks you through it: the English, then the Spanish twice (the second time slower) with time
to repeat it out loud; or "Test yourself" (the English, a pause to say it yourself, then the Spanish); or "Listen and
understand"; or a **Conversation drill** built from the unit's lessons ("How would you say...?", a pause, the answer;
long phrases built up from the end, "tás... estás... ¿cómo estás?"; earlier phrases come back at growing gaps; each
lesson's tip read first); or a story read a line at a time with its English. Pick the speed and how long the pauses are. Set it going before you drive: on a phone it keeps playing
with the screen off and through the car when the phone is connected, and "Save this lesson as an audio file" puts it
in Music > Learning Lab for any music player.

**Voices** are your device's own: free, offline, nothing to download from us. On Windows add a Spanish voice in
Settings > Time & language > Speech > Add voices (Spanish (Mexico) or Spanish (United States)). On Android, in the
phone's text-to-speech settings, "Speech Services by Google" > Install voice data > Spanish. Languages > Voices opens
those settings and lets you pick and try each voice. Progress is kept in %APPDATA%\LearningLabSE\Languages\
progress.json (safe saves, like the players); spoken lessons are kept for next time in
%LOCALAPPDATA%\LearningLabSE\Languages (made again whenever they're missing).
## Emulator Lab

A small computer made of software, to learn how computers work: a **RISC-V processor** (32-bit, RV32IM + Zicsr,
machine mode), 16 MB of memory, a screen (an 80 x 25 text console over 320 x 200 graphics), a keyboard, a disk and a
clock, running **LabOS**, a tiny operating system written in RISC-V assembly for it, with a shell, a file system and
four demos (`run hello`, `run bounce`, `run snake`, `run snake demo`, `run guess`). Every byte the machine runs is
written here (the boot ROM, LabOS and the programs are assembly files, assembled when Emulator Lab opens); nothing is
downloaded. The machine, LabOS and the shell commands are App Hub's, unchanged: its README describes the memory map,
the system calls and the assembler.

- **Computer**: the monitor (tap or click it, then type: the keys go to the computer; on a phone, the box under it
  takes the phone's keyboard), a key row with Esc, Tab, Ctrl, Ctrl+C, the arrows, Backspace, Enter and F1-F10, the
  power and disk lights, and *What's happening?* in a sentence or two.
- **Control bar**: Power, Run / Pause (F5 / Shift+F5), Step (F10), Step over (F11), Reset, Look inside (F12), the
  speed (1 instruction a second to full speed) and what the screen shows. On a phone, Reset, speed and screen are
  behind the `...` button.
- **Look inside**: the processor's registers (the ones that just changed light up) and its control registers, the
  code around pc with labels and source lines (double-click or double-tap a line for a breakpoint), any part of
  memory and the stack, and every trap. Beside the monitor on a wide window, its own tab on a phone.
- **Assembler**: an editor with colours and line numbers; Examples (small lessons and the LabOS demos), New, Open,
  Save as; Assemble, Assemble & run (saves the program on the LabOS disk and types `run <name>`), Run without LabOS,
  Save to LabOS disk. Problems are listed with their line; tap or click one to go there.
- **What's happening?** (Learn on a phone): short explanations of the computer, the processor, registers, memory,
  instructions, traps, LabOS, and things to try.

Settings and the program in the editor are kept in `%APPDATA%\LearningLabSE\EmulatorLab\settings.json` (Android: the
app's private storage), the LabOS disk in `disk.img` next to it. Both are kept safe as in App Hub: five backups
(`settings.json.bak1`..`bak5`; the disk is copied to `disk.img.bak1`..`bak5` when it changed), and a file that can't
be read is copied aside (`<name>.unreadable-<time>`) and never written over, so LabOS never formats over a damaged
disk. On Windows, the first start copies App Hub's Emulator Lab settings and disk (`%APPDATA%\EmulatorLab`) and leaves
App Hub's copy alone. The assembly sources are unpacked into `%LOCALAPPDATA%\LearningLabSE\EmulatorLab\sources` (made
again when missing).

## Building

```
dotnet run -c Release --project tests\LearningLab.Tests             # every check (about 3 million, ~10 s)
dotnet run -c Release --project tests\LearningLab.Tests -- -Only games-pages -Snapshot build\snap
dotnet run --project src\LearningLab.Desktop                          # the Windows app
dotnet build src\LearningLab.Android -c Debug                          # the Android app (needs the .NET Android workload and SDK platform 36)
```

Releases are made on GitHub, like Bored Games: set the version (`Directory.Build.props` and the Android project), move CHANGELOG.md's Unreleased lines under the new version, push, then Actions > Release > Run workflow with `vX.Y.Z`. It runs the checks, builds the Windows zip and the signed Android APK, and publishes the release SauceStore lists. The GitHub Action (`.github\workflows\build.yml`) runs the checks and builds test copies on every push.

## Layout

```
src\LearningLab.Engine     the apps' engines, no UI (Learning Games' problems, long division, network; Languages;
                           Emulator Lab's machine, assembler and LabOS sources; Spelling Bee's lists and
                           checking; safe saves)
src\LearningLab            the screens, shared by Windows and Android (Avalonia): Views\ (shell, home by subject),
                           LearningGames\, Languages\, EmulatorLab\, SpellingBee\
src\LearningLab.Desktop    the Windows app (sounds, Windows voices, the block games)
src\LearningLab.Android    the Android app (sounds, the phone's voice)
tests\LearningLab.Tests    the checks and page pictures (headless)
docs\PLAN.md               the plan for moving the apps over
```

## Licence

All rights reserved: the code is public to read, not to reuse. See [LICENSE](LICENSE) and [CREDITS.md](CREDITS.md).
