# phyloDIVaS installation guide

Use the instructions for your operating system. These commands run phyloDIVaS
on your own computer; GitHub stores the code and saved demo, not a hosted app.
A GitHub account, VS Code, Docker, and a local HyPhy installation are not required.

Copy the commands inside each code block, press Enter, and wait for that block to
finish before continuing. Do not copy Terminal prompts or Markdown formatting.
You will need an internet connection, free disk space for developer tools and
the genome-wide demo, and permission to install software on your computer.

## macOS: first-time installation

These steps use Terminal and the default macOS zsh shell. They cover both Apple
Silicon and Intel Macs. Open **Applications → Utilities → Terminal**.

### 1. Install Homebrew

Homebrew installs the software that phyloDIVaS needs. Run the official installer:

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

Follow its prompts. It may install Apple's Command Line Tools and ask for your
Mac login password. Password characters do not appear while typing; press Enter
after entering the password. On a managed computer, your IT administrator may
need to approve installation.

Then add Homebrew to this Terminal and future Terminal windows:

```bash
if [ -x /opt/homebrew/bin/brew ]; then
  echo 'eval "$(/opt/homebrew/bin/brew shellenv)"' >> "$HOME/.zprofile"
else
  echo 'eval "$(/usr/local/bin/brew shellenv)"' >> "$HOME/.zprofile"
fi
source "$HOME/.zprofile"
```

### 2. Install the required software

```bash
brew install git python@3.12 node@22
brew install --cask dotnet-sdk@8
```

The application currently targets .NET 8 and checks for an installed **8.x SDK**.
Installing only a .NET runtime, or only a newer SDK, does not satisfy that check.
The Homebrew cask may install another SDK alongside version 8; that is acceptable.

Make the selected Node and Python commands available in future Terminal windows:

```bash
echo 'export PATH="$(brew --prefix node@22)/bin:$(brew --prefix python@3.12)/libexec/bin:$PATH"' >> "$HOME/.zprofile"
source "$HOME/.zprofile"
```

### 3. Check the installation

```bash
git --version
python3 --version
node --version
npm --version
dotnet --list-sdks
```

You should see Git, Python 3.12.x, Node v22.x, an npm version, and an **8.x** entry
in the SDK list. If a command is missing, complete the preceding steps before
continuing. A newer SDK may also be listed.

### 4. Download phyloDIVaS

Run this once:

```bash
cd "$HOME"
git clone https://github.com/shrihithgolugula1/phyloDIVaS.git
cd "$HOME/phyloDIVaS"
```

The repository includes the genome-wide saved demo, so the download can take
longer than a small code-only repository. If Git reports that the directory
already exists, use the **Updating an existing installation** instructions below
instead of cloning again. Do not delete an existing installation to solve that error.

### 5. Trust the local development certificate and start the application

```bash
dotnet dev-certs https --trust
python3 scripts/local_app.py --check
python3 scripts/local_app.py --setup
```

Approve the operating system's certificate-trust prompt when asked. The certificate
is used by the local HTTPS APIs. Keep certificate validation enabled.

The check command validates prerequisites and the local configuration file's
format; it does not test database or cloud access. If it reports an error, correct
that error before running setup.

The setup command builds the APIs, installs frontend dependencies, and starts
the app's services in **one Terminal window**. This can take several minutes on
first use. Once startup succeeds, your default browser opens automatically at:

**http://localhost:3000**

Keep the Terminal window open while using phyloDIVaS. You do not need to start
each API separately or open multiple Terminal windows.

## Windows 11: first-time installation

