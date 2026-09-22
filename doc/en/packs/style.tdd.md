# Test-driven development

**Pack code:** `style.tdd` · **Records:** 10 · **Skills:** `code-test`, `code-write`,
`code-debug`, `code-refactor`

## What this style is

Development in which the test is written before the code: the red test first, then the smallest
change that turns it green, then refactoring on green. This methodology was picked because it
changes the **order of actions** of the performer rather than the shape of the report — and
that is where a bundle of text rules gives the most visible effect.

## Who it is for

Projects with executable code and at least some set of tests. If there is nowhere to run the
tests, or the run takes hours and nobody starts it, the pack turns into a bundle of wishes —
get a fast run first.

## What changes after the installation

* The test is written first and must fail for the reason it was written for; the failure
  message of the red run goes into the report as proof.
* One test — one statement; the name of the test says the condition and the expected result.
* A red test gets the smallest change; «will come in handy later» moves to the next round.
* Fixing a defect starts with a test that reproduces it.
* What is tested is observable behaviour, not the internal construction: a test must not forbid
  refactoring.
* A test is reproducible: no current time, no random numbers, no network, no dependence on the
  order of execution.
* A green run counts only together with the number of tests that passed.
* Refactoring happens on green and changes no test.
* Someone else's test that went red is investigated, not switched off silently.
* An uncovered case is named in the report on a line of its own.

## What is worth adjusting

* **The «red run in the report» requirement** is expensive where a run takes hours: replace it
  with «name the test and the reason it failed», otherwise the performer will work around it.
* **The rule about observable behaviour** is worth extending with your own list of what counts
  as observable (an HTTP response, a row in the database, a log event).
* **The rule about the number of tests that passed** is the only one in the pack that catches a
  «green run of no tests at all». If your run is filtered differently, write your own form of
  the command into the record: the rule becomes executable instead of pious.
* **The install scope.** The `code-test` records are useful across the whole organisation; the
  rule about the smallest change sometimes conflicts with projects that work in large steps —
  install that one into the experience of a particular project.
