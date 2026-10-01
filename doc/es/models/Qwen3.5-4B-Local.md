# Qwen3.5-4B-Local

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: openai-compatible`, `baseUrl: http://localhost:8084/v1`,
modelo `qwen3.5-4b`
**Clave de API:** no hace falta — el servidor local arranca sin autorización
**Qué hace:** trabajo ligero con texto y análisis de datos, puntuaciones 62-74

El registro menos exigente con el hardware de todo el catálogo. Lo añadió la tarea
T-347-S0 (23.09.2026) para el PAPEL DE APUNTADOR: antes de un trabajo de medios el
apuntador lee la descripción de la tarea y devuelve un pequeño json de control según el
esquema del perfil del modelo de trabajo. Ese trabajo es siempre el mismo y siempre
sencillo: levantar un modelo de 27 000 millones o pagar la nube no tiene sentido.

4000 millones de parámetros, cuantización **Q4_K_S**, motor **llama.cpp / llama-server**.
El modelo también sirve para tareas pequeñas normales —traducción, resumen, edición de
texto—, pero no conviene para código ni análisis complejo: el catálogo tiene registros más
potentes para eso.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Configuración →
Catálogos → Modelos de IA → Qwen3.5-4B-Local → «Instalar»**. Se instalan:

* el **paquete llama.cpp** (~0,5 GB), una compilación con `llama-server`;
* el **archivo de pesos** `Qwen3.5-4B-Q4_K_S.gguf` — **2,41 GiB** (2 590 430 368 bytes) en
  el repositorio de modelos (`storage.modelsRepo`, grupo `Qwen3.5-4B`).

El tamaño se obtuvo con una petición HEAD a HuggingFace el 23.09.2026: respuesta 200 sin
token, el repositorio `unsloth/Qwen3.5-4B-GGUF` no está restringido por licencia. La
descarga se reanuda y lo descargado se comprueba por el tamaño exacto. **Mientras los
archivos no estén en su sitio el modelo no puede estar activo**: es una regla de AI2P, no
un error.

Tras la instalación se escribe en el perfil el comando de arranque:

```
llama-server.exe -m <ruta al .gguf> -ngl 99 -c 16384 --cache-type-k q8_0 --cache-type-v q8_0 --jinja --port 8084 --alias qwen3.5-4b
```

El puerto **8084** es propio de este registro: 8080-8083 los ocupan los modelos locales
vecinos, de lo contrario un segundo `llama-server` no arrancaría junto al primero.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/unsloth/Qwen3.5-4B-GGUF> |
| Pago por generación | ninguno: calcula su ordenador |

AI2P no descarga los pesos originales sino una cuantización GGUF
(`unsloth/Qwen3.5-4B-GGUF`), publicada bajo la misma licencia que el modelo Qwen original.
La licencia se tomó de la ficha del modelo el 23.09.2026 con una consulta a HuggingFace
(campo `cardData.license`), no de memoria.

El propio motor **llama.cpp está bajo MIT**, así que tampoco impone condiciones a su
producto.

## Requisitos de hardware

| | |
|---|---|
| Espacio en disco | **desde 4 GB** (pesos + paquete llama.cpp) |
| Memoria de vídeo | **3 GB** — todas las capas en la GPU con contexto 16 384 y caché KV `q8_0` |
| | **2 GB** — baje `-c` a 4096 o tome la cuantización `IQ4_XS` como registro propio |
| | sin tarjeta gráfica — funcionará en la CPU, más lento, pero suficiente para el apuntador |
| Memoria RAM | **desde 10 GB** para toda la máquina |
| Procesador | cuantos más núcleos, más rápido en modo CPU |

Las cifras son **una estimación por el tamaño de los pesos y de la caché KV, no se han
hecho mediciones**: el modelo no se ha ejecutado en este ordenador. Los pesos Q4_K_S ocupan
2,41 GiB y la caché de claves y valores con `-c 16384` y `q8_0`, menos de 200 MiB.

## Límites y precio

| | |
|---|---|
| Contexto | 16 384 tokens (`-c 16384` en el comando de arranque) |
| Respuesta máxima | 8 192 tokens (`params.maxTokens`) |
| Precio | 0: calcula su ordenador |

## Este registro NO se retira por antigüedad de versiones

La regla habitual del catálogo es «un modelo con tres versiones posteriores de la misma
familia debe apagarse». A este registro NO se le aplica mientras el archivo de pesos siga
disponible para descarga: su valor no es la calidad sino el tamaño, y el catálogo no tiene
sustituto con esos requisitos de hardware. Solo hay un motivo para apagarlo: que el
repositorio `unsloth/Qwen3.5-4B-GGUF` deje de servir el archivo.

## Errores frecuentes

* **El trabajo de medios pasa a generación sin json de control**: el ejecutor apuntador no
  tiene modelo elegido, o el perfil del modelo de trabajo no lleva `prompter.required`.
* **Falta memoria de vídeo**: baje `-c` en el comando de arranque; el contexto es el
  principal consumidor de memoria más allá de los propios pesos.
* **El puerto 8084 está ocupado**: cambie `--port` en el comando y `baseUrl` en el perfil.
* **Las respuestas son peores de lo esperado**: es un modelo de 4000 millones de
  parámetros; para código y análisis complejo tome registros más potentes.
