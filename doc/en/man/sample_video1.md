
# A video clip made from a character's reference frame. An example

**The aim of the chapter:** there is a picture of a character in the project, and a media model has
to make a clip out of it — and in the next clip the character has to look the same.

We go through it on the local model **`Kandinsky-5.0-I2V-Lite-5s`** (an image + text → video, 5
seconds). Everything below holds for any model of the "image → video" mode; for a "text → video"
model (`Kandinsky-5.0-T2V-Lite-sft-5s`) there is nowhere to pass the start frame — it will get the
text only.

## 1.1. Why the picture cannot simply be described in words

A media model gets **only the task description** as its prompt and nothing else: neither the title,
nor the acceptance criteria, nor the project experience, nor the chat — none of that reaches it. So
the character's appearance has to be in the frame description verbatim.

It must not be retyped by hand into every frame: retelling it in your own words from frame to frame
is exactly the reason the character "drifts". That is why the appearance is kept **once** — in a
project object, and the frame description carries a **reference** to the object. On every start of
the job the reference expands into the passport and the paths of the reference files, and the first
path goes to the model as the start frame.

An edit of the passport takes effect on the very next frame — the task descriptions do not have to be
rewritten.

## 1.2. What will be needed

| | |
|---|---|
| Rights | the **admin** role in the organization (the model reference book is a system setting). To **edit the record of a local model** (to switch it on or off on this computer) a **local server administrator sign-in** is needed as well — the link is in the user menu in the header and in "Settings → Servers"; the installation itself does not require it |
| Hardware | NVIDIA, at least 6 GB of VRAM, driver 580+, ~18 GB on disk, 16 GB of RAM (the details are behind the **"i"** button in the model form) |
| Time | a clip of 768×512, 121 frames, 50 steps takes about an hour on 6 GB of VRAM |
| The picture | a reference file **inside the project folder**, the proportions had better be close to 768×512 from the start |

## 1.3. Step 1. Install the model

1. **Settings** (the gear icon on the left bar) → the **"Models"** tab.
2. The `Kandinsky-5.0-I2V-Lite-5s` row → the model form opens.
3. The **"i"** button — the model's document: the hardware requirements, the file sizes, the frequent
   errors.
4. The **"Install"** button — the installation window. A portable ComfyUI build (~2.1 GB) and four
   weight files (~16 GB; three of them are shared with the Kandinsky text model — if it is already
   installed, only ~4.3 GiB will be downloaded) are installed. The download resumes: an interrupted
   installation continues from where it stopped.

**After a successful installation the model switches itself on.** While the files are not in place it
cannot be active — that is checked at the application's start too. The activity of a local model is
**its own on every computer of the cluster** (in the form it is "Active on this server"): on a
neighbouring server it is switched on separately, where its files lie.

This model needs no API key — ComfyUI is raised locally, without authorization.

## 1.4. Step 2. Create an AI executor and put it to work

1. **Executors** (the people icon on the left bar) → the **"+"** button ("Add an executor"):
   * *The external name (the nick)* — how it will be called in the tasks, `kandinsky-i2v` for
     example;
   * *The type* — **AI**;
   * *The internal name* — `Kandinsky-5.0-I2V-Lite-5s` (only the **active** models are in the list);
   * *Active* — switch it on.
   The connection profile and the capability declaration are taken from the reference book record,
   there is no need to edit them now.
2. **Teams** → the project's team (or "Add a team") → the **"Members"** section → add this executor;
   make sure it has **"Active in the team"** set — an inactive member is not connected at the start
   and is not taken by the auto-pick.
3. **Press "Start"** — either the whole team or **"Start the member"**.

The third step is obligatory, and here is why: **it is the start of the team's work that raises
ComfyUI** (the start command was written into the profile at the installation). If the work has not
been started and ComfyUI has not been raised by hand, the job will fall with the message *"Timed out
connecting to ComfyUI (is the server running?)"*.

While the model loads its weights, the member stands in the "connecting" state — that is normal, the
first connection takes minutes.

## 1.5. Step 3. The project folder and the reference file

The start frame is looked up **in the project folder**, the path is always a relative one. Going
outside (`..`, `C:\…`) is forbidden on purpose.

1. The project card → the **"Main"** tab → the **"The path to the project folder"** field (with a
   browse button next to it). The folder is **its own on every server of the cluster**: on another
   computer the same project lives in another directory, while the relative paths inside are the
   same.
2. Put the reference into that folder, `refs/hero.png` for example.

