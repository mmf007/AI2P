# The LoRA editor

A **LoRA adapter** is a small file with an addition to the model weights. It teaches the model
one particular thing: the face of a character, the texture of a material, a drawing manner.
Once trained, the adapter is attached to the model for the time of the generation — and the
character stops drifting from frame to frame even where a text passport alone is not enough.

The **LoRA editor** is the form where the dataset is collected (frames with descriptions), the
list of models the adapter is trained for is kept, and the training itself is started. This
page is about it.

This help opens right from the editor: the book button in the top right corner of the form.

---

## 1. Before you start

Training is not a button but the work of a training program that lives **outside AI2P**. Four
things are needed for the editor to be able to start it:

| What | Where it is set | How to check |
|---|---|---|
| The project folder on this server | project card, «Main» tab | without it: «The project has no folder on this server: there is nowhere to collect the training dataset» |
| A model that works with adapters | Settings → Models, the LoRA mark of the record | in the «Models» tab of the editor the «Train» button of an unsuitable record is disabled |
| The training command of that model | model profile, section `lora.train.start.command` | without it: «The model catalog does not say what to train … with: the training command is not set» |
| The models repository | Settings → Servers → the local server form, «Models repository» | the ready adapter lands there, in the `loras` subdirectory |

Three more things take part in the work, though they need no separate setup: the **trainer
plugin**, the **software executor** and the **template of the training task**. The trainer
comes with the model installation, and the executor and the template are created with one
button by the training start window itself — see section 5, «The training runs as a task».

What exactly your model can do is visible in its catalog record: **Settings → Models**, the
«LoRA» column (a tick means it works, a dash with a tooltip says why it does not), and in the
record form itself — the lines «Adapter is attached», «Training», «Trained adapter goes to»
and the link **«How to train an adapter for this model»**. That link is where to start:
it leads to the instructions of whoever released the model.

The training command deserves a separate word. For **both Kandinsky 5 records** it **is**
shipped and nothing has to be written in: the training is done by musubi-tuner, and the
trainer itself comes together with Python **when the model is installed** — by the «Install»
button of its form, the same place where the weights are downloaded (the details are in the
model document, the «i» button). The first training run only prepares the setup files
(`dataset.toml`, `train.cmd`): the `.venv` environment with torch, the trainer requirements
and the Qwen2.5-VL-7B and CLIP text encoders (about 18 GB) come with the model install — in a
window that shows the progress, not silently in the middle of the training. If Python 3.10-3.12 is
already on this machine, the install does not download it and takes yours instead. For the other records the command is **deliberately
empty**: the directory of the training repository, the environment and the launch keys are
different for everyone, and an invented command would be worse than an empty one — it is
built from the instructions behind that link and written into the profile once
(`lora.train.start.command`).

Together with the command the profile may also carry the **trainer setup files**
(`lora.train.files`): the dataset configuration, the launching script. They are put next to
the dataset before every run and are **rewritten** every time — edit them in the model
profile and not on disk; your own files under your own names are never touched.

For the catalog records that are trained **outside the system** (the local llama.cpp text
models) starting the training from AI2P answers «The adapter for the model … is trained outside
the system — put the ready file in place by hand and write its path». This is not a breakage:
for such models the editor is used for the dataset and the list, while the training is done by
your own means and the path of the ready file is written into the «Adapter file» field of the
object form.

---

## 2. How to open the editor

1. Open the project card → the **«Objects»** tab.
2. Create an object of the kind **«LoRA adapter»** («New object» button, the «Kind» field) and
   **save** it. Until the object is saved there is no setup button at all: dataset frames and
   training rows need an owner, and an object that does not exist yet has no number.
3. Open the object and press **«LoRA setup»**.

The «LoRA adapter» section and that button exist only for an object of the kind «LoRA adapter».
A character, a location or a prop has no adapter of its own — but it may have a **ready**
adapter: the path of its file is written into the «Adapter file» field of the same form, and
then the adapter is attached to the model every time a task description refers to that object.

The editor form consists of the **«Description»** field and two tabs — **«Dataset»** and
**«Models»**.

---

## 3. The «Description» field: what to write

This is the very same text that the object form calls **«Passport (goes into the prompt)»** —
an object has no second description. It **does not take part in the training**, it goes into
the **generation** prompt: when a task description carries the reference `@obj:OBJ-7`, this
text is substituted for it.

