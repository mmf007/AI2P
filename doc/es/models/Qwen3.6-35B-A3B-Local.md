# Qwen3.6-35B-A3B-Local

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: openai-compatible`, `baseUrl: http://localhost:8080/v1`
**Clave de API:** no hace falta; el servidor local se levanta sin autorización
**Qué hace:** código y textos, puntuaciones de habilidad 72–80

Modelo de texto Qwen 3.6 (35 000 millones de parámetros, MoE con 3 000 millones activos) en
cuantización **Q4_K_M**, funciona a través de **llama.cpp / llama-server**. La calidad es menor
que la de los modelos en la nube, pero a cambio es **gratis y no se va a ninguna parte**: los
datos no salen del ordenador. Elección razonable para las tareas que no se pueden dar a la nube.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos de IA → Qwen3.6-35B-A3B-Local → «Instalar»**. Se instalan:

* el **paquete llama.cpp** (unos 640 MB), una compilación con `llama-server`;
* el **archivo de pesos** `Qwen3.6-35B-A3B-UD-Q4_K_M.gguf`, de **unos 20,6 GiB**, en el
  repositorio de modelos (`storage.modelsRepo`, por defecto `C:\ai` en Windows y `~/ai` en
  Linux/macOS, grupo `Qwen3.6`).

La descarga es reanudable: una instalación interrumpida continúa y lo ya descargado no se vuelve a
descargar. Mientras los archivos no estén en su sitio, el modelo no puede estar activo.

Después de la instalación se escribe automáticamente en el perfil el comando de arranque:

```
llama-server.exe -m <ruta al .gguf> --n-cpu-moe 99 -ngl 99 -c 65536 --jinja
                 --port 8080 --alias qwen3.6-35b-a3b
```

AI2P levanta ese proceso al poner en marcha el trabajo del equipo y lo descarga al detenerlo,
**precisamente su propio proceso**: un llama-server ajeno que ya esté funcionando en el 8080 no se
toca.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/Qwen/Qwen3.6-35B-A3B/blob/main/LICENSE> |
| Pago por la generación | no lo hay: calcula su ordenador |

AI2P no descarga los pesos originales, sino una cuantización GGUF
(`unsloth/Qwen3.6-35B-A3B-GGUF`), que está publicada bajo la misma licencia que el modelo original
(`Qwen/Qwen3.6-35B-A3B`). La licencia se tomó de la ficha del modelo el 27.08.2026 con una
petición a HuggingFace (el campo `cardData.license`), no de memoria: en los modelos abiertos
cambia poco, pero antes de un lanzamiento comercial compruebe la ficha otra vez.

El propio motor **llama.cpp está bajo MIT** (el archivo `LICENSE` del repositorio
ggml-org/llama.cpp, cotejado el 27.08.2026), así que tampoco impone ninguna condición a su
producto.

## Requisitos de equipo

| | |
|---|---|
| Espacio en disco | **unos 22 GB** (pesos + paquete) |
| Memoria RAM | **mínimo 24 GB**, cómodo con 32 GB |
| Tarjeta gráfica | no es obligatoria, pero acelera mucho |
| VRAM | desde 6 GB ya da una mejora notable; la opción `--n-cpu-moe 99` mantiene los expertos en la RAM y a la GPU se va lo demás |
| Procesador | cuantos más núcleos, más rápido en modo CPU |

El modelo es **MoE**: de sus 35 000 millones de parámetros hay activos unos 3 000 millones, y por
eso es bastante más rápido que un modelo denso del mismo tamaño. Pero se carga entero en memoria:
los 20,6 GiB de pesos tienen que caber en alguna parte.

**El primer arranque es largo:** la carga de los pesos lleva minutos. AI2P espera a que la API
esté lista y muestra al integrante del equipo en estado «arrancando»: no es que se haya colgado.

## Contexto y máximo de respuesta

| | |
|---|---|
| Contexto | 65 536 tokens (`-c 65536` en el comando de arranque) |
| Máximo de respuesta | 32 768 tokens (`params.maxTokens`) |

Esta pareja está comprobada y es importante: con un contexto **menor** las respuestas se cortaban
con el motivo `length`, porque el modelo quemaba el límite en razonamientos y devolvía un
resultado vacío. Si corrige uno de los números, corrija también el otro.

## Errores frecuentes

* **«37529 tokens exceeds context 32768»**: en el comando de arranque hay un `-c` pequeño. Ponga
  `-c 65536` en el campo «Comando de lanzamiento» del perfil y reinicie el trabajo del equipo.
* **Resultado vacío, motivo `length`**: `params.maxTokens` es pequeño; póngalo en 32768.
* **Falta memoria o el sistema se va al archivo de intercambio**: el modelo necesita del orden de
  24 GB de RAM; cierre lo que sobre o coja una cuantización menor (y entonces habrá que crear un
  registro propio del catálogo con su propio manifiesto).
* **El puerto 8080 está ocupado**: cambie `--port` en el comando de arranque y el `baseUrl` del
  perfil.

## Forma de instalación obsoleta

Los scripts `install_local_model.bat/.ps1/.sh` de la carpeta de la aplicación instalan ese mismo
modelo sin interfaz (para escenarios sin conexión). Crean un **registro personalizado aparte** del
catálogo: no lo confunda con el integrado. Desde la versión 1.42 la forma normal es el botón
«Instalar».