The first frame of a series is conveniently obtained with an ordinary "text → image" model or drawn
by hand — everything else is built from it.

## 1.6. Step 4. Create the character object

The project card → the **"Objects"** tab → the **"+"** button ("A new object").

| Field | What to write |
|---|---|
| *The name* | what people call the character: `Hero Vasya` |
| *The kind* | **a character** (the kinds: a character, a location, a prop, a style, a reference frame, a LoRA adapter, a file, equipment, a media asset) |
| *Belongs to the object* | empty for the character itself; for its reference frames — the character |
| *Tags* | their own group, separate from the task tags |
| *A file or an address* | `refs/hero.png` — the path **relative to the project folder**; next to it there is a file chooser button (it walks the project folder and puts a relative path), on the left there is the preview window |
| *The passport (goes into the prompt)* | a **verbatim** description of the appearance: this text will stand in place of the reference |
| *Active* | on |

The passport is the main field of the form. Write it the way you want to see it in the prompt:

> A man of 35, a short dark beard, a scar over the left eyebrow, grey-green eyes. A worn brown
> leather jacket, a grey scarf, jeans. The lighting is cold, evening.

After saving, two buttons appear in the form:

* **"The object reference"** — what is pasted into the task description (`@obj:OBJ-1`);
* **"What will go to the model"** — the text the reference will expand into. Check the passport here,
  **before** the generation, and not by a frame that does not look right.

**Several references of one character** are created as its children: a new object of the "reference
frame" kind, with the character in the *"Belongs to the object"* field. In the list they stand under
it, indented, and all their paths go into the substitution.

## 1.7. Step 5. The frame description

We create a task (the "new task" button in the task list) and fill it in:

| Field | The value |
|---|---|
| *The title* | `Scene 1: the hero turns around` (it will **not** get into the prompt) |
| *The description (.md)* | the prompt text + the object reference — see below |
| *The skills* | **`video-animate`** — that is exactly the "image → video" (i2v) mode |
| *The executor* | the AI executor you created; or leave it empty and rely on the auto-pick — but then the task must have a **team**: the pick is made from its members |
| *The project* and *the team* | in the **"Extended"** section; the project is obligatory — the paths are counted from it |

The description:

```
@obj:OBJ-1 slowly turns around over the left shoulder, looks into the camera.
Light rain, neon reflections on the wet asphalt, a night street.
The camera slowly moves in.
Put the result into the file scenes/s01.mp4.
```

What happens here at the start:

1. `@obj:OBJ-1` expands into the object's card — the name, the kind, the number, the verbatim
   passport and the paths of the reference files, each on a new line;
2. the paths are **cut out** of the prompt (these are paths, not the text of the scene), and the
   **first** of them becomes the start frame;
3. the line "Put the result into the file `scenes/s01.mp4`" is cut out too — it is carried out by the
   system: the ready clip is copied into the project folder under that name;
4. everything else — including the passport — goes to the model as the prompt.

> **Write the directives on a separate line.** In a description of several lines a directive ("put
> the result into …", "base it on …") is removed **as a whole line** — together with whatever you
> added next to it. The line "The camera slowly moves in. Put the result into the file
> scenes/s01.mp4." would cost the prompt the camera move. (A one-line description is an exception:
> there only the sentence with the directive is removed.)

**The reference does not have to be typed by hand:** in the object list every row has a "reference"
button, and in the description editor there is the **"A reference to a project object"** button: it
inserts the reference at the cursor position in the form set by the project setting *"The object
reference format"* — by the number (`@obj:OBJ-1`) or by the name (`@obj:[Hero Vasya]`). **Both forms
are always read**, so changing the setting does not break the descriptions already written.

### If you need a particular frame and not the first one

Name the file in words — such a directive is **stronger** than the path from the object:

```
Base it on refs/hero_side.png.
@obj:OBJ-1 slowly turns around over the left shoulder…
```

A directive is a line that holds the name of a picture file and a word like "source", "based on",
"start", "reference", "animate", «исходный», «за основу», «стартовый». Such a line is removed from
the prompt as a whole.

### An important limitation

The model has **one** start frame. The other reference paths are simply removed from the prompt — the
model does not see them. Several reference frames of an object are useful for a person and for the
future training of a LoRA adapter, but only the first path affects an i2v generation: the path of the
object itself, and if it is empty — the path of the first active child.

## 1.8. Step 6. The start and watching it

The **"Start"** button in the task card.

