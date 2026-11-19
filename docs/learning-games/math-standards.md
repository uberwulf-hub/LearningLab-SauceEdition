# Learning Games math and the Arizona standards (grades 2 and 6)

Researched 2026-10-24 for 2nd and 6th grade. Summaries and topic names here are our own words; only the
standard codes come from the standards. The children are never named in this repository.

## The curriculum

- **Math curriculum:** Ready / i-Ready (Curriculum Associates) is a common K-8 curriculum for the Arizona standards;
  its order of topics is below (a school's own pacing may differ).
- **Order of topics** (i-Ready Classroom Mathematics, from public crosswalks; the school's own pacing may differ):
  - Grade 6: Unit 1 area of parallelograms / triangles / polygons, nets and surface area, expressions and exponents;
    Unit 2 base-ten operations (multi-digit division, decimals), dividing fractions, volume; then ratio reasoning,
    percents, expressions and equations, positive and negative numbers, statistics.
    Sources: [Amplify crosswalk](https://amplify.com/desmos-classroom-crosswalk/i-ready-classroom),
    [EdReports grade 6](https://edreports.org/reports/detail/i-ready-classroom-mathematics-2024/sixth-grade/gateway-one).
  - Grade 2: Unit 1 numbers within 20, adding, subtracting and data; Unit 2 numbers within 100, time and money;
    Unit 3 numbers within 1,000, place value, adding and subtracting; then measuring length, shapes, arrays and
    equal parts. Sources: [Portland Public Schools grade 2](https://www.pps.net/Page/20378),
    [ST Math correlation](https://play.stmath.com/raft/resources/help/correlations/2/I-Ready_2.pdf).
- **Standards:** the **Arizona Mathematics Standards adopted December 2016** are still current. The Department's
  grade 6 document was re-issued "Updated 1-29-2025", a change to the state-test (AASA) section; every standard
  stays valid. Arizona's 2016 standards follow the Common Core layout with a few Arizona changes; the one that matters
  here is **AZ.6.NS.C.9** (converting between fractions, decimals and percents), an Arizona addition.
  Sources: [ADE mathematics standards](https://www.azed.gov/node/10495),
  [Grade 6 standards 2025 update](https://www.azed.gov/sites/default/files/2025/05/Math%20Grade%206%20Final%202025.docx),
  [Grade 6 summary of revisions (2017)](https://cms.azed.gov/sites/default/files/2017/02/Sixth%20Grade%20Summary%20of%20Revisions%208-16-17.pdf).
  Note: azed.gov refused automated reading of its PDFs, so the codes below were matched from the standards' public
  structure and search summaries, not read line by line from the Arizona document. A grown-up can check them against
  the ADE documents above.
- **Spot-check (2026-10-24, a separate reviewer).** azed.gov again refused automated reading (HTTP 403), so the codes
  were compared with a faithful third-party listing of the Arizona 2016 standards (goblins.com's Arizona grade 2 and
  grade 6 lists), the ADE revision summary's description as found in search results (AZ.6.NS.C.9 is an Arizona
  addition) and a Sadlier Arizona correlation (AZ.6.NS.C.9 = converting among fractions, decimals and percents).
  Every code in the tables and in the skills' labels exists and matches its topic. Findings:
  - Arizona splits **6.NS.B.4** into a (GCF to 100), b (LCM to 12) and c (the distributive property). The labels
    now say `gcflcm` 6.NS.B.4a-b and `distrib` 6.NS.B.4c, 6.EE.A.3.
  - **2.MD.C.8** is word problems with bills and coins written with $ and the cent sign; it does not mention making
    change. `money2`'s change questions are such word problems, so the code stands.
  - **2.G.A.1** in Arizona names attributes including faces, so `shapes2`'s cube question fits.
  - **2.OA.C.4** arrays go up to 5 by 5, as `arrays`, `blockwords` and `words2step` use.
  - The third-party grade 6 list leaves out AZ.6.NS.C.9 (it lists only the Common Core-shaped codes); the ADE
    summary and the Sadlier correlation both have it, so `fdp` keeps it.

## Grade 6

Skill ids are the ones in `Engine\Problems.cs` / `Engine\MoreProblems.cs` (the "Practice a skill" list groups them
by the area shown). "Workshop" is the Long Division Workshop page.

| Standard | Topic (our words) | Where in Learning Games |
|---|---|---|
| 6.RP.A.1 | Ratio language | `ratio` Equivalent ratios |
| 6.RP.A.2 | Unit rates | `rate` Unit rates; science stories in `sci6` |
| 6.RP.A.3a | Ratio tables, double number lines | `ratiotable` (tables and double number lines drawn) |
| 6.RP.A.3b | Unit rate problems, unit pricing | `rate`, `sci6` |
| 6.RP.A.3c | Percent of a quantity, finding the whole | `percent`, `pctwhole` (tape diagrams), `pctmoney` (discounts, tax, tips) |
| 6.RP.A.3d | Converting measurement units with ratios | `sci6` (lab measurements) |
| 6.NS.A.1 | Dividing fractions by fractions, with models and stories | `fracdiv` (models at low levels), `fracwhy` (why "multiply by the reciprocal" works, with a picture) |
| 6.NS.B.2 | Dividing multi-digit numbers with the standard algorithm | Long Division Workshop (guided and practice), `longdiv`, `divword` (what to do with the remainder) |
| 6.NS.B.3 | Adding, subtracting, multiplying, dividing multi-digit decimals | `decimals`, `sci6` |
| 6.NS.B.4a-c | GCF (a), LCM (b), the distributive property (c) | `gcflcm` (a-b), `distrib` (c) |
| 6.NS.C.5 | Positive and negative numbers in real life | `negatives`, `sci6` (dig depths, temperatures) |
| 6.NS.C.6 | Number line and the coordinate plane | `negatives`, `coords` (four quadrants), Number Line Frog |
| 6.NS.C.7 | Ordering and absolute value | `negatives` |
| 6.NS.C.8 | Distances on the coordinate plane | `coords` |
| AZ.6.NS.C.9 | Fractions, decimals and percents | `fdp` |
| (review 4-5.NF) | Equivalent fractions, simplifying, comparing, adding, multiplying, mixed numbers | `fracequiv`, `fraccompare`, `fracadd`, `fracmul`, `mixed` |
| 6.EE.A.1 | Whole-number exponents | `exponents` |
| 6.EE.A.2 | Writing, reading and evaluating expressions | `writeexpr`, `evaluate`, `exponents` (order of operations) |
| 6.EE.A.3-4 | Equivalent expressions | `distrib` |
| 6.EE.B.5-7 | One-step equations | `equations` |
| 6.EE.B.8 | Inequalities | `inequal` |
| 6.G.A.1 | Area of triangles, quadrilaterals, polygons | `area`, `polyarea` |
| 6.G.A.2 | Volume with fractional edges | `volume` |
| 6.G.A.4 | Nets and surface area | `surface` |
| 6.SP.A.2-3 | Center and spread | `stats` (mean, median, mode, range) |
| 6.SP.B.4-5 | Dot plots, box plots, describing data | `plots` |

Theme for 6th grade: uranium and the elements, terrariums, a space mining and building game (our own names, in the
spirit of App Hub's Spaceareum) and the science lab: `sci6`, the word problems in `divword`, `pctwhole`, `ratiotable`
and the Workshop's stories, and the **lab collection** reward (samples unlocked by right answers). Every science fact
used is listed in `docs\math-facts-to-check.md`.

## Grade 2

| Standard | Topic (our words) | Where in Learning Games |
|---|---|---|
| 2.OA.A.1 | One- and two-step word problems within 100 | `words2`, `blockwords`, `words2step` (blocky-world stories) |
| 2.OA.B.2 | Adding and subtracting within 20 from memory | `add20`, `sub20` |
| 2.OA.C.3 | Even and odd | `evenodd` |
| 2.OA.C.4 | Arrays | `arrays` |
| 2.NBT.A.1 | Hundreds, tens and ones | `place`, `placeblocks` (blocks drawn) |
| 2.NBT.A.2 | Skip counting by 5s, 10s, 100s | `skip` |
| 2.NBT.A.3 | Number names and expanded form | `placeblocks` |
| 2.NBT.A.4 | Comparing three-digit numbers | `compare` |
| 2.NBT.B.5-6 | Adding and subtracting within 100 | `add100`, `sub100` |
| 2.NBT.B.7, B.9 | Adding and subtracting within 1,000, regrouping | `add1000`, `regroup` (the trade drawn with blocks) |
| 2.NBT.B.8 | 10 or 100 more or less | `tenmore` |
| 2.MD.A.1-4, B.5-6 | Measuring and estimating length | `measure`, `estimate2`, Number Line Frog |
| 2.MD.C.7 | Time to 5 minutes, a.m. and p.m. | `time` (clock drawn) |
| 2.MD.C.8 | Coins and bills, making change | `money`, `money2` (bills and coins drawn), Pizza Shop |
| 2.MD.D.10 | Picture graphs and bar graphs | `graphs2` |
| 2.G.A.1 | Shapes by sides, angles and faces | `shapes2` |
| 2.G.A.2-3 | Rows and columns of squares; halves, thirds, fourths | `parts2`, Pizza Shop |

Theme for 2nd grade: a blocky building and mining world with our own words and creatures (miners, village traders,
gems, the Gloomlings that come out at night, Bouncy Blobs, Clatterbones), never another game's names or pictures;
the **build** reward places a block on the house for every right answer.
