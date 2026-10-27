# CalamityShock - release branch

This branch is written by GitHub Actions and contains only `CalamityShock.dll`, built from the latest `main`.
It exists so a TShock server can keep its plugin up to date with git. The source code is on the `main` branch.

## Install (once), straight into ServerPlugins/

Run these inside your server's `ServerPlugins/` folder (the one that already contains `TShockAPI.dll`):

```bash
git init
git remote add origin https://github.com/<you>/CalamityShock.git
git fetch origin release
git checkout release
```

`CalamityShock.dll` is now next to `TShockAPI.dll`. The `.gitignore` on this branch ignores everything else in the
folder, so `git status` stays clean. Start (or restart) the server; it writes its files to `tshock/CalamityShock/`.

## Update

```bash
cd ServerPlugins
git pull
```

then restart the server (plugins are loaded at start-up).
