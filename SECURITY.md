# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem. Instead, open a **private security advisory** on this repository's GitHub page (Security tab, then "Report a vulnerability"). Include what you found, how to reproduce it, and which version you tested. We will acknowledge the report, work on a fix, and credit you if you wish once it is released.

## How Desktop Deck handles secrets

Desktop Deck runs entirely on your PC, has no account system and no telemetry, and only contacts services you have configured (see "Network use" in the [README](README.md)). The sections below describe what the code actually does.

### What counts as a secret

- Your Google OAuth client ID and client secret, the Google refresh token, the granted scopes and the account's email address
- iCal calendar addresses (a private `.ics` link works like a password)
- Tokens and passwords entered into widgets: the GitHub token, the Notion integration secret and the OBS WebSocket password

### Where they are stored

All of the above live in `%LOCALAPPDATA%\DesktopDeck\secrets.json`, handled by `SecretStore` (`src/DesktopDeck/Services/SecretStore.cs`):

- Each value is encrypted with the Windows Data Protection API (DPAPI, `CryptProtectData`) for the current Windows user, with an application-specific entropy value and the "UI forbidden" flag so it never prompts. Only the same Windows account on the same PC can decrypt it; if the file is copied elsewhere, the values simply cannot be read.
- The file is a JSON map from entry name to the base64 of the encrypted bytes. It is written atomically (to a temporary file, then moved into place).
- The plaintext buffers used for the Windows call are cleared before they are released.
- Entry names are `google.*` for the Google sign-in, one entry per iCal address, and `widget:<name>` for widget tokens.
- Secrets are **not** stored in `settings.json`. `settings.json` holds only labels (for example an iCal link's label and color) and non-secret preferences.
- Daily snapshots (the History tab) copy `settings.json` only, never `secrets.json`.

### Google sign-in

- Desktop Deck never ships a Google OAuth client. You create your own (see the README) so there is no shared client secret to leak.
- Sign-in uses OAuth 2.0 for installed apps: authorization code with PKCE (S256) and a random `state` that is checked, with a loopback redirect to `http://127.0.0.1:<free port>/` that listens only for the duration of the sign-in (it times out after five minutes). Your browser, not Desktop Deck, sees your Google password.
- Access tokens are kept in memory only. The refresh token is stored encrypted as described above.
- Each feature (Calendar, Tasks, Gmail, Classroom) asks Google for only its own scopes when you enable it. Gmail access is read-only and uses message metadata (From, Subject, Date and Gmail's snippet); the code never downloads bodies or attachments and cannot send, change or delete mail.
- **Disconnect** revokes the grant at Google and deletes the saved client ID, client secret, refresh token, scopes and email from `secrets.json`.
- Error messages shown by the Google features are written so that they do not contain tokens or message contents.

## Limits you should know about

- DPAPI protects the file at rest and from other Windows users. It does **not** protect against malware or another process running as your Windows user, which can ask Windows to decrypt the values just as Desktop Deck does. Do not run untrusted software under the same account.
- Some data is stored unencrypted in your data folder because it is not a credential: your layouts and widget data in `settings.json` (including cached Gmail message metadata, if you use the Gmail widget, and the connected account's email address). Protect the folder with normal Windows account and disk encryption measures.
- `errors.log` records exception details to help diagnose problems. Look it over before sharing it publicly.
- Code you add to your own build runs with the same permissions as Desktop Deck. The widget system is an internal extension contract, not a sandbox, and Desktop Deck does not load external plug-in files.

## Supported versions

Security fixes are made to the latest release.
