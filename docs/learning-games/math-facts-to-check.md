# Facts used in the math problems and rewards (fact-checked)

Every fact below is written into Learning Games' math (`Engine\MoreProblems.cs`, `Engine\Rewards.cs`, `MathPlus.ps1`).
Numbers that are made up for a problem are marked "pretend" or "practice problem" in the problem itself and are not
listed. Any new fact must be added here and checked by a separate pass before it ships.

**Fact-check pass: 2026-10-24** (a separate reviewer, not the writer). Each line says "Checked" with its source, or
"Changed" with what was wrong and what it says now. Sources used (read online, nothing downloaded):

- [N1] NRC, Uranium enrichment: natural uranium "approximately 99.3% uranium-238, 0.7% uranium-235", power-reactor
  fuel "enriched to 3 to 5%": https://www.nrc.gov/materials/fuel-cycle-fac/ur-enrichment.html
- [N2] EIA, Nuclear explained: the nuclear fuel cycle (U-235 "just over 0.7%" of natural uranium, enriched to
  "3%-5%"): https://www.eia.gov/energyexplained/nuclear/the-nuclear-fuel-cycle.php
- [N3] NRC, Fuel fabrication (UO2 powder pressed into pellets, loaded into Zircaloy tubes, made into assemblies):
  https://www.nrc.gov/materials/fuel-cycle-fac/fuel-fab.html
- [N4] NRC Science 101, What is a Geiger counter (clicks show radiation entering the tube; some background clicking is
  always there): https://www.nrc.gov/reading-rm/basic-ref/students/science-101/what-is-a-geiger-counter.html
- [P1] IUPAC periodic table (atomic numbers, symbols): https://iupac.org/what-we-do/periodic-table-of-elements/ and
  NIST, Atomic weights and isotopic compositions (mass numbers of the isotopes):
  https://physics.nist.gov/cgi-bin/Compositions/stand_alone.pl
- [P2] Los Alamos National Laboratory periodic table, element pages (e.g. uranium https://periodic.lanl.gov/92.shtml,
  neon https://periodic.lanl.gov/10.shtml); New Mexico Tech, uranium FAQ ("the heaviest naturally-occurring
  element"): https://geoinfo.nmt.edu/faq/energy/uranium/home.html; ACS Molecule of the Week, neon (neon's own glow
  is red-orange; other sign colours come from other gases): https://www.acs.org/molecule-of-the-week/archive/n/neon.html
- [P3] Royal Society of Chemistry periodic table, element pages (uses and properties of H, He, C, N, O, Na, Al, Si,
  Ca, Fe, Cu, Ag, Au, Pb, U): https://www.rsc.org/periodic-table
- [A1] UCAR Center for Science Education, What's in the air (about 78% nitrogen, 21% oxygen): https://scied.ucar.edu/learning-zone/air-quality/whats-in-the-air
- [S1] Textbook biology and terrarium-keeping references (mosses have rhizoids, no true roots, no flowers; ferns
  reproduce by spores; springtails eat mold and fungi; isopods are crustaceans that eat decaying leaves; NIST SI
  units for 1 L = 1,000 mL and water freezing at 0 deg C at normal pressure).
- [M1] Everyday sizes: Crayola standard crayon 3 5/8 in; a new No. 2 pencil about 7.5 in; a No. 1 paper clip about
  1 5/16 in (33 mm); US interior door 80 in (2.03 m); US letter paper 8.5 x 11 in; adult baseball bats 32-34 in; US
  football field 100 yd between goal lines (NFL rules); full-size school buses about 35-45 ft.
- [STD] For the standards file see `math-standards.md`.

## 6th grade science stories (`Sci6`, `StoryFor` in Engine\MoreProblems.cs)