What to write:

* **a literal description of what the adapter is trained for**, not a note to self: «a woman of
  30, red hair to the shoulders, green eyes, freckles, a grey jacket» — and not «Masha, our
  main character, see the frames»;
* **the trigger word**, if it is used in the frame captions — it is what makes the model recall
  what it learned: «`m4sha_rf`, a woman of 30, …»;
* **the strength and the limits**: what the adapter does NOT do («waist-up only, there were no
  full-height frames») — this saves a few failed generations;
* write **in the language the task descriptions are written in**: the text goes into the prompt
  as is, mixed with the rest of the frame description.

What is not needed there: service notes like «trained for 2000 steps on 40 frames» — they do
not help the generation and take up room in the prompt. The «Models» tab is for that: it shows
what the adapter was trained for and how it ended.

The description is **not saved here**. The «Apply» button returns it to the object form, and
that form saves it together with everything else. Frames and training rows, on the contrary,
are saved at once, by each action: training takes hours, and losing it to an accidental
«Cancel» is not acceptable.

---

## 4. The «Dataset» tab

A dataset is a set of frames: **a picture plus a description**. The adapter learns from them.

**A dataset is a separate object** (version 1.98). It is created by itself when you open the
editor, is named «dataset» and lies as a child object of your adapter; the frames are its
children. This is done for the sake of the hierarchy: while the frames were direct children of
the object, half a hundred pictures lay mixed with meaningful child objects, and the list could
not be read at all.

### 4.0. The current dataset and why there are several

At the top of the tab there is the **«Current dataset»** field and the **«+»** button.

* Everything on the tab belongs to the **selected** dataset: both the list of frames and the
  picture control settings. A new frame lands in it too.
* The **«+»** button asks for a name and creates one more dataset, making it current at once.
* There are several datasets because **different models have different requirements for
  pictures**: one is trained on 768×512 PNG, another asks for 1024×1024. Keeping one set of
  frames for them means rebuilding it at every change of the model.

The choice is remembered **at the object**, not in the window: the training started later goes
by the current dataset as well.

### 4.0.1. The picture control settings

The **«Picture control of the dataset»** section holds the limits a frame is shrunk to when it
is added **to this dataset**: width, height, weight in KB, format, and also how many frames are
needed at least and how many make sense at most.

* When the dataset is created they are **copied from the common settings** of the application
  (**Settings → Main → «LoRA dataset frame»**) and live their own life afterwards.
* The **«Take from the model»** button puts in what the model declared in the catalog. The list
  holds only those models from the «Models» tab of this object whose control settings are filled
  in the catalog: there is nothing to take from a model that said nothing. If a model names
  several formats, **PNG** is taken — it is lossless.
* The changed values are written by the **«Save»** button; the frames already added are not
  redone — the new limits apply to the next frame.

### 4.1. How to add a frame

The «+» button opens the frame form. Then come two steps, and neither is redundant.

**Step 1. Take the source.** Either **«By address»** (`http://…`, the server downloads the
picture), or **«File on the computer»** — the folder button opens a walk over the disks of this
computer. The walk starts:

* from the directory already typed into the path field, if there is one;
* otherwise from the directory **the file was taken from last time**;
* otherwise from the **project folder**;
* and only if none of these exists — from the list of drives.

The directory of the previous pick is remembered for good (it survives closing the window and
the next login), so a batch of frames from one folder is added without walking the disks over
and over. Should that directory be gone, the walk starts from the nearest existing parent
instead of showing an empty list.

Press **«Load»** — the source lands in the storage and the crop frame appears.

**Step 2. Cut the frame out.** Drag the top left and the bottom right corners of the frame:
what is inside it becomes the frame. Cutting, shrinking and the format conversion are done **in
the browser** — a ready picture goes to the server. Under the frame it says what the frame will
be shrunk to; the limits come from the **picture control settings of THIS dataset** (see 4.0.1),
not from the common settings of the application — the common ones were only their snapshot on
the day the dataset was created. The aspect ratio does not change, the smaller of the two
factors is taken. If the size limit is not met, make the frame smaller or raise the limit in
the dataset settings.

