# Project objects

**An object is what a task refers to:** a character of a clip, a location, a prop, a style, a
reference frame, a LoRA adapter. Every project has its own list of objects; the organization has
no common object list on purpose — an object belongs to a project.

The list is opened by the **"Objects"** tab of the project card. A click on a row opens the
**"Object" tab** — a card with tabs, the same as a task has.

---

## What they are for

This is easiest to explain with the constant character of a video clip. If you describe the
hero's appearance in your own words in every frame, he "drifts" from frame to frame — **the
retelling is the cause**.

That is why an object has a **passport** — verbatim text for the prompt — and reference files,
and the task description carries a **reference `@obj:OBJ-3`**. On **every** start of a job the
reference expands into the passport and the file paths: fix the passport and the very next frame
takes the fix into account, there is no need to rewrite the tasks.

Three rules about the reference that save time:

* **both** forms are read — `@obj:OBJ-3` and `@obj:[Hero Vasya]`; which one the interface button
  puts into the clipboard is set by the project setting "object reference format";
* an unknown reference stays in the text as it is, and a reference **does not reach an object of
  another project**;
* a reference inside a passport is not expanded — there is no nesting.

## Kinds of objects

| Kind | What it is for |
|---|---|
| **character** | the hero of the clip; his appearance lives in the passport and in the reference frames |
| **location** | the setting of the action |
| **prop** | an item in the frame |
| **style** | the manner of drawing, the limits of the look |
| **reference frame** | a sample picture; usually a child of a character or of a location |
| **dataset** | a folder of captioned frames for training an adapter; the frames are its children |
| **LoRA adapter** | a trained addition to the weights of a model ([The LoRA editor](LoRAEditor.md)) |
| **file** | a file the tasks refer to |
| **hardware** | equipment tied to the work |
| **a media asset** | a shot take, an audio track, a subtitle, an editor project file: the project media library is a selection of objects of this kind |

## The list of objects

**Four views**: table (with preview squares), short table, hierarchy, tags; the chosen one is
remembered per project. There are filters by kinds and by tags, and a search.

* **the hierarchy is made by the same engine as the task tree**, so dragging with the mouse and
  with a finger, the auto-scroll near the edges and the sticky **"To root"** bar all work in it;
* for an object **without a picture of its own** (a character, a location, a style) the preview
  is taken from the children downwards — the appearance lives in the reference frames;
* the filter selects **rows**, not what a row shows: selecting "characters only" does not deprive
  a character of a face;
* at the end of a row there are the "edit" (pencil) and "delete" buttons: editing from the list
  stays one movement, even though a click on the row opens the tab;
* **objects have tags of their own**, a separate group from the task tags.

## The object tab

An object is **read in a tab**, while it is created and edited in a dialog form.

* **The header** is like the one of a task card: a plate with the project name, the `OBJ-N` code
  and the name; below — the kind of the object, the "inactive" mark, the state of the adapter.
* **The tab title** is "Object" plus the code or the beginning of the name; it is computed by the
  **same** setting as for a task and a project (Settings → Main, one setting for all of them).
* **The toolbar** in icons: edit (opens the dialog form), change server, reference, LoRA settings
  (only for an adapter), "what will go to the model", delete, refresh. Each is the same action as
  the button inside the form.
* **There are two tabs**: "Main" (the same fields, read-only, the passport rendered as Markdown)
  and **"Sub-objects"** — the list of the children with previews, the "edit" and "delete" buttons
  at the end of a row, a click opens a new tab, and the "add" button creates an object right
  inside the open one.

## The object form

The name, the kind, the parent ("belongs to object"), the tags, the reference file, the passport,
"active", and for an object of the "LoRA adapter" kind — the adapter section and the **"LoRA
setup"** button ([The LoRA editor](LoRAEditor.md)).

The file dialog button walks **only inside the project folder** and returns a path relative to
it. The name of an object is unique within the project.

### Two references — and they are about different things

The form gives out **two** references in two fields:

* **`@obj:OBJ-3`** — to be put into a task description; it expands into the passport at the start
  of a job;
* **the address of the tab** `…/object/{id}` — for external links. An object is addressed by its
  own uuid, so such a link works **between projects** and does not change the current project.
  There are two addresses, as for a task — the local one and the external one.

### "What goes into the model"

The button shows the whole expansion — exactly what the generation will get. A **LoRA adapter**
has two sets of data, and they are different, so the dialog has two fields:

* **"When used"** — the passport with the file paths; this is what goes into the prompt;
* **"When training"** — the frames of the current dataset with their captions; exactly what the
  trainer will get. A frame with no caption is visible as an empty line — before the training
  takes hours.

An ordinary object has no second field.

## The owning server of an object

In a cluster an object, like a task, has **exactly one owning server**: it is edited there and
its LoRA adapter is trained there. The card carries the plate "Object of server …", and the
toolbar the **"Change server"** button (the plate and the button are shown only when the
organization has more than one server).

* **on another server the object is read-only**: editing, deleting, the LoRA setup, creating
  sub-objects, the row buttons of the list and the drag grip in the tree are all disabled;
* the object moves **with its whole subtree** — sub-objects, datasets and frames: this is the
  content of the object;
* **the number does not change** when the server changes, and a new object gets its number with
  the server code (`OBJ-5-S1`; the conductor has no suffix) — otherwise two servers would create
  an `OBJ-5` each and the reference `@obj:OBJ-5` would point at different things;
* a new object gets its server by itself: a child — the server of the parent, a top level one —
  this server;
* objects that arrived with no server from a partner of an older version are claimed by the
  conductor when the organization is opened.

**A trained adapter travels to the neighbour.** A copy of the finished LoRA file is put into the
data folder of the organization, and that folder is replicated as a whole; on the receiving
server the file is found by itself and, before the generation, put into that server's model
repository. That is, "trained on one server — used on another" works with no manual copying.
Adapters trained before the update are not moved: the copy appears for those trained afterwards.

More about the ownership of rows and about replication — [Several servers](servers.md).

## Next

* [Projects](progects.md) — where the objects live and which project settings affect them.
* [Tasks](tasks.md) — where the `@obj:` reference is put.
* [The LoRA editor](LoRAEditor.md) — training an adapter from a project object.
* [A video clip made from a character's reference frame](sample_video1.md) — an end-to-end
  example with an object.
