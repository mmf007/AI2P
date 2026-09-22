# Strict code review

**Pack code:** `style.strict-review` · **Records:** 9 · **Skills:** `code-review`, `code-write`

## What this style is

The discipline of reviewing someone else's work: where the review starts, what every remark
must contain and how the review ends. This methodology was picked because a review is the one
place in the process where quality rests entirely on an agreement and not on a tool: no linter
will ever notice that what was built is not what was asked for.

## Who it is for

Teams where one performer writes the code and another accepts it — especially mixed teams,
where an AI writes and a human accepts, or the other way round. If you have no review as a
separate step, add the step to the process first and install the pack second.

## What changes after the installation

* A review starts with the task and its acceptance criteria, not with the diff.
* The review opens with the list of what will break — a failure scenario with concrete inputs;
  style remarks go into a separate section afterwards.
* Every remark names a place (file, line) and a weight: blocking, important, up to the author.
* Every defect names the test that catches it — an existing one or the one to be written;
  without it the remark counts as unproven.
* The boundaries are always examined: empty input, zero, the limit, a repeated call, a
  concurrent call, an outage of an external service.
* The review is limited to the changed code; an old problem next door becomes a separate task.
* The reviewer does not edit someone else's code and ends with an unambiguous verdict:
  accepted, accepted with fixes, sent back.
* Before handing the work over, the author walks through the change and removes everything they
  cannot explain.

The pack brings a task template node, **«Review of the changes»**, with the acceptance
criterion «every defect names a failure scenario and a test».

## What is worth adjusting

* **The list of remark weights.** «Blocking / important / up to the author» is the simplest
  scale; if yours is different, rewrite the record — otherwise the performer will invent one.
* **The «do not edit someone else's code» rule.** In a small team where a fix by the reviewer
  is normal, soften the record: otherwise the agent will refuse a direct request of the author.
* **The install scope.** The rule about the verdict belongs in the organisation's general
  rules; the rest belongs in the experience of the projects that actually have a review step.
* **The record for the author** (`code-write`) is useful even on its own: it alone keeps debug
  prints and accidental reformattings out of the review.