| # | Fact as used | Where | Check |
|---|---|---|---|
| 1 | Uranium's atomic number is 92 (92 protons). | Sci6 neutrons / protons, element tile | Checked [P1] |
| 2 | Uranium-238 and uranium-235 are isotopes of uranium; the mass number counts protons plus neutrons (U-238: 146 neutrons, U-235: 143). | Sci6 | Checked [P1] |
| 3 | Carbon: atomic number 6; carbon-12 and carbon-14 are its isotopes (6 and 8 neutrons). | Sci6 | Checked [P1] |
| 4 | Oxygen: atomic number 8; oxygen-16 (8 neutrons). | Sci6 | Checked [P1] |
| 5 | Iron: atomic number 26; iron-56 (30 neutrons). | Sci6 | Checked [P1] |
| 6 | Gold: atomic number 79; gold-197 (118 neutrons). | Sci6 | Checked [P1] |
| 7 | Lead: atomic number 82; lead-208 (126 neutrons). | Sci6 | Checked [P1] |
| 8 | The atomic number is the number of protons in each atom of the element. | Sci6 | Checked [P1] |
| 9 | Half-life: after each half-life, the amount left is cut in half. (The half-lives in the problems are pretend.) | Sci6 | Changed: the story said "every h days, half of it is left", which a child can read as half of the starting amount each time; now "every h days, the amount left is cut in half" (the answers were already start / 2^k; definition of half-life as in the NRC glossary). |
| 10 | Natural uranium is about 99.3% uranium-238 and about 0.7% uranium-235. | Sci6 | Checked [N1], [N2] (0.72% U-235, a trace of U-234) |
| 11 | Fuel for many nuclear power plants is enriched so that about 3% to 5% of its uranium is uranium-235. | Sci6 | Checked [N1], [N2] (light-water reactors) |
| 12 | Nuclear fuel is made of small pellets stacked inside long metal tubes called fuel rods. | StoryFor (the pellet counts per rod are pretend: a real rod holds a few hundred) | Checked [N3] |
| 13 | A Geiger counter clicks each time it detects radiation. | Sci6 (the 12-40 clicks a minute near a rock sample are an ordinary background-like count) | Checked [N4] |
| 14 | 1 liter = 1,000 mL. | Sci6 | Checked [S1] |
| 15 | Water freezes at 0 degrees Celsius (used as "below 0 °C" for a lab freezer). | Inequal | Checked [S1] |
| 16 | A terrarium's bottom layers: gravel (drainage), then soil; plants on top. | Sci6, StoryFor | Checked [S1] |
| 17 | A salt-water solution that is 5% salt by mass has 5 g of salt in every 100 g of solution. | Sci6 (percent concentration) | Checked (definition of mass percent) |
| 18 | Below the surface is written with negative numbers (sea level / the surface = 0). | Sci6, WriteExpr, Inequal | Checked (convention, 6.NS.C.5) |
| 18a | Test tubes hold up to about 40 mL; bigger amounts go in a beaker. | StoryFor (added in this pass: "test tube" for 40 mL or less, else "beaker"); Sci6 decimal story now says "test tube" for 4-20 mL instead of a "beaker" holding 5.6 mL | Checked (common lab sizes: test tubes about 5-40 mL) |

## 2nd grade measuring (`Lengths` in Engine\MoreProblems.cs): typical sizes for "about how long"

| # | Fact as used | Check |
|---|---|---|
| 19 | A new crayon is about 4 inches long. | Checked [M1] (3 5/8 in) |
| 20 | A new pencil is about 7 inches long. | Checked [M1] (about 7.5 in) |
| 21 | A paper clip is about 3 centimeters long. | Checked [M1] (No. 1 clip 33 mm) |
| 22 | A school bus is about 40 feet long. | Checked [M1] (full-size 35-45 ft) |
| 23 | A door is about 2 meters tall. | Checked [M1] (80 in = 2.03 m) |
| 24 | A sheet of notebook paper is about 11 inches on the long side. | Checked [M1] |
| 25 | A baseball bat is about 1 yard long. | Checked [M1] (32-34 in; 36 in = 1 yd) |
| 26 | A blueberry is about 1 centimeter across. | Checked (cultivated blueberries about 1-1.5 cm) |
| 27 | A football field is about 100 yards from one goal line to the other. | Checked [M1] |
| 28 | A child's hand is about 3 inches wide. | Checked (a 7-8 year old's palm is about 2.5-3 in wide) |
| 29 | It takes more inches than feet (more centimeters than meters) to measure the same thing. | Checked (2.MD.A.2) |
| 30 | A paper clip is a little longer than an inch (about 3 centimeters); a centimeter is about as wide as the nail on your little finger (hint text). | Changed: the hint said "an inch is about as wide as a big paper clip" (a paper clip is under half an inch wide; it is its length that is about an inch) and "a centimeter is about as wide as your finger" (a finger is 1.5-2 cm wide). Now it uses the paper clip's length and the little fingernail, the usual classroom benchmarks, and agrees with fact 21. [M1] |
| 31 | 12 inches = 1 foot, 3 feet = 1 yard, 100 centimeters = 1 meter (older questions, kept). | Checked [S1] |

## 2nd grade shapes (`Shapes2`, `Parts2`) and 6th grade nets (`Surface`)

| # | Fact as used | Check |
|---|---|---|
| 31a | Triangle 3 sides, quadrilateral 4, pentagon 5, hexagon 6; a flat shape has as many corners as sides; a cube has 6 faces; a triangular prism and a square pyramid each have 5 faces. | Checked (geometry definitions) |
| 31b | A square has 4 equal sides and 4 square corners. | Changed: "rectangle" was offered as the wrong answer, but a square is a rectangle too; the wrong choices are now pentagon, triangle and hexagon, and the hint says "A square is a special rectangle." |

