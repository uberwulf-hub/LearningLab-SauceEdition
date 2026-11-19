# Languages: the plan (2026-11-11)

Andrew: "want it similar to babel and these" (Babbel, Rocket, Duolingo, Pimsleur, Language Transfer, speaking
practice apps, Dreaming Spanish, conjugation trainers). We take the **ways of teaching**, never their content, names,
characters, lesson texts or look. Everything is written for Learning Lab and fact-checked by a second pass.

## What each idea becomes here

| Idea (where it's known from) | In Learning Lab |
|---|---|
| Short lessons around a real conversation, grammar tips along the way (Babbel, Rocket) | **Lessons**: each unit has 2-4 lessons of about 10 minutes. A lesson: meet the new words, hear the conversation, practise (several kinds of exercise), a tip card, a recap. |
| Daily habit, small wins (Duolingo) | **Daily goal** (minutes or points), streak, points per lesson, a calm "you're done for today". No hearts, no penalties. |
| Hands-free audio that makes you answer (Pimsleur) | **Car lessons** get a "Conversation drill" style: the voice asks you to say something in Spanish, waits, gives the answer; earlier phrases come back at growing gaps; new words are built up from the end ("-tás ... estás ... ¿cómo estás?"). |
| Building sentences by thinking (Language Transfer) | **Think it through**: short spoken explanations in the car drills and the lessons' tip cards ("Spanish puts the 'no' right before the verb..."), then you build a new sentence from what you know. |
| Speaking with feedback (AI speaking apps) | **Say it**: speaking exercises in lessons. Where the device can recognise Spanish speech offline (Android with Google's offline Spanish; Windows with a Spanish speech pack), what you said is compared with the answer, word by word; otherwise you say it, hear the voice, and mark yourself. Nothing is sent anywhere. |
| Comprehensible input (Dreaming Spanish) | **Stories**: short graded stories (level 1-4) read aloud, sentence by sentence, tap any word for its meaning, a few questions after. Also playable in the car. |
| Conjugation drills (ConjuGato, Ella Verbs) | **Verb trainer**: present, preterite, near future (ir a) and imperative later; regular -ar/-er/-ir plus the common irregulars. **Answers are computed by a conjugator in code** (like the math in Learning Games), never typed by hand; the checks prove it against tables. |

## Content files (src\LearningLab\Content\Languages)

- `spanish.json` (format 1, unchanged): units and their items. Items keep their ids (MD5 of es|en).
- `spanish-lessons-1.json` (levels 1-2) and `spanish-lessons-2.json` (levels 3-4): the lessons (format below), referring to units by id; the app reads every `spanish-lessons*.json`.
- `spanish-stories.json`: the stories.
- Verbs: `spanish-verbs.json` lists the verbs taught (infinitive, English, group, which tenses) and only the
  irregular forms; the regular forms are made by `Engine\Languages\Conjugator.cs`.

### spanish-lessons.json

```json
{
  "format": 1,
  "language": "es",
  "lessons": [
    {
      "id": "hello-1",                 // unique, never changes (progress is kept by it)
      "unit": "hello",                 // a unit id in spanish.json
      "title": "Meeting someone",
      "goal": "Say hello, ask how someone is, say goodbye.",   // one plain sentence for the lesson card
      "minutes": 10,
      "new": ["Hola.", "¿Cómo estás?", "Bien, gracias."],        // "es" texts of items in that unit, met in this lesson
      "dialogue": [
        { "who": "Ana", "es": "¡Hola, Diego! ¿Cómo estás?", "en": "Hi, Diego! How are you?" },
        { "who": "Diego", "es": "Bien, gracias. ¿Y tú?", "en": "Fine, thanks. And you?" }
      ],
      "tip": { "title": "Tú and usted", "text": "2-5 plain sentences for a family." },
      "exercises": [
        { "kind": "choose", "prompt": "How do you say \"Thank you\"?", "answer": "Gracias.", "wrong": ["Hola.", "Adiós."] },
        { "kind": "listen", "es": "¿Cómo estás?", "answer": "How are you?", "wrong": ["What's your name?", "Where are you?"] },
        { "kind": "gap", "es": "Bien, ___. ¿Y tú?", "answer": "gracias", "wrong": ["hola", "adiós"], "en": "Fine, thanks. And you?" },
        { "kind": "build", "en": "How are you?", "es": "¿Cómo estás?", "extra": ["está", "qué"] },
        { "kind": "type", "en": "Hello.", "es": "Hola.", "accept": ["Hola"] },
        { "kind": "match", "pairs": [["Hola.", "Hello."], ["Adiós.", "Goodbye."], ["Gracias.", "Thank you."]] },
        { "kind": "say", "en": "Say: How are you?", "es": "¿Cómo estás?" },
        { "kind": "reply", "es": "¿Cómo estás?", "answer": "Bien, gracias.", "wrong": ["Me llamo Ana.", "De nada."] }
      ]
    }
  ]
}
```

Exercise kinds: `choose` (pick the Spanish for the prompt), `listen` (hear the Spanish, pick the English), `gap` (pick
the missing word), `build` (put word tiles in order; `extra` are wrong tiles), `type` (type it; accents and ¿¡ are
forgiven but pointed out; `accept` lists other right answers), `match` (pairs), `say` (speaking), `reply` (pick a
sensible answer in the conversation). People and places are made up, kind, from Arizona family life. Each lesson
has 8-14 exercises, mostly on its own new words plus a few from earlier lessons.

### spanish-stories.json

```json
{
  "format": 1,
  "stories": [
    {
      "id": "the-lost-dog",
      "level": 1,
      "title": "El perro perdido",
      "titleEn": "The lost dog",
      "after": "family",               // shown once this unit is reached (a unit id)
      "lines": [ { "es": "Lucas tiene un perro.", "en": "Lucas has a dog." } ],
      "words": { "perro": "dog", "tiene": "has (tener)" },   // tap-to-translate for words not in the units
      "questions": [ { "q": "What does Lucas have?", "answer": "A dog", "wrong": ["A cat", "A bike"] } ]
    }
  ]
}
```

Level 1 stories use only present tense and unit 1-2 words; level 4 may use the preterite and ir a. 6-20 lines each.

### spanish-verbs.json

```json
{
  "format": 1,
  "verbs": [
    { "inf": "tener", "en": "to have", "level": 1, "stem": "ie",
      "irregular": { "present": ["tengo", null, null, null, null], "preterite": ["tuve", "tuviste", "tuvo", "tuvimos", "tuvieron"] } },
    { "inf": "gustar", "en": "to please", "level": 4, "persons": ["él", "ellos"], "irregular": { } }
  ]
}
```

Forms are yo, tú, él/ella/usted, nosotros, ellos/ustedes (no vosotros). `stem`: `ie`, `ue`, `i` or `u-ue` (present
boot forms; -ir verbs also change in the preterite's él and ellos). `irregular`: 5 per tense, null = worked out by the
Conjugator. Optional `tenses` (default present and preterite; the near future is always worked out) and `persons`
(weather verbs and verbs like gustar have built-in defaults in `LangVerb.OnlyThird`).

## Built (2026-11-11)

Everything in "What each idea becomes here" exists: lessons (`LessonPage`), daily goal and Continue (`CoursePage`),
the verb trainer (`VerbPage`, `Conjugator`), stories (`StoriesPage`, `StoryPage`), speaking with offline recognition
or self-marking, the conversation drill and stories in the car (`CarPage`). The content: 71 lessons, 20 stories, 83
verbs, each written and then fact-checked separately. Imperative: later.

## Cartoons (2026-11-13)

Andrew: "a cartoon with a conversation that I have to try to understand, make the cartoons silly (animated in nature),
with about 20-30 seconds of material and I have to try to tell what's going on". `spanish-cartoons.json`: `common`
(word meanings every cartoon shares) and `cartoons`, each with `scene` (kitchen, park, beach, space, market,
classroom, desert), `cast` (2-3: `kind` cat / dog / frog / chicken / robot / cactus, `color`, `voice` 0 or 1 = the
device's chosen Spanish voice or a second one, `pitch` 0.8-1.3), `lines` (`who`, `es`, `en`, `act`: talk, wave, jump,
spin, shake, laugh, surprise, cry, dance, fall, run, eat, sleep, think, point), `words`, `gist` ("What's going on?")
and 1-3 `questions`. Rules in `LangContent.CartoonProblems` (6-14 lines, an estimated 18-34 seconds, every character
speaks, two wrong choices per question...). Drawn in code (`CartoonStage`), voiced by `LessonAudio` with each
character's voice and pitch; written, then fact-checked by a separate pass (12 cartoons, 2026-11-13).

## Order of work

1. Engine + pages: lessons player with every exercise kind, daily goal / points, the verb trainer with the conjugator,
   stories reader, speaking (recognition where available), car drill style. Checks for all of it.
2. Content: lessons for every unit (about 60), 16+ stories, the verb list. Each written, then fact-checked separately.
3. Snapshots on PC and phone sizes; release when Andrew asks.