The frame lands in the **data directory of the organization**, in the
`projects/<project code>/objects/<object code>/<dataset code>/` subdirectory, and is created as
an object of the kind «reference frame» — a child of the **dataset**. That is why the frames are
also visible in the general list of the project objects.

The place is not accidental: the data directory of the organization is **replicated**, so a
dataset collected here is visible on the other servers of the cluster as well — there you can
open it, look at the frames and start the training on them. The project folder is different on
every server and does not travel across the cluster, so frames kept in it (as it was before
version 1.112) were not shown on a neighbouring server at all. Frames accumulated by earlier
versions are moved by the program itself at the first start of the new version; the original
files in the project folder are not deleted — that folder belongs to you.

### 4.2. The frame description: what to write

The **«Frame description»** field is **the only text the training sees**. Next to every picture
in the dataset directory it is written as a `.txt` file with the same name (for some models —
as a common `captions.json` file; which one is said in the model profile).

Recommendations proven by LoRA training practice:

* **describe the frame, not the character in general.** The training learns what is the same in
  all the frames — and that does not need a caption. What is captioned is what **changes** from
  frame to frame: the angle, the shot size, the light, the background, the clothes, the
  expression;
* **start with the trigger word** if you have one: `m4sha_rf, close-up, half-turned left,
  daylight, blurred street background`;
* **do not write what you do not want to be learned.** Everything named in the caption is
  treated as the variable part the model learns to control; everything not named is baked into
  the adapter itself. Hence the usual rule: the constant appearance of the character is not
  described, while the background and the clothes are — otherwise the adapter learns the jacket
  and the brick wall for good;
* **write short comma-separated enumerations**, not paragraphs: training scripts cut the caption
  by length, and a long sentence loses its tail;
* **one language for the whole dataset.** A mix of languages in the captions is a sure way to
  get an adapter that answers every other time;
* **do not leave the caption empty**: an empty `.txt` means «nothing changes here», and the
  training bakes the whole frame into the adapter, background and all.

The **«Name»** field in the frame edit form is the name of the **object**, not of the file: the
file already lies in the project folder under its own name and is not renamed. When a frame is
added it is the other way round: the name becomes the file name (the object code is prepended
so that names of different characters do not collide).

The «Edit» button changes only the name and the description. There is no way to re-cut a frame
that is already added: for that it is deleted and added again from the same source. The
«Delete» button removes the frame from the dataset, **the file itself stays in the project
folder** — it might have got there not from the editor.

### 4.3. Does the order of the frames matter

The short answer: **for the training itself no, for the selection yes.**

The order in the list is the order of **adding** (frames are numbered as objects: OBJ-12,
OBJ-13, …), and in the same order they get into the dataset directory under the names
`001.png`, `002.png`, `003.png`… There is nothing in the editor to reorder them: to make a
frame the first one it has to be deleted and added again.

Where the order does matter:

1. **The cut by the upper limit.** The model profile says how many frames it takes
   (`dataset.maxItems`; for the shipped Kandinsky records it is 60). If there are more, the
   **first** ones are taken — the rest silently do not take part. So the best and the most
   characteristic frames should be added **earlier**.
2. **The file numbering.** By the number `001…NNN` you later match a frame with what you saw in
   the log of the training script. The order of the numbers is the order of adding.

Where the order does not matter: the training itself goes over the dataset many times and
shuffles it — the «first» frame does not affect the result more than the «last» one. The lower
limit is worth remembering separately (`dataset.minItems`; for Kandinsky it is 10): fewer means
the refusal «There are N frames in the dataset, and the training of this model needs at least
M».

One more selection rule: only **active** frames go into the training. A frame can be switched
off without deleting it — open it as an object (the «Objects» tab of the project) and clear the
«Active» checkbox. This is the only way to throw a bad angle out of the training without losing
the file.

---

## 5. The «Models» tab and starting the training

An adapter is trained **for a particular model**: the weights of the models differ, and a file
trained for one does not fit another. So there are as many training rows as there are models
you train the adapter for.

The training always goes **by the current dataset** — the one selected on the «Dataset» tab.

1. The «+» button picks a model. The list holds the **active** catalog records with LoRA work
   declared in the profile; the rows already added are not offered twice. An empty list means
   exactly one thing: there is no suitable enabled record on this server.
