# VS Code plugin

The plugin prepares this server for linking projects with VS Code (project card → "Connect VS Code").
It has no agent actions — it is responsible for the programs:

| Program | Why | How it is found |
|---|---|---|
| Visual Studio Code | the editor a project is linked to | an installed copy is looked up first (the `code` command in PATH); not found — Install downloads the **latest** stable version as a portable archive into the packages folder |

## Several VS Code copies

If the machine has several copies (VS Code, Insiders, VSCodium, Cursor …), point the
"Visual Studio Code" row of the plugin window at the one to link projects with
(Windows: `<VS Code folder>\bin\code.cmd`). That copy is the **internal** one: the project's copy picker
marks it "internal" and selects it by default; the other copies can still be chosen.

## Turning on

Once VS Code is found or installed the plugin turns itself on — no separate Initialize click is needed.
A plugin disabled by hand is not turned on again automatically.

## Project link

"Connect VS Code" on the project card checks the plugin first. If it is not connected, connecting starts:
what is found is taken, what is missing is installed in the background. Until VS Code is found the project
cannot be connected — the panel says "VS Code is not found on this server".
The action requires the administrator role of this computer (the plugins section).
