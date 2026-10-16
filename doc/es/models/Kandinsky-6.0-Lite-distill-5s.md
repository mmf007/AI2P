# Kandinsky-6.0-Lite-distill-5s

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `Kandinsky-6.0-Lite-distill-5s`
**Clave API:** no hace falta — el servidor se inicia localmente sin autorización
**Qué hace:** texto → vídeo (5 segundos, 864×480, 24 fps), habilidad `video-generate` 80

Kandinsky 6.0 Video es una familia de modelos abiertos de Kandinsky Lab (Sber) publicada en septiembre de 2026: Lite (3 000 millones de parámetros) y Pro (29 000 millones). Este registro usa la versión **destilada** Lite: genera un clip en 10 pasos en lugar de 50 casi sin pérdida de calidad. El clip se genera solo a partir de la descripción de la tarea.

## Sin sonido

El propio modelo genera vídeo junto con sonido (44 kHz, voz sincronizada con los labios), pero el sonido se decodifica con los pesos MMAudio `v1-44.pth`, publicados bajo la licencia no comercial CC-BY-NC-4.0. AI2P no instala esos pesos, así que el registro produce un **clip sin pista de audio**. La parte de audio del prompt (`<AUDCAP>…`) no tiene efecto.

## No hace falta clave, hace falta instalación

Kandinsky 6 no está en el núcleo de ComfyUI: sus nodos los aporta la extensión https://github.com/kandinskylab/kandinsky-6 (carpeta `comfyui`, MIT). El botón «Instalar» del formulario del modelo lo hace todo:

1. paquete `comfyui` — la versión portátil de ComfyUI (se necesita la **0.38 o posterior**; actualice una anterior);
2. paquete `kandinsky6` — la extensión: archivo del commit, dependencias `pip` en el Python integrado de ComfyUI y copia de los nodos en `ComfyUI/custom_nodes/kandinsky6`;
3. pesos en el grupo `Kandinsky-6` del repositorio de modelos:

| Archivo | Origen | Tamaño |
|---|---|---|
| `Kandinsky-6.0-Lite-distill-5s.safetensors` | https://huggingface.co/kandinskylab/Kandinsky-6.0-Lite-distill-5s-Diffusers | 5.9 GiB |
| `qwen_2.5_vl_7b.safetensors` | https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI | 15.4 GiB |
| `clip_l.safetensors` | https://huggingface.co/comfyanonymous/flux_text_encoders | 0.2 GiB |
| `hunyuan_video_vae_bf16.safetensors` | https://huggingface.co/Kijai/HunyuanVideo_comfy | 0.5 GiB |

En total unos **22.1 GiB**. Todos los repositorios están abiertos (`gated=false`), no se necesita token de HuggingFace. El modelo solo puede activarse después de la instalación.

## Licencia

Los pesos de Kandinsky 6.0 son **MIT** (ficha https://huggingface.co/kandinskylab/Kandinsky-6.0-Lite-distill-5s-Diffusers, comprobado el 14.10.2026), se permite el uso comercial. La extensión de ComfyUI también es MIT. El codificador Qwen2.5-VL es Apache 2.0. El registro no usa los pesos de sonido MMAudio no comerciales (véase «Sin sonido»).

## Precio

Gratis: el modelo se ejecuta en su tarjeta gráfica y no hay que pagar a nadie — solo electricidad y espacio en disco.

## Requisitos de hardware

GPU NVIDIA con CUDA; como referencia, 16 GB de memoria de vídeo y 32 GB de RAM o más (ComfyUI descarga parte de los pesos a la RAM, así que con menos memoria funciona, pero más lento). Un clip tarda unos 3–8 minutos en una tarjeta de la clase RTX 4090.

## Parámetros de generación

Perfil: `width` 864, `height` 480, `length` 121 fotogramas (5 s a 24 fps; número de fotogramas de la forma 4n+1), `steps` 10, CFG 1, muestreador euler/simple — como recomienda Kandinsky Lab para las versiones destiladas. `negative` es el prompt negativo de la plantilla oficial. Este modelo no tiene entrenamiento LoRA (`lora.supported: false`).

## Errores frecuentes

* **«Cannot execute because node Kandinsky6… does not exist»** — la extensión no está cargada: reinstale el paquete `kandinsky6` y reinicie ComfyUI; compruebe que ComfyUI es 0.38 o posterior.
* **Memoria de vídeo insuficiente** — reduzca `width`/`height` en el perfil (múltiplos de 16) o use Lite en lugar de Pro.
* **Clip sin sonido** — es intencionado (véase «Sin sonido»).
