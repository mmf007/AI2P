# Qwen3.8-27B-Local

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: openai-compatible`, `baseUrl: http://localhost:8081/v1`,
modelo `qwen3.8-27b`
**Clave de API:** no hace falta; el servidor local se levanta sin autorización
**Qué hace:** código y textos, puntuaciones de habilidad 80–87

La mejor opción «para una sola tarjeta gráfica» del catálogo: 27 000 millones de parámetros,
cuantización **UD-Q4_K_XL**, funciona a través de **llama.cpp / llama-server**. En código
(puntuación 87) adelanta a Qwen3.6-27B-Local y a Muse-Glimmer-30B-Local, y además no hay que
pagar nada: **los datos no salen del ordenador**. Elección razonable para las tareas que no se
pueden dar a la nube.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos de IA → Qwen3.8-27B-Local → «Instalar»**. Se instalan:

* el **paquete llama.cpp** (unos 0,5 GB), una compilación con `llama-server`;
* el **archivo de pesos** `Qwen3.8-27B-UD-Q4_K_XL.gguf`, de **16,7 GiB** (17 923 394 624 bytes),
  en el repositorio de modelos (`storage.modelsRepo`, por defecto `C:\ai` en Windows y `~/ai` en
  Linux/macOS, grupo `Qwen3.8`).

La descarga es reanudable: una instalación interrumpida continúa, lo ya descargado no se vuelve a
descargar y lo descargado se coteja por el tamaño exacto. **Mientras los archivos no estén en su
sitio, el modelo no puede estar activo**: es una regla de AI2P, no un error.

Después de la instalación se escribe en el perfil el comando de arranque:

```
llama-server.exe -m <ruta al .gguf> -ngl 99 -c 65536 --jinja --port 8081 --alias qwen3.8-27b
```

El puerto **8081** es propio de este registro: en los modelos locales vecinos son el 8082 y el
8083, porque si no, un segundo `llama-server` no podría levantarse junto al primero. AI2P arranca
ese proceso al poner en marcha el trabajo del equipo y lo descarga al detenerlo, **precisamente su
propio proceso**: un llama-server ajeno en ese mismo puerto no se toca.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/Qwen/Qwen3.8-27B> |
| Pago por la generación | no lo hay: calcula su ordenador |

AI2P no descarga los pesos originales, sino una cuantización GGUF
(`unsloth/Qwen3.8-27B-GGUF`), que está publicada bajo la misma licencia que el modelo original
(`Qwen/Qwen3.8-27B`). La licencia se tomó de la ficha del modelo el 27.08.2026 con una petición a
HuggingFace (el campo `cardData.license`), no de memoria: en los modelos abiertos cambia poco,
pero antes de un lanzamiento comercial compruebe la ficha otra vez.

El propio motor **llama.cpp está bajo MIT** (el archivo `LICENSE` del repositorio
ggml-org/llama.cpp, cotejado el 27.08.2026), así que tampoco impone ninguna condición a su
producto.

## Requisitos de equipo

| | |
|---|---|
| Espacio en disco | **desde 20 GB** para el modelo (pesos + paquete llama.cpp) |
| Memoria de vídeo | **24 GB**: todo el modelo en la tarjeta gráfica (RTX 3090/4090/5090) |
| | **16 GB**: parte de las capas se descarga a la RAM, bastante más lento |
| | **menos de 12 GB**: coja una cuantización menor (`UD-Q3_K_XL`) con un registro propio del catálogo |
| Memoria RAM | **desde 32 GB** |
| Procesador | cuantos más núcleos, más rápido en modo CPU |

Las cifras son una **estimación por el tamaño de los pesos, no se han hecho mediciones**: el
modelo no se ha arrancado en este ordenador. El contexto del comando de arranque es de 65 536
tokens (`-c 65536`); cuanto mayor sea, más memoria de vídeo se va a la caché KV.

**El primer arranque es largo:** la carga de los pesos lleva minutos. AI2P espera a que la API
esté lista y muestra al integrante del equipo en estado «arrancando»: no es que se haya colgado.

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
* **Falta memoria o el sistema se va al archivo de intercambio**: cierre lo que sobre o coja una
  cuantización menor.
* **El puerto 8081 está ocupado**: cambie `--port` en el comando de arranque y el `baseUrl` del
  perfil.
