# Musubi Tuner (LoRA) — the `trainer.musubi` plugin

**What it does:** trains **LoRA** adapters with the musubi-tuner trainer (kohya-ss) — caching
the latents, caching the text encoder outputs and the training itself. The work takes hours,
so it is performed by a **separate task** with a software executor, not by a tool of the AI
agent.

**Why it is needed.** The official Kandinsky trainer requires several graphics cards and Linux;
musubi-tuner makes do with one card, works on Windows and writes the adapter file with the key
names ComfyUI understands. The «Train» button of the LoRA editor creates a task on exactly this
plugin.

The trainer sources: https://github.com/kohya-ss/musubi-tuner

---

## The programs

The plugin has **two** programs, and both are mandatory:

| Record | What it is | How it is installed |
|---|---|---|
| **Python 3.10–3.12** | all three steps are started with it | with the «Install» button (our `python` package) or by a path to an already installed Python. musubi-tuner does not build on 3.13, and a found 3.13 counts as unsuitable |
| **Musubi Tuner** | the folder of the trainer scripts | with the «Install» button (the v0.3.4 sources) or by a path to the folder you unpacked them into: it holds `kandinsky5_train_network.py`. It is not looked up in `PATH` — it is a folder, not a program |

The trainer builds its own torch environment in the `.venv` subfolder on the first training.

**The plugin record is usually created by itself** — when a model that declares training
packages is installed (both Kandinsky 5 records declare them). The found program is written
into the empty path field at the same moment. The initialisation is still done by a human with
the «Initialise» button: it creates the action catalog records, and with them the points the
security rules attach to.

---

## The long-run operations

Three steps, exactly in this order:

| Operation | What it does |
|---|---|
| `lora.cacheLatents` | runs the dataset frames through the VAE and stores the latents in the cache |
| `lora.cacheText` | computes the text encoder outputs (Qwen2.5-VL and CLIP) over the frame captions; the step is mandatory, the training does not start without it |
| `lora.train` | the adapter training itself; marked **«Single instance»** |

The «Single instance» mark on the training is not a decoration: there is one graphics card, and
a second training started at the same time would end in an out-of-memory refusal within a
minute. The second task **waits** in the queue.

The result of the training is **the newest** `.safetensors` file of the working folder by the
mask of the adapter name: the name of the epoch file is not known in advance. The limits: the
silence timeout is 15 minutes, the overall limit is a day.

---

## The settings of the record

They are set in **«Settings → Plugins and MCP»**, in the record form; they cannot be changed in
the text of a task.

| Key | Default | What it means |
|---|---|---|
| `scripts` | empty | the folder of the musubi-tuner scripts — must be filled in |
| `dit`, `vae` | empty | the model weight files |
| `task` | `k5-lite-t2v-5s-sd` | the trainer task; the other option is `k5-lite-i2v-5s-sd` |
| `textEncoderQwen` | `Qwen/Qwen2.5-VL-7B-Instruct` | the text encoder |
| `textEncoderClip` | `openai/clip-vit-large-patch14` | the second text encoder |
| `steps` | `2000` | the number of training steps |
| `networkDim`, `networkAlpha` | `32`, `32` | the size of the LoRA network |
| `learningRate` | `1e-4` | the learning rate |
| `mixedPrecision` | `bf16` | the mixed precision (`bf16`, `fp16`, `no`) |
| `outputName` | `lora` | the name of the adapter file |

The trainer needs the **original** text encoder weights (Qwen2.5-VL is about 16 GB), not the
trimmed ComfyUI builds: the training will not go with those.

---

## The operating system limitations

**Windows** — our Python and musubi-tuner packages are built for it. **Linux and macOS** — the
trainer supports them, but we have not checked it live: there it is safer to install Python and
the scripts by your own means and to point at the paths by hand.

A graphics card is needed in any case: on a processor the training does not compute in any
reasonable time.

---

## What this plugin does not do

* **it publishes no tools to the AI agent** — the training is created as a task, not as a call
  of the agent;
* **it does not collect the dataset**: the frames, the captions and the trainer setup files are
  prepared by AI2P itself from the model settings;
* **the `.venv` environment** is built by the trainer, not by the plugin;
* today the three operations are described in the manifest, but the actual start of the
  Kandinsky training is carried out by the command from the model profile; the plugin still
  gives the binding to the server, the «Single instance» flag and the point a security rule
  attaches to.
