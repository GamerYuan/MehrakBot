# Profile Credential Authentication

Profiles are authenticated with the two HoYoLAB browser-cookie values supplied
**separately**:

- `ltuid_v2` — the numeric HoYoLAB account UID.
- `ltoken_v2` — the session token.

Full browser cookie strings (`ltoken_v2=...; ltuid_v2=...; ...`) are not
accepted. Copy only the values of those two cookies from the browser's cookie
storage and enter them in their own fields. Surrounding whitespace is trimmed
from credential values before validation; passphrases are left unchanged.

## Validation rules

`Mehrak.GameApi.Shared.LTokenValidator` enforces the token format everywhere a
Cookie header is built:

- Non-empty and at most 4096 characters.
- Only RFC 6265 cookie-octet characters: printable ASCII except space, `"`,
  `,`, `;`, and `\`.

The Dashboard and the Bot additionally require the UID to be a positive
decimal value no greater than `long.MaxValue` (9223372036854775807), because
profiles persist it as a `long`. Zero and overflowing values are rejected
before any cast, upstream request, encryption, or persistence.

New/changed passphrases are 12–64 characters. Failures are reported with
generic messages and never echo the token, UID, or passphrase.

## Dashboard (`/profiles`)

`AddProfileRequest` requires `ltUid`, `lToken`, and `passphrase`:

```json
{ "ltUid": 123456789,
  "lToken": "v2_...",
  "passphrase": "twelvechars-or-more" }
```

`UpdateProfileRequest` requires `lToken` and `passphrase`; the UID is taken
from the stored profile and cannot be changed through this endpoint:

```json
{ "lToken": "v2_...",
  "passphrase": "twelvechars-or-more" }
```

Behavior notes:

- `ltUid` zero or above `long.MaxValue` returns `400` before the credential is
  sent to HoYoLAB or encrypted.
- `lToken` must satisfy the attributes on the request model (`[Required]`,
  length, cookie-octet pattern). A malformed value returns a `400` validation
  problem, and the API service also refuses it before building the Cookie
  header.
- Valid requests are still validated against HoYoLAB (`bypassCache: true`)
  before persistence. Only the entered token is encrypted.
- Update rotates the stored token and revokes both the Bot and Dashboard
  credential caches; max-profile/duplicate handling is unchanged.

## Discord bot (`/profile add`, `/profile update`)

- The add modal asks for the HoYoLAB UID (`ltuid_v2`) and the
  `ltoken_v2` cookie value as separate inputs, plus the passphrase.
- The update modal shows the stored UID read-only and asks for the new
  `ltoken_v2` value plus the passphrase. The UID cannot be changed here.
- Invalid UID and malformed token inputs are rejected before the GameApi call
  and before encryption, with generic messages that never echo the values.

## Security invariants

- Tokens and passphrases are never logged, returned in errors, or embedded in
  exception messages.
- The UID and token enter independent fields; callers cannot smuggle a full
  cookie string through either input.
- Existing weak passphrases keep working for the decrypt-only auth modal;
  new/changed passphrases must meet the 12–64 character rule.