* The **"Jobs"** tab — the list of jobs and the **console**: the output of ComfyUI itself is piped
  there, including the step counter. It shows whether the generation is going on or has stalled.
* The first line of the console is the start summary: the ComfyUI address, the model, the frame size,
  the number of frames and steps, the `seed`, the length of the prompt, the start frame and the result
  file.
* The wait is long: about an hour on 6 GB of VRAM. The waiting limit is the executor's "answer
  timeout", and if it is not set — the `params.timeoutMinutes` of the profile (180 minutes by
  default).
* It can be stopped by the **"Stop"** button in the task card: the generation in ComfyUI is
  interrupted, the job is removed from its queue.

**What you get:**

* the **"Result"** tab — the job's files (`J-12-Kandinsky_00001.mp4`) and the `J-12-result.md`
  summary: the model, the frame size, the number of frames and steps, the **`seed`** and the
  generation time;
* a copy of the clip in the project folder — if the description said where to put it;
* the exact request — the `J-12-request.json` file (the prompt after all the substitutions, the
  `startImage`, the `seed`, the whole graph). The path to it is written into the work journal, the
  **"History"** tab of the task. This is the best way to check that the model got exactly what you
  had in mind.

## 1.9. The same character across a series of frames

| the trick | what it gives |
|---|---|
| **one and the same object** in all the frames | the face, the clothes and the colour hold between the clips |
| **a fixed `seed`** | the same "character" of the generation; empty or 0 means a random one for every job |
| **chaining** | save the last frame of scene N as a picture into the project folder and give it as the input of scene N+1 |
| **one frame size** | clips with different `width`/`height` drift apart from each other |

The `seed` and the other generation parameters are edited in the model's profile — the **"Profile…"**
button in the model form or in the executor form:

| Parameter | The default | The meaning |
|---|---|---|
| `width` / `height` | 768 × 512 | the frame resolution; the start picture is brought to it too (cropped by the centre) |
| `length` | 121 | frames, ≈5 seconds |
| `steps` | 50 | the diffusion steps; fewer means faster and rougher |
| `negative` | empty | the negative prompt |
| `seed` | none | a fixed seed |
| `timeoutMinutes` | 180 | how long to wait for the result |

An executor's profile is its own: two executors can be created on one model with different `seed`s
and resolutions.

## 1.10. Frequent errors

| The message or the symptom | What to do |
|---|---|
| **"This model needs a start frame…"** | the description holds neither a picture file nor a reference to an object with a reference file |
| **"The start frame file was not found in the project folder: …"** | the path is counted from the project folder; check the spelling and that the file really lies there |
| **"The project has no folder set on this computer…"** | set "The path to the project folder" in the project card — the folder is its own on every server |
| **"The start frame … leads outside the project folder"** | `..` and absolute paths are forbidden on purpose |
| **"Timed out connecting to ComfyUI (is the server running?)"** | the team's work has not been started (step 2) — and it is exactly what raises ComfyUI |
| **"The task description is empty — a media model needs the generation prompt in the description"** | nothing was left of the description after the directives were cut out; add what happens in the frame |
| **"The workflow of this model does not accept a start frame — the file … is not used"** | the task went to a "text → video" model; give the task the `video-animate` skill or name the executor you need explicitly |
| **The `@obj:…` reference stayed in the prompt as text** | the object was not found: the wrong number or name, or an object of another project. The reference is deliberately not erased — otherwise the frame would come out without the character while the prompt would look right |
| **The character "drifts"** | check that the frames refer to one object, that the `seed` is fixed, that the frame size is the same, and that the passport is not retold anew in the description |
| **The process disappears without a message** | almost always an old NVIDIA driver; start with `nvidia-smi`, 580+ is needed |
| **Not enough VRAM** | reduce `width`/`height` or `length` |

## 1.11. What is worth remembering

* A media model **does not hold a dialogue**: it has neither tools nor a chat. Writing to it in the
  task chat is useless — a new frame is a new start of a job.
* The title, the acceptance criteria, the project experience and the experience of a template node do
  **not** get into the prompt. Everything that has to get into the generation lies in the task
  description or in the object's passport.
* A reference inside a passport **is not expanded**: a passport that referred to another object will
  leave the reference as text. That is a protection against objects referring to each other.
* An object belongs to a project: a reference will not reach an object of a neighbouring project.
* A switched-off object stays in the list and the references to it do not break — that is not a
  deletion; but switched-off **children** do not go into the path substitution.
