# HTTPS

Por defecto AI2P habla por **http**: las contraseñas, las claves de API y el contenido de las
tareas viajan por la red en texto claro. En un solo ordenador eso no afecta a nadie: el tráfico
no sale del bucle local. En cuanto al servidor se accede desde otras máquinas (el clúster, un
teléfono, los compañeros), ya toca cifrar la conversación.

Este capítulo trata de cómo activar **https**: de dónde sacar el certificado, qué escribir en
los ajustes, qué habrá que hacer una vez **en el navegador** y qué en los **servidores vecinos**
del clúster.

---

## 1. En breve

**La forma más sencilla es un botón.** En el formulario del servidor local, con el
protocolo `https` y el origen «archivo», hay un botón **«Crear un certificado»** (§ 3):
el programa emite su propia autoridad de certificación y un certificado de servidor firmado
por ella para todos los nombres y direcciones de este servidor, y rellena los campos de
archivo por sí mismo. Solo queda instalar el archivo de la autoridad en el navegador (§ 5).
El resto de este capítulo es para quien ya tiene un certificado o necesita otro distinto.

1. Consiga un certificado: de una autoridad de certificación (Let's Encrypt y similares), del
   administrador de su red, o háganse uno usted mismo (§ 3).
2. Póngalo como archivo junto a `config.json` **o** instálelo en el almacén de certificados del
   equipo.
3. Ajustes → **Servidores** → formulario del servidor local: protocolo **https**, y más abajo la
   sección **«Certificado HTTPS»**. Guarde y **reinicie** AI2P.
4. Si el certificado no es de una AC global, permítalo **en el navegador** (§ 5). A los vecinos
   del clúster eso no los detiene: la casilla **«Confiar en el certificado de los vecinos del
   clúster»** está marcada por defecto desde la versión 1.129 (§ 6).

La sección del certificado se ve **sólo cuando está elegido el protocolo https**: un servidor
http no tiene certificado y no hay por qué hablar de él. La casilla «Confiar en el certificado de los vecinos del clúster» es una excepción: está DEBAJO de la sección y se ve con cualquier protocolo propio (§ 6).

---

## 2. De dónde sale el certificado

Los orígenes son dos y se eligen en ese mismo formulario, en el campo **«De dónde tomarlo»**.

### Archivo

El caso normal. Sirven:

| Qué | Campos del formulario |
|---|---|
| `.pfx` / `.p12`: certificado y clave privada en un mismo archivo, normalmente con contraseña | **Archivo del certificado**, **Contraseña del archivo .pfx** |
| Par PEM: `cert.crt` (o `fullchain.pem`) y un `privkey.pem` aparte | **Archivo del certificado** y **Archivo de la clave privada** |
| PEM en el que la clave está en el mismo archivo | sólo **Archivo del certificado** |

La ruta se puede escribir **relativa**: se cuenta desde `config.json` (es decir, desde la
carpeta de trabajo de la instalación, véase [Instalación](install.md)); una ruta absoluta y
`~/…` se toman tal cual.

**La contraseña del `.pfx` no llega nunca a `config.json`**: se va a los secretos
(`secrets/https.certPassword.json`) y en la configuración sólo queda una referencia. Un campo de
contraseña vacío al guardar significa «no cambiar», igual que en la contraseña del correo.

### Almacén del sistema

Un certificado **ya instalado en este ordenador**:

* **Windows**: el complemento «Certificados» (`certmgr.msc` para el usuario, `certlm.msc` para
  el equipo);
* **macOS**: el llavero (Keychain);
* **Linux**: el almacén .NET del usuario actual
  (`~/.dotnet/corefx/cryptography/x509stores`).

Así se hace allí donde la clave privada **no debe estar en un archivo del disco**: por ejemplo,
cuando el certificado lo emite el dominio y lo renueva una directiva de grupo. Los campos:

* **Almacén**: `CurrentUser` (por defecto) o `LocalMachine`. `LocalMachine` en Linux y macOS
  suele ser inaccesible para un usuario normal: ponga el certificado en el almacén **del usuario
  bajo el que corre AI2P** (en el servicio, su cuenta; véase
  [Ejecutar como servicio](service.md)).
* **Sección del almacén**: `My` (certificados personales), el caso normal.
* **Nombre del certificado (CN)**: una parte del nombre; vacío: se busca por el **nombre de
  host** de este servidor.
* **Huella**: más precisa que el nombre y no se confunde cuando hay varios certificados para un
  mismo nombre (el antiguo y el renovado). Si se indica, el nombre no se mira.

Se toma sólo un certificado **con clave privada**; si hay varios adecuados, el de fecha de
caducidad más tardía.

---

## 3. Cómo hacerse uno mismo un certificado

### Con un botón en el propio programa (lo más sencillo)

Ajustes → **Servidores** → formulario del servidor local → protocolo **https** → «De dónde
tomarlo» = **archivo** → botón **«Crear un certificado»**. El programa hace DOS certificados
a la vez, exactamente lo que exige el navegador:

* `certs/ai2pCA.crt` y `certs/ai2pCA.key` — **su propia autoridad de certificación**. Se
  instala en el navegador (§ 5) y en las raíces de confianza de los servidores vecinos
  (§ 6); el enlace **«Descargar el certificado de su autoridad»**, junto al botón, entrega
  ese archivo directamente al navegador.
* `certs/ai2p.crt` y `certs/ai2p.key` — el certificado del **servidor**, firmado por esa
  autoridad: `CA:FALSE`, `extendedKeyUsage=serverAuth` y `subjectAltName` con todos los
  nombres del servidor: el nombre de los ajustes, el segundo nombre (externo), el nombre de
  la máquina, sus direcciones en la red, `localhost` y `127.0.0.1`. Válido 825 días.

Los campos «Archivo del certificado» y «Archivo de la clave privada» se rellenan solos; solo
queda **guardar el formulario y reiniciar** AI2P. Pulsar de nuevo el botón reemite solo el
certificado del servidor, y la **autoridad se reutiliza**: no hace falta volver a recorrer
los navegadores. Así también se añade un nombre o una dirección nueva al certificado:
corrija los nombres del servidor en el formulario y pulse el botón otra vez.

> **Para esto no hace falta ninguna autoridad de certificación en funcionamiento.** El
> navegador no «acude a la autoridad» ni le pide permiso: comprueba la **firma** del
> certificado del sitio con la clave pública de la autoridad, de forma local y sin red. Por
> eso «su propia autoridad» es aquí un simple par de archivos, no un servicio que haya que
> levantar y mantener encendido. De la persona se requiere exactamente una cosa: colocar una
> vez el archivo de la autoridad en el almacén del navegador.

Un certificado propio hace falta cuando el servidor está en la red local y una AC global no
puede emitir nada para él (`ai2p.local`, `192.168.1.10`).

### Windows, con el PowerShell integrado

```powershell
$cert = New-SelfSignedCertificate -DnsName "ai2p.local","192.168.1.10" `
        -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(3)
$pwd = ConvertTo-SecureString -String "contraseña" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "C:\ai\AI2P\ai2p.pfx" -Password $pwd
```

El certificado queda a la vez **en el almacén** (`CurrentUser\My`) **y como archivo**: sirve
cualquiera de los dos orígenes. La huella la imprime `$cert.Thumbprint`.

### Linux / macOS, con openssl

```sh
# SUSTITUYA estas dos líneas por el nombre y la dirección de SU servidor: exactamente lo que
# escribe en la barra de direcciones del navegador. Copiadas tal cual, emiten un certificado
# para un nombre ajeno y el navegador lo rechazará por discrepancia de nombre
HOST=ai2p.local
IP=192.168.1.10

# 1) su propia autoridad de certificación: es ELLA la que se instala en el navegador
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout ai2pCA.key -out ai2pCA.crt \
    -subj "/CN=AI2P local CA" \
    -addext "basicConstraints=critical,CA:TRUE" \
    -addext "keyUsage=critical,keyCertSign,cRLSign"

# 2) el certificado del PROPIO servidor, firmado por esa autoridad
openssl req -newkey rsa:2048 -nodes -keyout ai2p.key -out ai2p.csr -subj "/CN=$HOST"
cat > ai2p.ext <<EOF
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:$HOST,IP:$IP,DNS:localhost,IP:127.0.0.1
EOF
openssl x509 -req -in ai2p.csr -CA ai2pCA.crt -CAkey ai2pCA.key -CAcreateserial \
    -days 825 -out ai2p.crt -extfile ai2p.ext

# 3) compruebe que el certificado lleva SUS nombres y no el ejemplo de la documentación
openssl x509 -in ai2p.crt -noout -text | grep -A1 "Alternative Name"
```

Saldrán `ai2p.crt` y `ai2p.key`: ese es el par PEM para el formulario («Archivo del certificado» y
«Archivo de la clave privada»), y `ai2pCA.crt` es lo que se instala en el navegador (apartado 5).
El archivo `ai2p.csr` es una «solicitud de firma» intermedia: no se indica en ningún ajuste y, una
vez emitido el certificado, no hace falta. El par del servidor se junta en un solo archivo `.pfx` así:

```sh
openssl pkcs12 -export -out ai2p.pfx -inkey ai2p.key -in ai2p.crt -passout pass:contraseña
```

`openssl` lo instala el script `install_required.sh` (en Windows, `install_required.bat`, donde
no es obligatorio: PowerShell sabe hacer lo mismo).

> **`subjectAltName` es obligatorio.** Los navegadores llevan muchos años sin mirar el `CN` en
> absoluto: sin la lista de nombres (`DNS:` e `IP:`) el certificado será rechazado incluso
> después de haberlo permitido.

> **Un solo certificado autofirmado no le basta al navegador.** La orden `openssl req -x509` crea
> un certificado de AUTORIDAD DE CERTIFICACIÓN (`basicConstraints=CA:TRUE`), y los navegadores se
> niegan a aceptar ese certificado como certificado del SITIO, incluso cuando ese mismo certificado
> se ha añadido a las autoridades de confianza. Firefox lo llama
> `MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY` y en pantalla escribe «no está conectado de forma
> segura a este sitio», sin explicar nada. Por eso se hacen dos certificados: la autoridad y el
> certificado de servidor firmado por ella. AI2P avisa de esto por su cuenta: en el formulario del
> servidor local, debajo de la línea «El certificado se lee».

> **Hay que entrar por un nombre del certificado.** En `subjectAltName` se enumeran los nombres por
> los que se puede llamar al servidor, y `localhost` normalmente no está entre ellos: por eso el
> navegador rechaza `https://localhost:5480` por discrepancia de nombre y abre
> `https://ai2p.local:5480`. Desde la versión 1.125 AI2P abre el navegador en el nombre del servidor
> indicado en los ajustes y no en el bucle local; pero si quiere escribir el nombre a mano,
> añádalo al SAN y a `hosts` (o al DNS).

### Certificado de Let's Encrypt

Si el servidor tiene un nombre real en internet, emita un certificado normal (`certbot`) e
indique sus archivos: `fullchain.pem` en «Archivo del certificado» y `privkey.pem` en «Archivo
de la clave privada». Después de cada renovación hay que **reiniciar** AI2P: el certificado se
lee una sola vez, al arrancar.

---

## 4. Qué comprueba el propio programa

La comprobación es la misma en tres sitios, y está hecho a propósito.

* **El formulario del servidor local** no deja guardar «https» con un certificado inservible. El
  motivo se escribe con palabras: «no existe el archivo del certificado: …», «el certificado no
  tiene clave privada», «en el almacén no hay ningún certificado que responda a …», «en los .pfx
  la causa habitual es una contraseña incorrecta». De otro modo el siguiente arranque no
  levantaría, y ya no se podría entrar a corregirlo.
* **El asistente de primer arranque** se niega igual, en la primerísima pantalla que la persona
  ve en su vida.
* **El arranque del servidor** imprime el certificado encontrado («certificado CN=…, válido
  hasta …, huella …») o el motivo del rechazo, y se detiene: caerse en silencio a http cuando se
  pedía https es peor que no levantar.

En el formulario, mientras está elegido https, se muestra también la **línea de estado**: verde
para «El certificado se lee: …», amarilla para «No hay certificado: …». Responde a la pregunta
«¿y funcionará?» antes de reiniciar, no después.

La dirección, el puerto y el **protocolo se aplican sólo tras reiniciar**: el proceso ya está
escuchando el puerto anterior.

---

## 5. Configuración del navegador

Un certificado de una AC global no hay que explicárselo al navegador. Uno **propio** (o del
dominio, o de otra AC no global) sí: de lo contrario, en cada visita el navegador recibe con una
página de «La conexión no es privada».

La forma correcta es añadir **una sola vez** el certificado (o el certificado de su AC) a los de
confianza. Eso se hace **en el ordenador desde el que se mira**, no en el servidor.

> **En el navegador se instala SOLO el archivo de la autoridad: `ai2pCA.crt`.** El certificado del
> servidor `ai2p.crt` ni hace falta importarlo ni se puede: el navegador admite en «Autoridades»
> únicamente un certificado con `CA:TRUE`, y el del servidor es a propósito `CA:FALSE`; Firefox
> responde «Este no es un certificado de una autoridad de certificación, por lo que no se puede
> importar a la lista de autoridades». El propio servidor entrega el archivo correcto: junto al
> botón «Crear certificado» está el enlace **«Descargar el certificado de su autoridad»**. Si el
> certificado se hizo con `openssl` y no con el botón, el archivo de la autoridad es el
> `ai2pCA.crt` de la primera llamada (§ 3), nunca `ai2p.crt`.

**Windows (Chrome, Edge, cualquier navegador del sistema).** Copie `ai2pCA.crt` al ordenador →
doble clic → «Instalar certificado» → «Equipo local» → «Colocar todos los certificados en el
siguiente almacén» → **«Entidades de certificación raíz de confianza»**. Lo mismo desde
PowerShell como administrador:

```powershell
Import-Certificate -FilePath ai2pCA.crt -CertStoreLocation Cert:\LocalMachine\Root
```

**macOS (Safari, Chrome).** Doble clic en `ai2pCA.crt` → el certificado va al llavero → ábralo →
«Confianza» → «Al usar este certificado» → **«Confiar siempre»**.

**Linux (Chrome/Chromium).** Configuración → «Privacidad y seguridad» → «Seguridad» →
«Administrar certificados» → pestaña «Entidades» → «Importar» → marcar «Confiar en este
certificado para identificar sitios web». Para todo el sistema:

```sh
sudo cp ai2pCA.crt /usr/local/share/ca-certificates/ai2pCA.crt && sudo update-ca-certificates
```

**Firefox** lleva **su propio** almacén y no lee el del sistema: Ajustes → «Privacidad y
seguridad» → «Certificados» → «Ver certificados» → «Autoridades» → «Importar».

> **La casilla del diálogo de importación es obligatoria.** Al elegir el archivo, Firefox
> pregunta «¿Confiar en esta CA para identificar sitios web?» (*Trust this CA to identify
> websites*), y la casilla está **desmarcada** por omisión. Sin ella el certificado sí queda
> en la lista «Autoridades» y se ve perfectamente, pero no se confía en él para los sitios y
> la página sigue mostrando la advertencia. Esta es la causa más frecuente de «instalé la CA
> y Firefox sigue quejándose». Para comprobarlo y arreglarlo sin volver a importar: elija su
> CA en la lista «Autoridades» → «Editar confianza» → marcar «Este certificado puede
> identificar sitios web».

**Cuál es el error en realidad.** En la página de advertencia pulse «Avanzado»: abajo aparece
el código, y por él se ve qué hay que arreglar:

| Código | Qué es | Qué hacer |
|---|---|---|
| `SEC_ERROR_UNKNOWN_ISSUER` | la CA no está en el almacén **o** no tiene marcada la confianza para sitios | instalar `ai2pCA.crt` y marcar la casilla (arriba) |
| `SSL_ERROR_BAD_CERT_DOMAIN` | el nombre que escribió no está en `subjectAltName` | entrar por un nombre del certificado o añadir el nombre en los ajustes del servidor y pulsar «Crear certificado» otra vez |
| `MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY` | el servidor entrega el certificado de la **CA** en lugar del del servidor | § 3: hacen falta dos certificados, lo más sencillo es el botón |
| `SEC_ERROR_EXPIRED_CERTIFICATE` | ha caducado | volver a emitirlo (el botón) |

Dos detalles más del mismo sitio: el certificado se lee **al arrancar**, así que después de
«Crear certificado» hay que guardar el formulario y **reiniciar** AI2P — si no, el navegador
ve el archivo anterior; y una «excepción» añadida antes para esta dirección se quita en
Ajustes → «Privacidad y seguridad» → «Ver certificados» → **«Servidores»**.

**Android / iOS.** El archivo `.crt` se abre en los ajustes del sistema («Instalar certificado»
/ perfil de configuración); en Android, para Chrome, el certificado hay que instalarlo
precisamente como «Certificado de CA», porque si no se instala «para VPN» y el navegador no lo
ve.

Tres cosas con las que se tropieza más a menudo:

1. **El nombre debe coincidir.** Un certificado permitido no salva nada si usted entra por
   `https://192.168.1.10` y en el certificado sólo está `DNS:ai2p.local`. Ponga ambos nombres en
   `subjectAltName`, o entre por el nombre para el que se emitió el certificado.
2. **«Continuar de todos modos» no es una solución.** La excepción vive hasta que se reinicia el
   navegador, y algunos navegadores rompen con ello la conexión WebSocket de la que vive la
   interfaz: la pantalla se abrirá y se quedará congelada.
3. **El nombre del servidor en los ajustes debe ser el mismo** que en el certificado: los
   enlaces a las tareas se construyen a partir del campo «Nombre de host» (véase
   [Varios servidores](servers.md)).

---

## 6. Clúster por HTTPS

> **La casilla se ve siempre, no solo con `https` propio.** El ajuste trata de las llamadas
> **salientes**: un servidor que trabaja por `http` llama igualmente a su vecino por `https`
> y tropieza igualmente con su certificado casero («The remote certificate is invalid
> because of errors in the certificate chain: UntrustedRoot»). Antes de la versión 1.126 la
> casilla estaba dentro de la sección del certificado: en un servidor http no se veía en
> absoluto y la única forma de activarlo era editar `config.json` a mano. Se aplica **de
> inmediato**, sin reiniciar.

Los servidores se llaman entre sí **por su cuenta**, y allí no hay quien confirme una excepción:
si el certificado no es de una AC global, la replicación se detiene con un error de verificación
para siempre, hasta que se corrijan los ajustes.

Por eso en la sección hay una casilla **«Confiar en el certificado de los vecinos del clúster»**
(`ui.https.trustAnyPeer`, **marcada por defecto desde la versión 1.129**). Activada, quita la
verificación del certificado **sólo para las llamadas servidor-servidor**: solicitudes, replicación
y transferencia de archivos; no afecta ni al navegador ni a las llamadas a los proveedores de
modelos.

El valor por defecto cambió porque el certificado de nuestros servidores casi siempre es propio
(§ 3): con la casilla desmarcada, el primer servidor que se conectaba a un clúster con https se
detenía con «el certificado remoto ha sido rechazado», y no había forma de adivinar dónde buscar
la causa. Al clúster el servidor sólo entra con el secreto común de todos modos. Si quiere una
verificación estricta de la cadena, reparta el certificado de su AC por todas las máquinas del
clúster (§ 5) y **desmarque** la casilla: el desmarcado sobrevive a la actualización.

Dos reglas más sobre el clúster:

* El protocolo es una **propiedad del registro del servidor**. A un vecino que en la lista de
  servidores tiene `https` se le llama por https; al pasar un servidor a https, corrija su
  registro también en los vecinos (normalmente llega solo por replicación).
* **La llamada a uno mismo por el bucle local no verifica el certificado.** La interfaz llama a
  su propia API por `localhost`, y ningún certificado corresponde a ese nombre; allí no hay nada
  que verificar, porque al otro extremo está el mismo ordenador. Lo mismo vale para el cliente
  de línea de comandos `ai2p` con el que trabaja el agente de IA.

---

## 7. Qué se edita en `config.json`

Con el formulario se edita lo mismo; el archivo hace falta cuando no se puede entrar en el
sistema. **Detenga** el programa antes de editarlo.

```json
{
  "ui": {
    "protocol": "https",
    "port": 5480,
    "hostname": "ai2p.local",
    "https": {
      "source": "file",
      "certFile": "ai2p.pfx",
      "keyFile": "",
      "passwordRef": "https.certPassword",
      "storeLocation": "CurrentUser",
      "storeName": "My",
      "subject": "",
      "thumbprint": "",
      "trustAnyPeer": true
    }
  }
}
```

* `source`: `file` (por defecto) o `store`. Un valor vacío o desconocido se lee como `file`: la
  configuración de versiones anteriores, donde sólo había dos rutas, funciona como antes.
* `passwordRef` es una **referencia** a la contraseña en los secretos, no la contraseña. El
  valor se introduce en el formulario o se pone a mano en
  `secrets/https.certPassword.json`.
* El análisis es sensible a la extensión: `.pfx`/`.p12` se leen como PKCS#12 y todo lo demás,
  como PEM.

---

## 8. Si no ha levantado

| Qué se ve | Qué es | Qué hacer |
|---|---|---|
| «HTTPS: no se ha indicado el certificado» | el protocolo es `https` y la sección está vacía | indicar un archivo o elegir el almacén |
| «HTTPS: no existe el archivo del certificado: …» | la ruta no es correcta | la ruta se cuenta **desde `config.json`**; compruebe la ruta completa del mensaje |
| «HTTPS: no se ha podido leer el certificado …» | el `.pfx` tiene una contraseña incorrecta o el archivo no es PKCS#12 | comprobar la contraseña; a un archivo PEM le hace falta `keyFile` |
| «HTTPS: el certificado no tiene clave privada» | se ha indicado un solo `.crt` sin la clave | añadir `keyFile` o coger un `.pfx` |
| «HTTPS: en el almacén …/… no hay ningún certificado …» | no es esa sección, no es ese usuario o no hay clave | cotejar el almacén y la huella; el servicio mira en el almacén de **su** cuenta |
| En el navegador, «La conexión no es privada» | el navegador no conoce el certificado | § 5 |
| La pantalla se ha abierto y se ha congelado | el navegador ha cortado el WebSocket por la excepción temporal | no «continuar de todos modos», sino permitir el certificado como en el § 5 |
| La replicación se ha detenido con un error de certificado | el vecino no confía en nuestra AC | § 6 |

---

## Y después

* [Configuración](config.md): los demás campos de `config.json` y la pantalla de ajustes.
* [Varios servidores](servers.md): el clúster, la replicación y el formulario del servidor
  local.
* [Ejecutar AI2P como servicio del sistema operativo](service.md): con qué cuenta funciona el
  servicio (de ello depende el almacén de certificados).
* [Instalación de AI2P y dónde están sus datos](install.md): dónde están `config.json` y
  `secrets/`.
