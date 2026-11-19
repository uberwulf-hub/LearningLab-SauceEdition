# Math: the plan (2026-11-11)

Andrew: "Extra focus areas on this for them is Math. Can we make as good as this?" (IXL). We take the **ways of
teaching** that make IXL work, never its skill names, codes, question texts, explanations, art or look. Every question
is made by code and its answer computed (as Learning Games already does), so nothing can be wrong by typing.

Today: Learning Games has about 51 skills for 2nd and 6th grade (Arizona 2016 standards as taught with
Ready / i-Ready, `docs\math-standards.md`), levels per skill, pictures (`Visuals.cs`), the Long Division Workshop,
games and rewards.

## What makes IXL good, and what it becomes here

| IXL idea | In Learning Lab |
|---|---|
| A big skill map for every grade, by strand (numbers, operations, fractions, geometry, measurement, data, ...) | **Math skill map**: grades K-8 by strand, each skill a tile with its standard, a short name and the player's score. Start with grades 1-7 around the boys' grades (2nd and 6th), the rest later. |
| SmartScore 0-100: rises with right answers, drops on misses, slows near the top; 80 = excellent, 90-100 = the Challenge Zone where one miss costs more; 100 = mastered | **Mastery score** with our own curve and names (Getting started / Building / Strong / Challenge zone / Mastered), harder questions as the score rises, shown as a ring. Tuned by simulation in the checks so a steady learner masters a skill in about 15-25 questions. |
| After a wrong answer: the right answer and a worked, step-by-step explanation of *this* question | **Learn from it**: every generator also produces its own worked steps for the exact numbers asked (Learning Games' hints and Long Division steps already do this for some skills), plus the picture where one helps. |
| Questions answered, time spent, streaks | Per skill and per day, kept per player. |
| Diagnostic: finds the level in each strand and recommends skills | **Find my level**: a short adaptive check per strand (about 10-15 questions) that places the player and suggests the next skills. |
| Recommendations | **Up next**: skills just above what's mastered, the ones started but not finished, and ones with misses to review. |
| Awards, certificates, a prize board | Reuses Learning Games' rewards (block build / science lab) and adds a medal per mastered skill. |
| Parent / teacher analytics: trouble spots, questions missed | **Parent view**: per player, skills practised, mastery, time, and the questions missed most with their explanations. |
| Answer types: number pad, fractions, multiple choice, drag, graphs | Number pad (exists), fractions (num/den boxes), multiple choice, comparison (<, =, >), pictures from `Visuals.cs`, tables, simple graphs and number lines drawn in code. |

## Rules

- Generators in `Engine\LearningGames\` (or a new `Engine\Math\`), each with: grade, strand, standard, difficulty 1-5,
  the question, the computed answer, accepted answer forms, and worked steps. The checks generate thousands of each
  at every difficulty and re-derive every answer independently (as Learning Games' `-Test` did).
- Standards follow Arizona's (Ready / i-Ready order) for the grades covered; new skills are listed
  in `docs\math-standards.md`.
- Never a real child's name in the repository.

## Order of work

1. Mastery score engine + the skill map + the practice page with worked explanations, on the existing 2nd and 6th
   grade skills (fastest value for the kids).
   **Status (2026-11-11): done.** `Engine\LearningGames\Mastery.cs` (curve tuned by simulation: 90% right masters in a
   median of 18 questions, 70% right reaches Strong but masters within 30 questions only 15% of the time), the skill map
   (grades 2 and 6; 1, 3, 4, 5, 7 say "coming"), practice with Learn from it, Up next on the map and the Math page, scores
   in profiles.json. Checks: group `math-mastery`.
2. Worked steps for every existing skill; more answer types.
   **Status (2026-11-11): worked steps done** for all 57 existing 2nd and 6th grade skills (`Engine\LearningGames\Worked.cs`
   and `Problem.Steps`; every sum in them recomputed by the checks for 2,000 seeds). **Still to do:** more answer types
   (fraction boxes with a top and a bottom, drag, number lines and graphs to tap), and the per-day time and streaks per
   skill (time per skill is kept; days are kept per player, not per skill yet).
3. Grades 1, 3, 4, 5, 7 skills; Find my level; Up next; parent view analytics.
   **Status (2026-11-11):** Up next is done (started-not-mastered first, then new skills in standard order; skills with
   recent misses to review are not picked out yet). Still to do: grades 1, 3, 4, 5, 7, Find my level, the parent view
   analytics, and a medal per mastered skill on the rewards page.

## Backlog: what the kids' school focuses on (Andrew, 2026-11-13)

The school teaches the Arizona K-12 Mathematics Standards with Ready / i-Ready Mathematics and Beyond
Textbooks. Andrew's list of each grade's focus is the main target for new math work: every focus below should have
skills on the skill map, practice with Learn from it, and a place in Up next. Same rules as above: our own wording and
look, never Ready / i-Ready's names, questions, art or lessons. "Have" = skills that already cover it; "Add" = to build
(the generator, its worked steps and its checks, as in "Order of work" 2).

**Grade 2** (key milestone: regrouping in 3-digit adding and subtracting)
- Place value and base ten to 1,000: read, write (digits, words, expanded form), count, hundreds / tens / ones,
  compare three-digit numbers. Have: place, placeblocks, skip, compare, tenmore, evenodd.
- Adding and subtracting: facts within 20 from memory, one- and two-step word problems, to 1,000 with models and
  regrouping. Have: add20, sub20, add100, sub100, add1000, regroup, words2, words2step, blockwords.
- Measurement and data: length in inches, feet, centimetres and metres; time to the nearest 5 minutes; bills and coins;
  bar graphs and line plots. Have: measure, estimate2, time, money, money2, graphs2. **Add: line plots** (2.MD.D.9:
  measure several things, mark them on a line plot, read it back).
- Geometry: name and draw triangles, quadrilaterals, pentagons, hexagons; split circles and rectangles into halves,
  thirds and fourths. Have: shapes2, parts2, arrays. **Add: draw a shape** with given sides or corners (tap the dots),
  once tapping answers exist.

**Grade 6** (key milestone: writing and solving one-step equations)
- Ratios and proportional relationships: ratios, unit rates, percent of a quantity, converting measurement units.
  Have: ratio, rate, ratiotable, percent, pctwhole, pctmoney, fdp. **Add: converting units with ratios** (6.RP.A.3d:
  inches / feet / yards / miles, cups / quarts / gallons, metric).
- The number system: dividing fractions by fractions, multi-digit decimals, negative numbers on a number line and the
  four-quadrant plane. Have: fracdiv, fracwhy, mixed, decimals, negatives, coords, gcflcm. **Add: multi-digit
  division and decimals fluency** (6.NS.B.2-3, the standard algorithm with worked steps) and **ordering rational
  numbers and absolute value on a number line** (6.NS.C.6-7, tap the line).
- Expressions and equations: write, read and evaluate expressions with variables and exponents; one-variable
  equations and inequalities; dependent and independent variables. Have: exponents, evaluate, writeexpr, distrib,
  equations, inequal. **Add: dependent and independent variables** (6.EE.C.9: a table, an equation and a graph of the
  same story).
- Geometry: area of triangles, special quadrilaterals and polygons by composing / decomposing; volume of prisms with
  fractional edges; surface area from nets. Have: area, polyarea, volume (fractional edges included), surface.
  **Add: polygons on the coordinate plane** (6.G.A.3: side lengths from coordinates).
- Statistics and probability: statistical questions, distributions, mean and median, interquartile range and mean
  absolute deviation. Have: stats, plots (box plots and IQR). **Add: statistical questions** (6.SP.A.1: which
  questions expect varied answers) and **mean absolute deviation** (6.SP.C.5c, worked steps that show every distance).
- Accelerated / honors track: some 7th grade standards come early, mainly proportional equations (7.RP.A.2) and
  adding, subtracting, multiplying and dividing negative integers (7.NS.A.1-2). These go with grade 7 on the skill map.

Order: the grade 6 gaps first (the equations milestone and the units the class is on now), then grade 2's line plots,
then the accelerated grade 7 skills.
