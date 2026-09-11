# HTTPS

By default AI2P talks over **http**: the passwords, the API keys and the contents of the tasks go
over the network in plain text. On a single computer that concerns nobody — the traffic does not
leave the loopback. As soon as the server is reached from other machines (a cluster, a phone,
colleagues), it is time to encrypt the conversation.

This chapter is about how to switch **https** on: where to get a certificate, what to type into the
settings, what has to be done once **in the browser** and what on the **neighbouring servers** of
the cluster.

---

## 1. In brief

**The simplest way is a button.** In the local server form, with the `https` protocol and
the «file» source, there is an **«Issue a certificate»** button (§ 3): the program issues
its own certificate authority and a server certificate signed by it for every name and
address of this server, and fills in the file fields itself. All that is left is to install
the authority file into the browser (§ 5). The rest of this chapter is for those who
already have a certificate or need a different one.

1. Get a certificate: from a certification authority (Let's Encrypt and the like), from your network
   administrator — or make one yourself (§ 3).
2. Put it as a file next to `config.json` **or** install it into the computer's certificate store.
3. Settings → **Servers** → the local server form: the protocol **https**, below it the **"The HTTPS
   certificate"** section. Save and **restart** AI2P.
4. If the certificate is not from a global CA — allow it **in the browser** (§ 5). The cluster peers
   are not held up by it: the **"Trust the certificate of cluster peers"** switch has been on by
   default since version 1.129 (§ 6).

The certificate section is visible **only when the https protocol is chosen**: an http server has no
certificate, and there is nothing to talk about. The «Trust the certificate of cluster peers» switch is an exception: it stands BELOW the section and is visible with any protocol of your own (§ 6).

---

## 2. Where a certificate comes from

There are two sources, and they are chosen in the same form by the **"Where to take it from"** field.

### A file

The usual case. Suitable are:

| What | The form fields |
|---|---|
| `.pfx` / `.p12` — the certificate and the private key in one file, usually under a password | **The certificate file**, **The .pfx file password** |
| A PEM pair — `cert.crt` (or `fullchain.pem`) and a separate `privkey.pem` | **The certificate file** and **The private key file** |
| A PEM in which the key lies in the same file | only **The certificate file** |

The path may be written **relative** — it is counted from `config.json` (that is, from the working
directory of the installation, see [Installation](install.md)); an absolute path and `~/…` are taken
as they are.

**The `.pfx` password never gets into `config.json`** — it goes into the secrets
(`secrets/https.certPassword.json`), and only a reference stays in the configuration. An empty
password field on saving means "do not change it", as with the mail password.

### The system store

A certificate **already installed into this computer**:

* **Windows** — the "Certificates" snap-in (`certmgr.msc` for the user, `certlm.msc` for the
  computer);
* **macOS** — the Keychain;
* **Linux** — the .NET store of the current user
  (`~/.dotnet/corefx/cryptography/x509stores`).

This is done where the private key **must not lie as a file on disk** — for example, when the
certificate is issued by the domain and renewed by a group policy. The fields:

* **The store** — `CurrentUser` (the default) or `LocalMachine`. `LocalMachine` on Linux and macOS is
  usually unavailable to an ordinary user: put the certificate into the store of **the user AI2P runs
  under** (for a service that is its account, see [Running as a service](service.md)).
* **The store section** — `My` (the personal certificates), the usual case.
* **The subject name (CN)** — a part of the name; empty — the **host name** of this server
  is used.
* **The thumbprint** — more precise than a name and does not get confused when there are several
  certificates for one name (an old one and a renewed one). If it is given, the name is not looked at.

Only a certificate **with a private key** is taken; if there are several suitable ones — the one with
the latest expiry date.

---

## 3. How to make a certificate yourself

### With a button in the program itself (the simplest way)

Settings → **Servers** → local server form → protocol **https** → «Where from» = **file** →
the **«Issue a certificate»** button. The program makes TWO certificates at once — exactly
what the browser requires:

* `certs/ai2pCA.crt` and `certs/ai2pCA.key` — **your own certificate authority**. It is
  installed into the browser (§ 5) and into the trusted roots of the neighbouring servers
  (§ 6); the **«Download your authority certificate»** link next to the button hands this
  file straight to the browser.
* `certs/ai2p.crt` and `certs/ai2p.key` — the **server** certificate signed by that
  authority: `CA:FALSE`, `extendedKeyUsage=serverAuth` and a `subjectAltName` with every
  name of the server — the name from the settings, the second (external) name, the machine
  name, its addresses on the network, `localhost` and `127.0.0.1`. Valid for 825 days.

The «Certificate file» and «Private key file» fields are filled in automatically — all that
is left is to **save the form and restart** AI2P. Pressing the button again reissues the
server certificate only, and the **authority is reused**: there is no need to go round the
browsers again. That is also how a new name or address is added to the certificate — correct
the server names in the form and press the button once more.

> **No running certificate authority is needed for this.** The browser does not «go to the
> CA» and does not ask it for permission: it verifies the **signature** of the site
> certificate with the authority's public key — locally, without any network. So «your own
> CA» here is just a pair of files, not a service that has to be brought up and kept
> running. Exactly one thing is required of a human: to put the authority file into the
> browser's store once.

Your own certificate is needed when the server is in a local network and a global CA can issue
nothing for it (`ai2p.local`, `192.168.1.10`).

### Windows — with the built-in PowerShell

```powershell
$cert = New-SelfSignedCertificate -DnsName "ai2p.local","192.168.1.10" `
        -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(3)
$pwd = ConvertTo-SecureString -String "password" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "C:\ai\AI2P\ai2p.pfx" -Password $pwd
```

The certificate lands at once **both in the store** (`CurrentUser\My`) **and as a file** — either of
the two sources will do. The thumbprint is printed by `$cert.Thumbprint`.

### Linux / macOS — openssl

```sh
# REPLACE these two lines with the name and the address of YOUR server — exactly what you
# type in the browser address bar. Copied as they are, they issue a certificate for someone
# else's name, and the browser refuses it on a name mismatch no matter how much you trust it
HOST=ai2p.local
IP=192.168.1.10

# 1) your own certificate authority — this is what goes into the browser
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout ai2pCA.key -out ai2pCA.crt \
    -subj "/CN=AI2P local CA" \
    -addext "basicConstraints=critical,CA:TRUE" \
    -addext "keyUsage=critical,keyCertSign,cRLSign"

# 2) the certificate of the SERVER itself, signed by that authority
openssl req -newkey rsa:2048 -nodes -keyout ai2p.key -out ai2p.csr -subj "/CN=$HOST"
cat > ai2p.ext <<EOF
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:$HOST,IP:$IP,DNS:localhost,IP:127.0.0.1
EOF
openssl x509 -req -in ai2p.csr -CA ai2pCA.crt -CAkey ai2pCA.key -CAcreateserial \
    -days 825 -out ai2p.crt -extfile ai2p.ext

# 3) check that the certificate carries YOUR names, not the example from the documentation
openssl x509 -in ai2p.crt -noout -text | grep -A1 "Alternative Name"
```

You get `ai2p.crt` and `ai2p.key` — that is the PEM pair for the form («Certificate file» and
«Private key file»), and `ai2pCA.crt` is what goes into the browser (section 5). The `ai2p.csr` file
is an intermediate «signing request»: it goes into no setting at all and is not needed once the
certificate is issued. The server pair is put together into a single `.pfx` file like this:

```sh
openssl pkcs12 -export -out ai2p.pfx -inkey ai2p.key -in ai2p.crt -passout pass:password
```

`openssl` is installed by the `install_required.sh` script (on Windows — `install_required.bat`,
where it is not obligatory: PowerShell can do the same).

> **`subjectAltName` is obligatory.** For many years now browsers have not looked at `CN` at all:
> without the list of names (`DNS:` and `IP:`) the certificate will be rejected even after it has
> been allowed.

> **One self-signed certificate is not enough for a browser.** The `openssl req -x509` command makes
> a CERTIFICATE AUTHORITY certificate (`basicConstraints=CA:TRUE`), and browsers refuse to accept
> such a certificate as a SITE certificate — even when the very same certificate has been added to
> the trusted authorities. Firefox calls this `MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY` and
> writes «You are not securely connected to this site» on the screen, explaining nothing. That is
> why two certificates are made: the authority and the server certificate signed by it. AI2P warns
> about this itself — in the local server form, under the «The certificate reads» line.

> **You have to enter by a name from the certificate.** `subjectAltName` lists the names the server
> may be called by, and `localhost` is usually not among them — so the browser refuses
> `https://localhost:5480` on a name mismatch and opens `https://ai2p.local:5480`. Since version
> 1.125 AI2P itself opens the browser at the server name from the settings rather than at the
> loopback — but if you want to type the name by hand, add it to the SAN and to `hosts` (or to DNS).

### A certificate from Let's Encrypt

If the server has a real name on the internet, issue an ordinary certificate (`certbot`) and give its
files: `fullchain.pem` for "The certificate file", `privkey.pem` for "The private key file". After
every renewal AI2P has to be **restarted**: the certificate is read once, at the start.

---

## 4. What the program itself checks

The check is one and the same in three places, and that is done on purpose.

* **The local server form** does not let you save "https" with an unfit certificate. The reason is
  written in words: "there is no certificate file: …", "the certificate has no private key", "there
  is no certificate in the store by the sign …", "for a .pfx a frequent cause is a wrong password".
  Otherwise the next start would not come up, and there would be no way left to sign in and fix it.
* **The first-start wizard** refuses the same way — on the very first screen a person sees in their
  life.
* **The start of the server** prints the certificate it found ("the certificate CN=…, valid until …,
  thumbprint …") or the reason for the refusal, and stops: silently falling back to http when https
  was asked for is worse than not coming up.

While https is chosen, the form also shows a **status line**: green — "the certificate reads: …",
yellow — "there is no certificate: …". It answers the question "will it work at all" before the
restart, not after it.

The address, the port and **the protocol apply only after a restart** — the process is already
listening on the previous port.

---

## 5. Setting up the browser

A certificate from a global CA does not have to be explained to the browser. **Your own** one (or the
domain's, or one from another non-global CA) does: otherwise on every visit the browser meets you with
the "Your connection is not private" page.

The right way is to add the certificate (or the certificate of your CA) to the trusted ones **once**.
That is done **on the computer you look from**, not on the server.

> **ONLY the authority file — `ai2pCA.crt` — goes into the browser.** The server certificate
> `ai2p.crt` neither needs to be imported nor can be: a browser accepts into "Authorities" only a
> certificate with `CA:TRUE`, while the server one is deliberately `CA:FALSE`, and Firefox answers
> it with "This is not a certificate authority certificate, so it can't be imported into the
> certificate authority list". The server hands the right file out itself: next to the "Create
> certificate" button there is the **"Download your own authority certificate"** link. If the
> certificate was made with `openssl` rather than by the button, the authority file is the
> `ai2pCA.crt` from the first call (§ 3) and never `ai2p.crt`.

**Windows (Chrome, Edge, any system browser).** Copy `ai2pCA.crt` onto the computer → double-click →
"Install certificate" → "Local machine" → "Place all certificates in the following store" →
**"Trusted root certification authorities"**. The same from PowerShell as an administrator:

```powershell
Import-Certificate -FilePath ai2pCA.crt -CertStoreLocation Cert:\LocalMachine\Root
```

**macOS (Safari, Chrome).** A double click on `ai2pCA.crt` → the certificate lands in the keychain →
open it → "Trust" → "When using this certificate" → **"Always Trust"**.

**Linux (Chrome/Chromium).** Settings → "Privacy and security" → "Security" → "Manage certificates" →
the "Authorities" tab → "Import" → tick "Trust this certificate for identifying websites". For the
whole system:

```sh
sudo cp ai2pCA.crt /usr/local/share/ca-certificates/ai2pCA.crt && sudo update-ca-certificates
```

**Firefox** keeps **its own** store and does not read the system one: Settings → "Privacy &
Security" → "Certificates" → "View Certificates" → "Authorities" → "Import".

> **The checkbox in the import dialog is mandatory.** Once the file is chosen, Firefox asks
> "Trust this CA to identify websites?" — and the box is **unchecked** by default. Without it
> the certificate does land in the "Authorities" list and is plainly visible there, but it is
> not trusted for sites, and the page still greets you with a warning. This is the most common
> reason for "I installed the CA and Firefox still complains". To check and fix it without
> importing again: pick your CA in the "Authorities" list → "Edit Trust" → tick "This
> certificate can identify websites".

**What the error actually is.** On the warning page press "Advanced" — the code is printed at
the bottom, and it tells you what to fix:

| Code | What it is | What to do |
|---|---|---|
| `SEC_ERROR_UNKNOWN_ISSUER` | the CA is not in the store **or** it is not trusted for websites | install `ai2pCA.crt` and tick the box (above) |
| `SSL_ERROR_BAD_CERT_DOMAIN` | the name you typed is not in `subjectAltName` | use a name from the certificate, or add the name to the server settings and press "Create certificate" again |
| `MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY` | the server serves the **CA** certificate instead of the server one | § 3: two certificates are needed, the button is the easy way |
| `SEC_ERROR_EXPIRED_CERTIFICATE` | it has expired | reissue it (the button) |

Two more details from the same place: the certificate is read **at startup**, so after "Create
certificate" the form has to be saved and AI2P **restarted** — otherwise the browser sees the
previous file; and an old "exception" added for this address earlier is removed in Settings →
"Privacy & Security" → "View Certificates" → **"Servers"**.

**Android / iOS.** The `.crt` file is opened in the system settings ("Install a certificate" / a
configuration profile); on Android, for Chrome, the certificate has to be installed exactly as a "CA
certificate", otherwise it is installed "for VPN" and the browser will not see it.

The three things people stumble over most often:

1. **The name has to match.** An allowed certificate will not save you if you go to
   `https://192.168.1.10` while the certificate holds only `DNS:ai2p.local`. Put both names into
   `subjectAltName` — or go by the name the certificate was issued for.
2. **"Proceed anyway" is not a solution.** The exception lives until the browser is restarted, and
   some browsers break the WebSocket connection the interface lives on while they are at it: the
   screen will open and freeze.
3. **The server name in the settings has to be the same** as in the certificate: the links to tasks
   are built from the "Host name" field (see [Several servers](servers.md)).

---

## 6. A cluster over HTTPS

> **The switch is visible always, not only with your own `https`.** The setting is about
> **outgoing** calls: a server that itself works over `http` calls its peer over `https`
> just the same and runs into its home-made certificate just the same («The remote
> certificate is invalid because of errors in the certificate chain: UntrustedRoot»). Before
> version 1.126 the switch lived inside the certificate section — on an http server it was
> not visible at all, and the only way to enable it was to edit `config.json` by hand.
> It takes effect **at once**, without a restart.

The servers go to each other **by themselves**, and there is nobody there to confirm an exception: if
the certificate is not from a global CA, the replication stops with a verification error forever,
until the settings are fixed.

That is why the section has a **"Trust the certificate of cluster peers"** checkbox
(`ui.https.trustAnyPeer`, **on by default since version 1.129**). When it is on, it removes the
certificate check **only for the server-to-server calls** — the requests, the replication and the
file transfer; it does not touch the browser or the calls to the model providers.

The default changed because the certificate of our servers is nearly always a self-made one (§ 3):
with the checkbox off, the very first server joining a cluster that runs https stopped with "the
remote certificate is rejected", and there was no way to guess where to look. A server is let into
the cluster by the shared secret anyway. If you want the chain checked strictly, hand the
certificate of your CA out to every machine of the cluster (§ 5) and **clear** the checkbox —
clearing it survives an upgrade.

Two more rules about the cluster:

* The protocol is a **property of the server record**. A neighbour that has `https` in the server list
  is called over https; when moving a server to https, fix its record at the neighbours' too (usually
  it arrives by replication on its own).
* **A call to oneself over the loopback does not check the certificate.** The interface goes to its
  own API over `localhost`, and no certificate matches that name — there is nothing to check there,
  the same computer is at the other end. The same applies to the `ai2p` command-line client the AI
  agent works with.

---

## 7. What is edited in `config.json`

The same things are edited by the form; the file is needed when there is no way into the system.
**Stop** the program before the edit.

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

* `source` — `file` (the default) or `store`. An empty and an unknown value are read as `file`: the
  configuration of the previous versions, where there were only the two paths, works as before.
* `passwordRef` is a **reference** to the password in the secrets, not the password. The value is
  entered in the form or put by hand into `secrets/https.certPassword.json`.
* The parsing is sensitive to the extension: `.pfx`/`.p12` are read as PKCS#12, everything else as
  PEM.

---

## 8. If it did not come up

| What you see | What it is | What to do |
|---|---|---|
| "HTTPS: no certificate is given" | the protocol is `https` and the section is empty | give a file or choose a store |
| "HTTPS: there is no certificate file: …" | the path is wrong | the path is counted **from `config.json`**; check the full path from the message |
| "HTTPS: the certificate … could not be read" | the `.pfx` password is wrong, or the file is not PKCS#12 | check the password; a PEM file needs a `keyFile` |
| "HTTPS: the certificate has no private key" | a single `.crt` without a key was given | add a `keyFile` or take a `.pfx` |
| "HTTPS: there is no certificate … in the store …/…" | the wrong section, the wrong user, or there is no key | check the store and the thumbprint; a service looks into the store of **its own** account |
| "Your connection is not private" in the browser | the browser does not know the certificate | § 5 |
| The screen opened and froze | the browser broke the WebSocket because of a temporary exception | do not "proceed anyway", allow the certificate as in § 5 |
| The replication stopped with a certificate error | the neighbour does not trust our CA | § 6 |

---

## Next

* [Configuration](config.md) — the other fields of `config.json` and the settings screen.
* [Several servers](servers.md) — the cluster, the replication and the local server form.
* [Running AI2P as an operating system service](service.md) — which account the service runs under
  (the certificate store depends on it).
* [Installing AI2P and where its data lives](install.md) — where `config.json` and `secrets/` lie.