Open **Windows Terminal → PowerShell**. The commands below use Windows Package
Manager (`winget`). If `winget` is not recognized, install or update Microsoft's
[App Installer](https://learn.microsoft.com/windows/package-manager/winget/)
through the Microsoft Store, then reopen PowerShell.

### 1. Install the required software

```powershell
winget install --exact --id Git.Git --source winget
winget install --exact --id Python.Python.3.12 --source winget
winget install --exact --id OpenJS.NodeJS.22 --source winget
winget install --exact --id Microsoft.DotNet.SDK.8 --source winget
```

Complete the installation prompts. **Close and reopen PowerShell** afterward so
that the installed commands are available.

### 2. Verify the commands

```powershell
git --version
py -3.12 --version
node --version
npm --version
dotnet --list-sdks
```

You should see Python 3.12.x, Node v22.x, npm, Git, and an **8.x SDK** entry.
If PowerShell blocks `npm.ps1`, use this verification command instead:

```powershell
npm.cmd --version
```

The launcher can use npm's Windows command shim; changing the system-wide
PowerShell execution policy is not required.

### 3. Download and launch phyloDIVaS

```powershell
Set-Location $HOME
git clone https://github.com/shrihithgolugula1/phyloDIVaS.git
Set-Location "$HOME\phyloDIVaS"
dotnet dev-certs https --trust
py -3.12 scripts/local_app.py --check
py -3.12 scripts/local_app.py --setup
```

Approve certificate trust when Windows asks. If the check command reports an
error, resolve it before running setup. The browser opens automatically at
**http://localhost:3000** once the services are ready. Keep PowerShell open.

Windows launcher conventions and unit checks are included, but a complete
fresh-machine Windows workflow has not been verified to the same extent as the
current macOS installation. Report platform-specific startup errors with the
relevant log file, without sharing credentials.

## Open the saved demo

Once the app is running, open **Run Analysis → View demo results**, or visit:

**http://localhost:3000/Demo**

The saved bat demo needs no Galaxy API key, phyloDIVaS login, database setup, or
new analysis submission. Its report data and pictures are packaged with the code.

## Start the app next time

The prerequisite installation and Git clone are one-time steps. Normally, use:

macOS:

```bash
cd "$HOME/phyloDIVaS"
python3 scripts/local_app.py
```

Windows PowerShell:

```powershell
Set-Location "$HOME\phyloDIVaS"
py -3.12 scripts/local_app.py
```

The launcher opens the browser automatically. If it recognizes a complete local
instance already running, it opens that instance and skips setup.

## Stop the app

Press **Ctrl+C** in the Terminal or PowerShell window that started phyloDIVaS.
This stops the local services started by that launcher. Closing the browser alone
does not stop them. Already submitted Galaxy jobs are managed separately by Galaxy.

## Updating an existing installation

Stop the current launcher with **Ctrl+C** first. Then run:

macOS:

```bash
cd "$HOME/phyloDIVaS"
git pull --ff-only
python3 scripts/local_app.py --setup
```

Windows PowerShell:

```powershell
Set-Location "$HOME\phyloDIVaS"
git pull --ff-only
py -3.12 scripts/local_app.py --setup
```

This fetches the latest published code and demo, rebuilds the APIs, reinstalls the
locked frontend dependencies, and starts the app. If Git reports conflicting
local edits, preserve those edits and resolve the conflict rather than deleting
files or forcing an update.

## Running your own analysis

- Create a [Galaxy Europe](https://usegalaxy.eu/) account and obtain its API key.
- Enter the key in phyloDIVaS's analysis form; do not put it in Terminal commands,
  Git, or configuration files.
- Live genome search and sequence retrieval need network access to NCBI.
- Login, registration, saved search history, and feedback require configured
  database/AWS services. A public clone does not provision those resources.
  Your lab administrator should supply the appropriate private configuration if
  those features are required. The saved demo does not depend on them.

## Common startup problems

| Message or symptom | What to do |
| --- | --- |
| `Missing prerequisites: node, npm` | On macOS, complete the Node/PATH steps above and reopen Terminal. On Windows, finish the installers and reopen PowerShell. Verify `node --version` and `npm --version` or `npm.cmd --version`. |
| Missing .NET 8 SDK | Install the SDK from step 2, then check that `dotnet --list-sdks` includes an 8.x entry. |
| `destination path 'phyloDIVaS' already exists` | Use the existing-installation update commands; do not repeat the clone. |
| `Ports already in use` | Stop the old launcher with Ctrl+C. The launcher does not terminate other programs. If you cannot find the old process, ask your administrator rather than deleting files or killing unrelated applications. |
| An API exits during startup | Read the specific log named in the error under `.local-run/`. |
| An API build or `npm ci` fails | Setup logs are in `.local-run/setup/`; correct the reported dependency, network, or build error before retrying. |
| Browser does not open | Once Terminal says the app is ready, open http://localhost:3000 manually. |
| Login or saved history fails while the demo works | Those features need additional database/account-service configuration; installing Node or re-cloning will not create it. |

The guide matches the current launcher. Package downloads depend on the package
providers and supported operating systems; these instructions are not a claim
that a brand-new installation has been tested on every platform.

## Official prerequisite references

- [Homebrew installation](https://brew.sh/)
- [Homebrew Node.js 22](https://formulae.brew.sh/formula/node@22)
- [Homebrew Python 3.12](https://formulae.brew.sh/formula/python@3.12)
- [Homebrew .NET 8 SDK](https://formulae.brew.sh/cask/dotnet-sdk@8)
- [Microsoft .NET installation on Windows](https://learn.microsoft.com/dotnet/core/install/windows)
- [Microsoft Windows Package Manager](https://learn.microsoft.com/windows/package-manager/winget/)
