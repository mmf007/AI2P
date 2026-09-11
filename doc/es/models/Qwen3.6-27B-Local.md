# Qwen3.6-27B-Local

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: openai-compatible`, `baseUrl: http://localhost:8082/v1`,
modelo `qwen3.6-27b`
**Clave de API:** no hace falta; el servidor local se levanta sin autorización
**Qué hace:** código y textos, puntuaciones de habilidad 75–82

La generación anterior de esa misma línea: 27 000 millones de parámetros, cuantización
**UD-Q4_K_XL**, funciona a través de **llama.cpp / llama-server**. En calidad va por detrás de
Qwen3.8-27B-Local (código 82 frente a 87) con los mismos requisitos de equipo, así que se mantiene
sobre todo por compatibilidad: si un encargo ya está comprobado con esta versión, no hay por qué
cambiarle el modelo. No hay que pagar, y **los datos no salen del ordenador**.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos de IA → Qwen3.6-27B-Local → «Instalar»**. Se instalan:

* el **paquete llama.cpp** (unos 0,5 GB), una compilación con `llama-server`, común con los demás
  modelos locales: si ya está instalado, no se descarga por segunda vez;
* el **archivo de pesos** `Qwen3.6-27B-UD-Q4_K_XL.gguf`, de **16,4 GiB** (17 612 564 704 bytes),
  en el repositorio de modelos (`storage.modelsRepo`, por defecto `C:\ai` en Windows y `~/ai` en
  Linux/macOS, grupo `Qwen3.6`).

La descarga es reanudable y lo descargado se coteja por el tamaño exacto. **Mientras los archivos
no estén en su sitio, el modelo no puede estar activo**: es una regla de AI2P, no un error.

El comando de arranque que se escribe en el perfil después de la instalación:

```
llama-server.exe -m <ruta al .gguf> -ngl 99 -c 65536 --jinja --port 8082 --alias qwen3.6-27b
```

El puerto **8082** es propio de este registro (en los modelos locales vecinos son el 8081 y el
8083): dos `llama-server` no se levantan juntos en un mismo puerto, y el segundo modelo
respondería en silencio con las respuestas del primero.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/Qwen/Qwen3.6-27B/blob/main/LICENSE> |
| Pago por la generación | no lo hay: calcula su ordenador |

AI2P no descarga los pesos originales, sino una cuantización GGUF
(`unsloth/Qwen3.6-27B-GGUF`), que está publicada bajo la misma licencia que el modelo original
(`Qwen/Qwen3.6-27B`). La licencia se tomó de la ficha del modelo el 27.08.2026 con una petición a
HuggingFace (el campo `cardData.license`), no de memoria: en los modelos abiertos cambia poco,
pero antes de un lanzamiento comercial compruebe la ficha otra vez.

El propio motor **llama.cpp está bajo MIT** (el archivo `LICENSE` del repositorio
ggml-org/llama.cpp, cotejado el 27.08.2026), así que tampoco impone ninguna condición a su
producto.

## Requisitos de equipo

| | |
|---|---|
| Espacio en disco | **desde 20 GB** para el modelo (pesos + paquete llama.cpp) |
| Memoria de vídeo | **24 GB**: todo el modelo en la tarjeta gráfica |
| | **16 GB**: parte de las capas se descarga a la RAM, bastante más lento |
| | **menos de 12 GB**: coja una cuantización menor (`UD-Q3_K_XL`) con un registro propio del catálogo |
| Memoria RAM | **desde 32 GB** |
| Procesador | cuantos más núcleos, más rápido en modo CPU |

Las cifras son una **estimación por el tamaño de los pesos, no se han hecho mediciones**. El
contexto del comando de arranque es de 65 536 tokens (`-c 65536`); cuanto mayor sea, más memoria
de vídeo se va a la caché KV.

**El primer arranque es largo:** la carga de los pesos lleva minutos, no es que se haya colgado.

## Límites y coste

| | |
|---|---|
| Contexto | 65 536 tokens (`-c 65536` en el comando de arranque) |
| Máximo de respuesta | 32 768 tokens (`params.maxTokens`) |
| Coste | 0: calcula su ordenador |

## Errores frecuentes

* **«N tokens exceeds context 32768»**: el `-c` del comando de arranque es pequeño; ponga
  `-c 65536` y reinicie el trabajo del equipo.
* **Resultado vacío, motivo `length`**: `params.maxTokens` es pequeño; póngalo en 32768.
* **Los dos modelos Qwen responden igual**: han coincidido los puertos de los comandos de
  arranque; este registro debe tener el 8082.
* **Falta memoria**: cierre lo que sobre o coja una cuantización menor.
