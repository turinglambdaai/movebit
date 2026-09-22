# Windows production code signing

MoveBit supports production Windows code signing with Azure Artifact Signing, but signing is currently **optional** so public releases do not require a paid signing service during the project's early stage.

When the complete Azure signing configuration is present, the Windows release job signs both:

- `MoveBit.exe` inside the Windows portable ZIP; and
- `MoveBit-Setup-windows-x64.exe` after the Inno Setup package is built.

The workflow then runs `Get-AuthenticodeSignature` on both files. SHA-256 sidecars are always generated **after** the optional signing step so the published checksums describe the exact files users download.

When none of the Azure signing variables are configured, the same workflow publishes unsigned Windows binaries with SHA-256 sidecars. A partially configured signing setup is treated as an error: configure all six variables or none of them.

## Why Azure Artifact Signing

MoveBit is prepared to use [Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/overview) (formerly Trusted Signing) with a **Public Trust** certificate profile.

This avoids checking a long-lived private signing key into GitHub. GitHub Actions authenticates to Azure with OpenID Connect (OIDC), and the signing key remains in Microsoft's managed HSM-backed service.

A self-signed certificate is deliberately not used for public releases because Windows does not trust it by default and it does not solve the SmartScreen publisher-trust problem.

## SmartScreen expectation

Authenticode signing materially improves the Windows trust experience, but a brand-new publisher identity can still receive an "unrecognized app" SmartScreen warning while reputation is being established. Keep signing every public release with the same publisher identity once production signing is enabled so reputation can accumulate across versions.

Until signing is enabled, Windows may identify the direct-download installer as coming from an unknown publisher. The release SHA-256 sidecar still provides an integrity check, but it is not a substitute for publisher authentication.

## One-time Azure setup

When production signing becomes worthwhile:

1. In Azure, register/use the **Microsoft.CodeSigning / Artifact Signing** resource provider.
2. Create an Artifact Signing account.
3. Complete **Public** identity validation.
4. Create a **Public Trust** certificate profile. Do not use `Public Trust Test` for production; test profiles are not publicly trusted.
5. Create or reuse a Microsoft Entra application/service principal for GitHub Actions.
6. Add a federated credential for this repository so GitHub Actions can authenticate with OIDC. Scope it to `turinglambdaai/movebit` and the release context you intend to use.
7. Assign that service principal the **Artifact Signing Certificate Profile Signer** role on the production certificate profile (or the narrowest supported scope containing it).

Microsoft's setup references:

- https://learn.microsoft.com/azure/artifact-signing/quickstart
- https://learn.microsoft.com/azure/artifact-signing/tutorial-assign-roles
- https://learn.microsoft.com/azure/artifact-signing/how-to-signing-integrations
- https://github.com/Azure/artifact-signing-action

## Optional GitHub repository variables

Configure these together under **Settings → Secrets and variables → Actions → Variables** when signing is enabled:

| Variable | Meaning |
| --- | --- |
| `AZURE_CLIENT_ID` | Microsoft Entra application/client ID used by GitHub OIDC |
| `AZURE_TENANT_ID` | Microsoft Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription containing the Artifact Signing resource |
| `AZURE_ARTIFACT_SIGNING_ENDPOINT` | Artifact Signing account endpoint, for example the regional `https://...codesigning.azure.net/` endpoint shown by Azure |
| `AZURE_ARTIFACT_SIGNING_ACCOUNT` | Artifact Signing account name |
| `AZURE_ARTIFACT_SIGNING_PROFILE` | Production **Public Trust** certificate profile name |

These identifiers are not private signing keys. The workflow intentionally uses OIDC and does not require an `AZURE_CLIENT_SECRET`.

## Release behavior

The Windows release job performs this sequence:

1. Inspect the six signing variables.
2. If all are absent, select unsigned mode. If some but not all are present, fail the job. If all are present, select signed mode.
3. Publish the Windows x64 self-contained single-file payload.
4. In signed mode, authenticate to Azure, sign `MoveBit.exe`, and require its Authenticode status to be `Valid`.
5. Package the Windows payload into `MoveBit-windows-x64.zip` and generate its SHA-256 sidecar.
6. Build `MoveBit-Setup-windows-x64.exe` from that same payload.
7. In signed mode, sign the final installer and require its Authenticode status to be `Valid`.
8. Generate the installer's SHA-256 sidecar.
9. Upload the Windows portable and installer artifacts.
10. Allow the GitHub Release job to run after Windows, macOS, and Linux artifacts all succeed.

This keeps signing ready to turn on later without maintaining a separate release pipeline.

## Verifying a downloaded build locally

On Windows PowerShell:

```powershell
Get-AuthenticodeSignature .\MoveBit-Setup-windows-x64.exe |
  Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

For an unsigned release, `Status` will indicate that no valid Authenticode signature is present. Verify the corresponding `.sha256` sidecar against the downloaded file instead.

For a signed production build, `Status` should be `Valid` and `SignerCertificate` should identify the verified MoveBit publisher identity from the Artifact Signing profile.

You can also inspect signed files through **Properties → Digital Signatures**.

## Existing releases

`v1.0.2` and earlier are unsigned. Starting with the optional-signing pipeline, future releases remain publishable without Azure while retaining the ability to switch to trusted signing simply by configuring all six repository variables.
