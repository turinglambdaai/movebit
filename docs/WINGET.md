# Publishing to winget

The [Windows Package Manager community repo](https://github.com/microsoft/winget-pkgs)
takes one manifest PR per release version. The
[wingetcreate](https://github.com/microsoft/winget-create) CLI generates and
submits those manifests from an installer URL — no hand-written YAML.

## One-time setup

1. `winget install Microsoft.WingetCreate` (or grab it from its releases page).
2. A GitHub account that can open pull requests against `microsoft/winget-pkgs`.

## Per release

```powershell
wingetcreate new https://github.com/turinglambdaai/movebit/releases/download/v<version>/MoveBit-Setup-windows-x64.exe
# Fill in PackageIdentifier (TuringLambda.MoveBit unless a reviewer suggests
# another publisher prefix) and confirm the metadata it scraped.
wingetcreate submit --github-token <token> .
```

Notes:

- The Inno Setup installer already supports the silent switches winget expects
  (`/VERYSILENT /NORESTART /SUPPRESSMSGBOXES`) and installs per-user, which
  winget's manifest generation detects automatically.
- Submit the installer only — not the portable ZIP. MoveBit updates itself in
  place; advertising the portable build to winget would create a second update
  path that can race the in-app updater.
- winget requires the SHA256 of the exact installer asset, which wingetcreate
  computes from the URL at submission time; re-run the flow for every release
  tag.
