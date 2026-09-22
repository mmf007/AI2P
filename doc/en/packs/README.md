# Experience packs: working styles

Documents of the **experience packs** — one file per **pack code** (`style.tdd.md`). The
document is opened by the **«i»** button in the pack row: **Settings → Experience → Packs**.

An experience pack is a ready-made bundle of experience records that a human installs with one
button and gets the discipline of the agents' work out of the box: how we review, how we write
tests, how we report. The pack file is `packs/<code>/pack.json` in the data directory; the
records land in the scope you choose at install time (the organisation's general rules, a
project's experience or a template node) and are removed as a whole by one button.

## Contents of the section

* [style.strict-review](style.strict-review.md) — Strict code review
* [style.tdd](style.tdd.md) — Test-driven development
* [style.research-report](style.research-report.md) — Research and reporting
* [style.experience-analysis](style.experience-analysis.md) — Experience analysis

The last one stands apart: `style.experience-analysis` is not a working style but a
task template for the periodic review of the accumulated experience.

## What all packs have in common

**Records are rules, not reasoning.** Every record is short, says one thing and changes the
behaviour of the performer. A retelling of common knowledge does not get into a pack: it eats
the experience budget of the job and crowds out what is actually needed.

**A record carries a skill.** The rule about tests reaches whoever writes tests, the rule about
reports reaches whoever writes reports. A record without a skill does not reach a job at all —
except records marked «always load», and the distribution has exactly one of those.

**Nothing project-specific.** The shipped packs name no product, no file path and no task code:
a pack is installed into any organisation. Your own, project-specific conclusions go next to
them — as ordinary project experience records.

**A pack is installed and removed as a whole.** The installation marks its records with the
service tag `pack:<code>`; removal takes exactly those and touches neither your records nor
your edits. A record you have edited stays yours: a repeated installation does not overwrite it.

## How to add a document for a new pack

Put a file `<pack code>.md` here — with exactly the code the pack carries in `pack.json`. Add
the same file in every other language: the set of documents must be the same in all languages.
