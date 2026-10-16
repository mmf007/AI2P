# AI2P security policy

## Supported versions

Security fixes are released only for the **latest released version** of AI2P (the Releases page of
the repository). Earlier versions are not fixed — update to the latest one: installing over a
previous version keeps the data, the settings and the keys.

| Version | Supported |
|---|---|
| the latest release | yes |
| all earlier ones | no |

## How to report a vulnerability

**Do not open a public issue** and do not write about a vulnerability in Discussions: while there is
no fix, a public description helps whoever wants to exploit it.

Report it privately in one of these ways:

1. **GitHub → the Security tab → Report a vulnerability** (Private vulnerability reporting) — the
   main way. The report is seen only by the maintainers of the repository.
2. **Email** — to the address given in the profile of the repository owner on GitHub, with the
   subject "AI2P security".

What to attach to the report:

* the AI2P version (the `version.json` file in the installation directory) and the OS;
* what exactly is vulnerable and what the threat is;
* the steps to reproduce or an example (a request, a file, a task text);
* if you know one — a suggested fix.

## Response times

* acknowledgement of receipt — within **7 days**;
* a first assessment (a vulnerability or not, how serious) — within **30 days**;
* the fix comes out in the nearest release; we will agree on the date with you, and we ask you not to
  publish the details until the fix is out. If you wish, we will credit you in the release notes.

## The threat model: what AI2P protects and what it does not

AI2P is a local program: the server runs on your computer, and the AI agents it starts run on it as
well. Hence the boundaries of protection that you need to know about in advance.

**A CLI agent runs as the server's user.** A command-line agent (Claude Code and other CLI
executors) is started as a separate process under the same OS account as the AI2P server, without a
sandbox and without separate isolation. The security rules of a task (the allowed and forbidden
directories, the actions) apply to the tools of AI2P itself and are passed to the agent in the job,
but **the OS does not enforce them**: the agent's own tools (reading files, search, the shell) are
not limited to the project's working directory. This means the agent **can read files outside the
project** — everything available to that account, including keys and tokens in the home directory.
The distribution's profiles start the agent in editing mode without confirmations.

What follows from this in practice:

* run the AI2P server under a **separate OS account** with no access to other people's and personal
  data, if the agent is not trusted with everything on the machine; on Linux it can be run in a
  container with only the project directories mounted inside;
* do not keep secrets on this machine that the agent must not see;
* the "deny"/"ask" rules for a CLI agent are an agreement, not a barrier; the barrier is the OS
  permissions;
* the text of a task imported from an external system (Trello, GitHub, GitLab, Figma) is input from
  an outsider: it may contain instructions that the agent will carry out (prompt injection). Check
  tasks from foreign sources before handing them to an agent.

**The server and the network.** By default the UI and the API listen on `localhost`. Access to
`/api` is closed by a login. When opening the server to the network (several servers, access from a
phone), turn on HTTPS (see "HTTPS" in the administrator's guide) and do not publish the server to the
internet without a reverse proxy with its own protection.

**Secrets.** The API keys and the administrator password are stored on disk in the installation
directory (`secrets.json`, `secrets/`) in plain text under the permissions of the server's user (on
Linux and macOS — `0600`). Their protection is the file system permissions and disk encryption.

We consider a vulnerability to be a bypass of these boundaries: access to the API without a login, an
agent going beyond the rules where AI2P promises to enforce them with its own tools, a leak of
secrets into the logs, tasks or a replica on another server, code execution from import or
replication data, and the like. A CLI agent reading the files of its own account is the property
described above, not a vulnerability.
