# Profile Cookie-String Authentication

Profiles can be added/rotated with a full pasted browser cookie string instead
of entering the HoYoLAB UID and `ltoken_v2` separately. The string only needs
to contain the `ltoken_v2` and `ltuid_v2` pairs; any other cookies in the paste
are ignored.

## Cookie format

`Mehrak.GameApi.Shared.CookieCredentialParser` enforces these rules:

- Cookie names match exactly and case-sensitively (`ltoken_v2`, `ltuid_v2`).
- Each pair splits at the first `=` (tokens containing `=` keep working).
- ASCII spaces around pairs, names, and values are trimmed.
- Unrelated pairs are ignored; empty segments are skipped.
- Either key missing, empty, or duplicated (even with identical values) fails.
- The token must pass the existing `LTokenValidator` charset/length checks.
- The UID must be positive decimal digits fitting `long.MaxValue`.
- Any control character (CR/LF/TAB/...) fails instead of being stripped.
- Total length is bounded at 16 KiB.
- A `Cookie:` header prefix and quoted token values are not supported.

Failures are boolean-only so every caller reports the same generic
"Invalid HoYoLAB UID or Cookies" message without echoing the input.

## Dashboard (`/profiles`)

`AddProfileRequest` and `UpdateProfileRequest` accept an additive optional
`cookieString` JSON field. The legacy inputs keep working unchanged.

| Supplied input | Result |
| --- | --- |
| `ltUid` + `lToken` (add) / `lToken` (update) | Legacy path, as before |
| `cookieString` only | Cookie path: parsed first, then validated upstream |
| Both `cookieString` and legacy credentials | `400` model-validation failure |
| Neither | `400` model-validation failure |

```json
{ "cookieString": "ltoken_v2=...; ltuid_v2=...; other=...",
  "passphrase": "twelvechars-or-more" }
```

Behavior notes:

- The passphrase is still required: only the extracted token is encrypted.
- Only the extracted `ltoken`/`UID` are sent to HoYoLAB (`bypassCache: true`
  validation is preserved).
- On update, a cookie UID that differs from the stored profile UID is rejected
  before any upstream call or persistence.
- Max-profile/duplicate handling and cache revocations are unchanged.

## Discord bot (`/profile add`, `/profile update`)

- The add modal asks for the full cookie string plus the passphrase; there is
  no separate UID input.
- The update modal displays the stored profile UID and asks for the new cookie
  string plus the passphrase; a cookie UID that differs from the displayed UID
  is rejected with the same generic message.
- Discord modal inputs cap at 4000 characters. Pastes near or above that size
  should go through the Dashboard endpoint (16 KiB bound) instead.

## Security invariants

- The raw cookie string is never logged, returned in errors, encrypted, or
  sent upstream; only the extracted values flow downstream.
- Error messages stay generic so failures do not oracle which half was wrong.
- Existing legacy `LToken` validation and weak-passphrase decrypt-only unlocks
  are unchanged.
