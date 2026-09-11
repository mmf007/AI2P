# Muse-Glimmer-30B-Local

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: openai-compatible`, `baseUrl: http://localhost:8083/v1`,
modelo `muse-glimmer-30b`
**Clave de API:** no hace falta; el servidor local se levanta sin autorización
**Qué hace:** textos y código, puntuaciones de habilidad 73–80

Modelo abierto de Meta de 30 000 millones de parámetros en cuantización **UD-Q4_K_XL**, funciona
a través de **llama.cpp / llama-server**. Es el pariente menor del Muse-Spark-1.2 de la nube. En
código es más flojo que los modelos Qwen del mismo tamaño (75–78 frente a 82–87), pero a cambio es
más regular en textos y, como todos los registros locales, **es gratis y no envía datos a ninguna
parte**.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos de IA → Muse-Glimmer-30B-Local → «Instalar»**. Se instalan:

* el **paquete llama.cpp** (unos 0,5 GB), una compilación con `llama-server`, común con los demás
  modelos locales;
* el **archivo de pesos** `Muse-Glimmer-30B-UD-Q4_K_XL.gguf`, de **14,8 GiB** (15 878 222 368
  bytes), en el repositorio de modelos (`storage.modelsRepo`, por defecto `C:\ai` en Windows y
  `~/ai` en Linux/macOS, grupo `Muse-Glimmer`).

Es el archivo más pequeño entre los registros locales del catálogo, así que, si falta memoria de
vídeo, resulta más fácil empezar por él. La descarga es reanudable y lo descargado se coteja por
el tamaño exacto. **Mientras los archivos no estén en su sitio, el modelo no puede estar activo**:
es una regla de AI2P, no un error.

El comando de arranque que se escribe en el perfil después de la instalación:

```
llama-server.exe -m <ruta al .gguf> -ngl 99 -c 65536 --jinja --port 8083 --alias muse-glimmer-30b
```

El puerto **8083** es propio de este registro (en los modelos Qwen son el 8081 y el 8082): dos
`llama-server` no se levantan juntos en un mismo puerto.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/meta-models/Muse-Glimmer-30B> |
| Pago por la generación | no lo hay: calcula su ordenador |

AI2P no descarga los pesos originales, sino una cuantización GGUF
(`unsloth/Muse-Glimmer-30B-GGUF`), que está publicada bajo la misma licencia que el modelo
original (`meta-models/Muse-Glimmer-30B`). La licencia se tomó de la ficha del modelo el
27.08.2026 con una petición a HuggingFace (el campo `cardData.license`), no de memoria: en los
modelos abiertos cambia poco, pero antes de un lanzamiento comercial compruebe la ficha otra vez.
Ambas fichas, la de la cuantización y la del modelo original, indican Apache 2.0.

El propio motor **llama.cpp está bajo MIT** (el archivo `LICENSE` del repositorio
ggml-org/llama.cpp, cotejado el 27.08.2026), así que tampoco impone ninguna condición a su
producto.

## Requisitos de equipo

| | |
|---|---|
| Espacio en disco | **desde 20 GB** (pesos 14,8 GiB + paquete llama.cpp) |
| Memoria de vídeo | **24 GB**: todo el modelo en la tarjeta gráfica, con margen para el contexto |
| | **16 GB**: cabe justo, o con una pequeña descarga de capas a la RAM |
| | **menos de 12 GB**: coja una cuantización menor (`UD-Q3_K_XL`) con un registro propio del catálogo |
| Memoria RAM | **desde 32 GB** |
| Procesador | cuantos más núcleos, más rápido en modo CPU |

Las cifras son una **estimación por el tamaño de los pesos, no se han hecho mediciones**: el
modelo no se ha arrancado en este ordenador. El contexto del comando de arranque es de 65 536
tokens (`-c 65536`); cuanto mayor sea, más memoria de vídeo se va a la caché KV.

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
* **El puerto 8083 está ocupado**: cambie `--port` en el comando de arranque y el `baseUrl` del
  perfil.
* **Falta memoria**: coja una cuantización menor o libere memoria de vídeo.
