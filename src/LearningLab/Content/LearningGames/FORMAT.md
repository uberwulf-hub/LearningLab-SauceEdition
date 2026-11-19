# Learning Games question bank format

One UTF-8 JSON file per subject (no BOM). Shape:

```json
{
  "id": "history",
  "name": "History",
  "levels": {
    "prek":    [ { "name": "Topic name", "questions": [ Q, Q, ... ] }, ... ],
    "k2":      [ ... ],
    "g35":     [ ... ],
    "g68":     [ ... ],
    "g912":    [ ... ],
    "college": [ ... ],
    "adult":   [ ... ]
  }
}
```

Levels and who they are for:

| key | who | ages |
|---|---|---|
| prek | Pre-K | 3-4 |
| k2 | Kindergarten to 2nd grade | 5-7 |
| g35 | 3rd to 5th grade | 8-10 |
| g68 | 6th to 8th grade | 11-13 |
| g912 | High school | 14-17 |
| college | College | 18-22 |
| adult | Graduate level and adult refresher | 23+ (a grown-up brushing up: the things adults forget, plus deeper material) |

A question (Q):

```json
{ "q": "Question text", "choices": ["right answer", "wrong", "wrong", "wrong"], "answer": 0, "explain": "One or two sentences on why, shown after answering." }
```

- `choices`: 2 to 4 options. The app shuffles them, so put the correct one anywhere; `answer` is its index. For true/false use exactly `["True", "False"]` (not shuffled).
- `"tip": "..."` (strongly wanted on most questions): a short, fun MEMORY TRICK that makes the answer stick: a mnemonic,
  rhyme, acronym, vivid picture, story, joke or link to something familiar (e.g. "Columbus sailed the ocean blue in
  fourteen hundred ninety-two", "Never Eat Soggy Waffles", "ESTAR is for how you feel and where you are"). Shown
  with the explanation and again when the question comes back. Keep it age-appropriate for the level; one sentence.
- Optional `"code": "..."`: a code snippet shown in a monospace box under the question (Coding). Use `\n` for new lines.
- Optional `"say": "..."`: text the app reads aloud instead of `q` (for Pre-K and K-2, write questions that make sense read aloud).
- Optional `"passage": "..."` with `"passageTitle": "..."`: a short story or article shown above the question in a
  paper-colored box (reading comprehension). Each question about the story repeats the same passage and title; write
  questions that can be answered from the passage alone. The R key reads the story aloud; automatic read-aloud reads
  only the question. Memory Match leaves these questions out.
- Optional `"lang": "es"`: the question is about a Spanish word; the app reads Spanish words with a Spanish voice when one is installed.
- Plain text only (no Markdown, no HTML). Keep questions short; Pre-K and K-2 use very simple words.
- Every fact must be correct and stable over time. Do not ask about who currently holds an office, current prices, populations or other things that change. If unsure of a fact, leave the question out.
- Wrong choices should be plausible but clearly wrong to someone who knows the material; never two right answers.
- Avoid politically or religiously charged framing; stay factual and age-appropriate.

A bank may leave out levels it has no questions for yet: the app offers only the levels that have questions and
puts each player on the nearest one. (All six banks have all seven levels today.)

Size: at least 4 topics per level, about 8-12 questions per topic (Pre-K: 3-4 topics, 6-8 questions each).

Check a bank with `tools\Test-LearningGamesBanks.ps1` (all banks) or `tools\Test-LearningGamesBanks.ps1 -Path
Content\science.json`: JSON without a BOM, required fields, 2-4 distinct choices with the answer among them, no
repeated question at a level, topic sizes.