2. The **«Train»** button in the row is the beginning. The training is **not started from the
   form itself**: it is arranged as a separate **task**. First the system asks the dataset
   questions, then it offers to pick a **task template** and asks what to do next — «create and
   run», «create only» or «cancel». The detailed order is below.
3. The row goes into the **«training»** state while the training itself takes hours. The form
   may be closed — the training is not interrupted by that; when you open it again you see the
   current state (while there is a training row in the list, it is re-read by itself every five
   seconds).
4. **The state of the row is a link into the task card**, and not only for «training»: one
   comes to a crashed training a day later exactly for the log. The stop is there as well: to
   stop the training now means to stop the task.
5. An already trained row is retrained through a confirmation: retraining **overwrites the
   ready adapter file**, which might have already been put into the model, and there is nothing
   to undo it with.

The states of a row: **not trained → training → trained**, or **error**. The tooltip of the word
«error» (hover the mouse over it) holds what the training program or the provider said — without
that the word «error» is of no use.

### What the system asks before the start

Besides the retraining confirmation there are two questions, and both are about the training
silently doing something other than what you expect.

* **«The dataset is not the same one».** A training row remembers which dataset the file lying
  in it was obtained from. If another one is selected now, the system names both and asks which
  one to run on: «the current one» or «the previous one». An adapter trained on other frames is,
  in effect, another character, and replacing it silently is not acceptable.
* **«The settings are larger than the model declared».** The picture control settings of the
  dataset are compared with what the model declared in the catalog, and only **upwards**: a
  frame that is larger, heavier, more frames — or a format the model does not accept. A dataset
  with values **smaller** than the declared ones is legitimate and is not asked about. The
  question lists exactly what differs, and there are **three** answers: «Train» (as is),
  «Cancel» and **«Create/switch to a suitable dataset»**.

Both conditions are checked by the **server**, not only by the form: the training is started
from outside this window too.

### «Create/switch to a suitable dataset»

The button removes the manual work of «create a dataset — copy the model settings into it —
convert fifty pictures». It works like this:

1. **First a suitable one is looked for among the datasets already created** for the object:
   its settings fit into the ones the model declared AND it holds the same frames (compared by
   file names without the extension). Found — it simply becomes the current one, and nothing
   is converted.
2. Not found — a **new** dataset is created with the settings taken from the model, named like
   «Character 768×512 PNG». It becomes the current one at once.
3. The frames of the source dataset are **converted** into it. The rule: the aspect ratio does
   not change and **the picture may not be cropped** — it is shrunk by its longer side, placed
   in the centre of the frame, and the remaining padding is filled with the colour from
   «Settings → LoRA dataset frame → Padding colour» («#RRGGBBAA», the default `#FFFFFF00` is
   white with full transparency: for PNG the padding comes out transparent, for JPEG, which has
   no alpha at all, — white). A small source is never enlarged.
4. After that the training starts on the new dataset; the question about the dataset having
   changed is not asked a second time — that was your answer.

Frames that could not be converted (no file, did not fit into the kilobyte limit) are named in
a separate message: the dataset is still assembled from the rest.

### The training runs as a task: a template, a console and a log

Training is not a minute of work, and in the system it looks like an ordinary task: it has an
executor, a console, a log, a history and a result. Its executor is of the third type — the
**software executor** (see [Executors](performers.md)): a pair of «trainer plugin + training
operation».

What happens when «Train» is pressed, in order:

1. **The dataset questions** — the ones described above: they are about «what to train with»
   and come first.
2. **The system looks at what and by which template it can start**: with one request it learns
   whether there is a trainer plugin, whether there is a software executor and whether there
   are suitable task templates. What you see depends on the answer:

| What is missing | What the form offers |
|---|---|
| there is no trainer at all | a refusal in words: install the program in «Settings → Plugins and MCP» |
| the plugin is there but not ready | open the plugin installation window and deliver the program |
| there is no executor | create it from the «Plugins and MCP» record — with one button |
| there is no task template | create a template node with that executor — with one button |
| everything is in place | the template choice window and three answers |

3. **The choice window**: a task template and one of three answers — **«create and run»**,
   **«create only»** or **«cancel»**. «Create only» is needed when the training must go at
   night or after some other work: the task is created and waits for the usual start.