## Rewards: the lab sample cards (`Lab` in Engine\Rewards.cs)

Symbols and atomic numbers on every card were checked against [P1]: H 1, He 2, C 6, N 7, O 8, Ne 10, Na 11, Al 13,
Si 14, Ca 20, Fe 26, Cu 29, Ag 47, Au 79, Pb 82, U 92.

| # | Fact as used | Check |
|---|---|---|
| 32 | Hydrogen (H, 1): the lightest element; stars like the Sun are mostly hydrogen. | Checked [P3] (the Sun is about 73% hydrogen by mass) |
| 33 | Gravel: the bottom layer of a terrarium; extra water drains into it so roots don't get soggy. | Checked [S1] |
| 34 | Helium (He, 2): lighter than air, so helium balloons float. | Checked [P3] |
| 35 | Carbon (C, 6): found in every living thing; diamonds and graphite (pencil "lead") are both carbon. | Checked [P3] |
| 36 | Charcoal: a thin layer of activated charcoal can help keep a closed terrarium smelling fresh. | Checked (common terrarium-keeping advice; worded "can help", which is fair) |
| 37 | Nitrogen (N, 7): about 78% of the air. | Checked [A1] |
| 38 | Oxygen (O, 8): about 21% of the air; plants give it off when they make food from sunlight. | Checked [A1], [S1] |
| 39 | Soil: plants take in water and nutrients through their roots. | Checked [S1] |
| 40 | Neon (Ne, 10): glows red-orange in neon signs. | Checked [P2] |
| 41 | Sodium (Na, 11): table salt is sodium and chlorine joined together. | Checked [P3] |
| 42 | Moss: has no roots or flowers; soaks up water like a sponge. | Checked [S1] (rhizoids, not true roots) |
| 43 | Aluminum (Al, 13): light and doesn't rust; used for cans and foil. | Checked [P3] (rust is iron oxide; aluminum's thin oxide layer protects it) |
| 44 | Silicon (Si, 14): sand is mostly silicon and oxygen; computer chips are made from silicon. | Checked [P3] (most sand is quartz, silicon dioxide) |
| 45 | Fern: grows from tiny spores instead of seeds. | Checked [S1] |
| 46 | Calcium (Ca, 20): helps build strong bones and teeth. | Checked [P3] |
| 47 | Iron (Fe, 26): Earth's core is mostly iron; iron in blood carries oxygen. | Checked [P3] (iron-nickel core; hemoglobin) |
| 48 | Springtails: tiny helpers that eat mold, keeping a terrarium clean. | Checked [S1] |
| 49 | Copper (Cu, 29): carries electricity well, used for wires. | Checked [P3] |
| 50 | Silver (Ag, 47): conducts electricity better than any other metal. | Checked [P3] (highest electrical conductivity of any element) |
| 51 | Isopods: little crustaceans (cousins of crabs) that recycle dead leaves. | Checked [S1] |
| 52 | Gold (Au, 79): doesn't rust or tarnish, so old gold coins still shine. | Checked [P3] |
| 53 | Lead (Pb, 82): very dense; lead aprons block X-rays at the dentist. | Checked [P3] |
| 54 | Glass lid: in a closed terrarium, water evaporates, collects on the glass and drips back down (a small water cycle). | Checked [S1] |
| 55 | Uranium (U, 92): the heaviest element found in nature in large amounts; power plants use it as fuel; it is radioactive, so it is kept in labelled containers. | Checked [P2] (trace plutonium exists in nature, hence "in large amounts"), [N1] |

## Story realism changes made in the same pass

- Remainder stories (`StoryFor`) now pick a story that fits the size of the number: pebbles and rock samples only up
  to 3,000, water and seeds up to 20,000, ore and fuel pellets for bigger numbers (a class doesn't collect 86,000
  pebbles).
- `PctWhole`: "92 cups of compost per 100 cups of mix" became a seed test ("30 out of every 100 seeds sprouted"),
  which makes sense for any rate; "a terrarium's water tank" became "a plant mister bottle".
- `PctMoney`: each thing has its own price range (a poster $8-$25, a telescope $60-$200, ...), so no $114 poster.
- `Money2`: things bought with a $1 bill are small (an eraser, a pencil, a sticker...), not a block-building set
  for 65 cents.

## Wording rules followed

- Radiation is described calmly (Geiger counters, labelled containers, fuel); no weapons anywhere.
- The space-mining stories are "a space mining game" with our own words (rovers, smelters, ore, iron bars); no
  Terraria / Re-Logic names. The blocky world uses our own creatures (Gloomlings, Bouncy Blobs, Clatterbones), no
  Minecraft / Mojang names. (Checked in this pass by reading generated problems at every level of every new skill:
  no trademark names, no weapons; the toy pickaxe and torches are tools, the creatures only "hop away" or knock
  blocks off.)
