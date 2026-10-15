# BehavioralAuthSystem

A .NET 8 Blazor Server secure file-storage prototype combining password authentication, keystroke dynamics, action-level behavioral verification, AES-256-GCM encryption, versioning, sharing, trash management and security analytics.

## Behavioral action protection

The current action-level system uses four protected actions:

- Create
- Upload
- Update
- Delete

Each action can have one of four behavioral patterns:

- Mouse Clicks - 3 to 5 click points
- Shape - mouse path / center-of-mass verification
- Shortcut - modifier + key combination
- Screen Tap - single coordinate target

Patterns are stored as JSON in `BehavioralPatterns.PatternData`. The verification modal requests the pattern before a protected operation proceeds.

## Security model

- Passwords: PBKDF2-derived hashes with salts.
- Files: AES-256-GCM authenticated encryption.
- Key derivation: HKDF-SHA256 using a master secret and per-user encryption salt.
- Behavioral login: keystroke hold/latency statistics.
- Action verification: interaction-pattern comparison and audit logging.

## Storage disclosure

The application stores encrypted payloads on the server under `App_Data/uploads`. It is not a deployed Azure/AWS/GCP storage system and it is not client-side zero-knowledge encryption.

## Run

```text
cd BehavioralAuthSystem
dotnet run
```

The application applies pending EF Core migrations on startup.