4. **The title, the description, the executor and the tags are written by you nowhere** — they
   are carried over by the template node together with its experience. They are edited in the
   template, once for all trainings.
5. The system looks for the trainer plugin **by the training packages** named by the model — by
   the same rule that creates the plugin record when the model is installed. It picks the
   operation by itself: the one marked «single instance» (for a trainer that is the training —
   there is one graphics card).

Such a task needs no parameter fields of its own: the training row remembers its task, and the
row is found by the task — and the row already holds the object, the dataset, the model and the
whole training setup.

**Where to watch how it goes.** In the task card: the console shows the trainer's output live,
and the job log keeps all of it. An error after that lies in three places at once — in the
console, in the job artifact and in the tooltip of the word «error» in the training row.

**There are two guards against «the same thing twice», and they are about different things**: a
row that is already training gets an immediate refusal from the button, while a second task on
the same trainer operation **waits** in the queue while the graphics card is busy.

### What the system does while the row is «training»

1. **Collects the dataset** into the directory from the model profile (for Kandinsky it is
   `lora/<object code>/dataset` relative to the project folder). The directory is **cleaned**
   before that: otherwise a removed frame would keep taking part in the training. The pictures
   are copied as `001.<ext>`, `002.<ext>`…, the captions land next to them as `.txt` files of
   the same name. The frames are taken from **the dataset the run was started on**; its code
   can be substituted into the command as `{datasetId}`.
2. **Checks the training packages** named by the model (`lora.train.packages`) — for
   Kandinsky those are `musubi-tuner` and `python`. The training does not install them: they
   come with the model install, where a window with the progress is ready for that. If
   something is missing, the refusal arrives at once, at the press of the button, and it says
   what to open and what to press.
3. **Writes the trainer setup files** (`lora.train.files`) next to the dataset — for Kandinsky
   those are `dataset.toml` and `train.cmd`. They are rewritten on every run.
4. **Starts** the training command from the model profile (in a separate hidden process) or
   calls the training service over HTTP. The command and the setup files get: `{object}` — the
   object code, `{name}` — its name, `{dataset}` — the dataset directory, `{output}` — the
   result directory, `{steps}` — the number of steps, `{width}`/`{height}` — the frame size from
   the dataset settings, and the model install paths as well: `{modelsRepo}`, `{groupDir}`,
   `{model:<file>}` — a weights file, `{package:<code>:<file>}` — a file of an installed package.
   **The object description is not substituted** — all the text the training sees lies in the
   frame captions.
5. **Waits** for the end of the work. Longer than the timeout from the profile (720 minutes, that
   is 12 hours, by default) we do not wait: a hung training has to become an error some day
   instead of staying in the «training» state for good. A non-zero exit code is an error too,
   and the last lines of the output get into its text.
6. **Fetches the adapter file** and puts it into the models repository — where the engine itself
   takes it from (for ComfyUI it is the `loras` subdirectory, the default name is
   `<object code>.safetensors`).
7. **Fills in the object**: the path of that file and the state «ready» — that is, from this
   moment on the adapter is attached to the model by itself.

Steps 4–7 go **inside the job of the task**: the trainer's output is read continuously and
lands in the console of the card instead of piling up in the pipe. There are two waiting
limits — the overall one (720 minutes by default) and the **silence timeout** (30 minutes by
default: that is how long reading the weights of a text encoder can take). A program that has
not said a line for longer than that is considered stuck and is killed — twelve hours of
silence no longer happen under any setting.

---

## 6. Which field goes where

| Field | Where it is filled in | Where it goes |
|---|---|---|
| The adapter «Description» (the «Passport») | the editor form, the object form | into the **generation prompt** instead of the `@obj:` reference — it does NOT go into the training |
| «Frame description» | the dataset frame form | into the **training**: the `NNN.txt` file next to the picture (or the common `captions.json`) |
| The «Name» of the adapter object | the object form | the `{name}` substitution in the training command; the `@obj:[Name]` reference |
| The object code (OBJ-N) | assigned automatically | the `{object}` substitution: the dataset directory, the adapter file name |
| The «Name» of a frame | the frame form | the file name in the project folder (when adding) and the name of the frame object |
| «Adapter file» and «Adapter state» | the object form | attaching the adapter to the model at generation time; filled in by themselves after a successful training |

