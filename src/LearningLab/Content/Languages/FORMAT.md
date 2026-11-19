# Language courses

One JSON file per language (`spanish.json`), UTF-8. Learning Lab's checks (`tests\LearningLab.Tests -Only languages`)
read every course with `LangCourse.Problems()`.

```json
{
  "format": 1,
  "language": "es",                 // the language code the lessons speak
  "name": "Spanish",
  "variant": "Latin American Spanish, as spoken in Mexico and the US Southwest",
  "voice": "es-MX",                 // the region preferred when picking a voice
  "units": [
    {
      "id": "hello",                // never changes (progress is kept by it)
      "level": 1,                   // 1 Starter, 2 Beginner, 3 Getting around, 4 Building sentences
      "title": "Hello and goodbye",
      "intro": "2-4 plain sentences for a family.",
      "items": [
        { "es": "Hola.", "en": "Hello." },
        { "es": "Estoy cansado.", "en": "I'm tired.", "note": "A girl or woman says cansada.", "lit": "optional word for word" },
        { "es": "el perro", "en": "the dog", "kind": "word" }
      ],
      "tips": [ "1-3 short notes" ]
    }
  ]
}
```

Rules:

- An item is known by its `es` and `en` together (the first 6 bytes of MD5 of `es|en`): changing either makes it
  a new item to learn, so change them only to fix a mistake. Notes and tips can change freely.
- Every `es` is unique in the file. Short (heard in the car), natural, with full accents and ¿ ¡.
- Numbers: words in `es`, digits then words in `en` (`25 (twenty-five)`; lessons read only the words).
- Every new or changed phrase is fact-checked by a second, separate pass before it ships.