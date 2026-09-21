# Windows production code signing

MoveBit's release workflow treats Windows code signing as a **release requirement**, not an optional decoration.

The Windows release job signs both:

- `MoveBit.exe` inside the Windows portable ZIP; and
- `MoveBit-Setup-windows-x64.exe` after the Inno Setup package is built.

The workflow then runs `Get-AuthenticodeSignature` on both files and refuses to publish if either signature is missing or invalid. SHA-256 sidecars are generated **after** signing so the published checksums always describe the exact signed files users download.

## Why Azure Artifact Signing

MoveBit uses [Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/overview) (formerly Trusted Signing) with a **Public Trust** certificate profile.

This avoids checking a long-lived private signing key into GitHub. The GitHub Actions workflow authenticates to Azure with OpenID Connect (OIDC), and the signing key remains in Microsoft's managed HSM-backed service.

A self-signed certificate is deliberately not supported for public releases because Windows does not trust it by default and it does not solve the SmartScreen problem.

## SmartScreen expectation

Authenticode signing materially improves the Windows trust experience, but a brand-new publisher identity can still receive an "unrecognized app" SmartScreen warning while reputation is being established. Keep signing every public release with the same publisher identity so reputation can accumulate across versions.

The only distribution path Microsoft documents as avoiding SmartScreen download warnings from the first install is Microsoft Store distribution. Direct-download EXE releases should still be signed consistently.

## One-time Azure setup

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

## Required GitHub repository variables

Configure these under **Settings → Secrets and variables → Actions → Variables**:

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

The `windows-signed` release job performs this sequence:

1. Validate that every required signing variable exists.
2. Publish the Windows x64 self-contained single-file payload.
3. Authenticate to Azure through GitHub OIDC.
4. Sign `publish/MoveBit.exe` with SHA-256 + RFC 3161 timestamping.
5. Verify the application's Authenticode signature is `Valid`.
6. Package the signed executable into `MoveBit-windows-x64.zip` and generate its SHA-256 sidecar.
7. Build `MoveBit-Setup-windows-x64.exe` from the already-signed payload.
8. Sign the final installer.
9. Verify the installer's Authenticode signature is `Valid`.
10. Generate the installer's SHA-256 sidecar.
11. Upload the signed Windows portable and installer artifacts.
12. Allow the GitHub Release job to run only after signed Windows artifacts and the macOS/Linux builds all succeed.

If the Azure signing configuration is missing, expired, unauthorized, or produces an invalid signature, the Windows job fails and **no GitHub Release is created**.

## Verifying a downloaded build locally

On Windows PowerShell:

```powershell
Get-AuthenticodeSignature .\MoveBit-Setup-windows-x64.exe |
  Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

For a correctly signed production build, `Status` should be `Valid` and `SignerCertificate` should identify the verified MoveBit publisher identity from the Artifact Signing profile.

You can also inspect the file through **Properties → Digital Signatures**.

## Existing releases

`v1.0.2` and earlier were published before mandatory production signing was introduced, so those historical assets remain unsigned. Do not replace old release assets in place: preserving immutable historical artifacts is preferable. Publish the next version through the signed pipeline once the Azure Artifact Signing identity/profile and GitHub repository variables are configured.