---

## 7. How a trained adapter gets into the generation

There is no separate «attach the adapter» action. The reference rule works instead: put a
reference to the object into the description of a media task (`@obj:OBJ-7` or `@obj:[Name]`),
and before the job starts the system decides what goes into the model:

* a reference to an object of the kind **«LoRA adapter»** — the adapter is **mandatory**: the
  model has to accept it and the adapter has to be trained. Otherwise the job fails;
* a reference to **any object with a ready adapter** (the «Adapter file» of a character is
  filled in) — the adapter is attached: that is what it was trained for;
* an adapter **in progress** («planned», «training») — only a note in the job console: the frame
  is made from the passport;
* a character, a location or a style without an adapter — as before: the passport goes into the
  prompt, the reference frames of the children are candidates for the start image.

This is made an error deliberately: a frame silently generated without the adapter looks fine
and goes out as a ready result — while the character on it is somebody else.

---

## 8. Frequent errors

| Message | What happened and what to do |
|---|---|
| «The project has no folder on this server» | the project has no folder **on the server where the training runs**; the folder is per-server and is set in the project card |
| «The model catalog does not say what to train … with» | the training command in the model profile is empty — write it (the link to the instructions is in the profile) |
| «The adapter for the model … is trained outside the system» | this record has external training: train it by your own means and write the file path into the «Adapter file» field |
| «There are N frames in the dataset, and the training needs at least M» | add frames: the lower limit of the shipped Kandinsky records is 10 |
| «There is no frame file in the project folder» | the frame file was deleted or renamed outside AI2P — delete the frame and add it again |
| «The frame weighs N KB / the frame is N×M pixels» | the limits of this dataset are smaller than what was sent; make the frame smaller or raise the limits in «Picture control of the dataset» |
| «The dataset is not found or belongs to another object» | the selected dataset was deleted or is a foreign one: select the dataset again |
| «Last time the training used the dataset …» | the dataset changed since the last training — answer which one to run on |
| «The dataset settings are larger than the ones the model declared» | the comparison with the catalog found an excess or a foreign format: train anyway, press «Create/switch to a suitable dataset», or take the settings from the model and rebuild the frames by hand |
| «Only an image can be a dataset frame» | what lies at the given path is not a picture |
| «The training did not finish in N min and was interrupted» | the timeout from the profile was not enough — raise `wait.timeoutMinutes` or lower the number of steps |
| «The training finished with the code N» | the training program itself failed; its last lines are in the tooltip of the word «error» |
| «The training left no adapter file» | the command finished, but there is no file at the `result.path` path: check the path in the profile against where your script puts the result |
| «The training did not print a line for N min» | the silence timeout fired: the program got stuck. Look at the last lines in the task console and in the job log; the limit is set by `wait.idleMinutes` in the model profile |

---

## 9. What the editor does not do

An honest list of the limits of this version — so that nothing non-existent is looked for:

* **the order of the frames does not change** — only by deleting and adding again;
* **an added frame cannot be re-cut** — the crop frame is available only while adding;
* **a frame cannot be switched off from the editor** — the «Active» checkbox lives in the form
  of the frame object, and a switched-off frame carries no mark in the editor list (it does not
  go into the training all the same);
* **sending the dataset to a provider** (cloud training with an archive of frames) is not done:
  the training runs as a process of its own or over HTTP to your own training service;
* **frames are not moved between datasets by a button** — a dataset is an ordinary object, and
  a frame is moved by dragging it in the «hierarchy» view of the «Objects» tab;
* **changing the control settings does NOT redo the frames already added** — the new limits
  apply to the next frame;
* **there is no training progress in percent** — there is the state of the row, the text of the
  error and the live output of the training program itself in the console of the task card
  (the whole job log is there too);
* **several adapters at once** are usually not taken by a model (`apply.maxCount`, 1 for the
  shipped records): if a task description names more objects with adapters, the job answers with
  an error instead of quietly taking the first one.

---

## See also

* [A video clip made from a character's reference frame](sample_video1.md) — where the work with a
  media model starts: installing a local model, the AI executor, the character object and the
  `@obj:` reference.
* [Projects](progects.md) — the project objects, the object passport and the project folder.
* [Section contents](README.md) — the other chapters of the guide.
