# Dashboard command response contract

The existing POST routes are unchanged: Genshin character/charlist/abyss/theater/stygian;
HSR character/charlist/moc/pf/as/aa; ZZZ character/charlist/shiyu/da/tower; HI3 battlesuit.
No notes, redemption or check-in route is added.

JSON properties use camelCase. All enum values are **strings using the
shared enum names (PascalCase)**, never numeric enum values. Optional properties are
present as `null` when absent with the current MVC JSON defaults. UTC cycle instants
are ISO 8601 strings with zero offset; absent timestamps remain null, not epoch/zero.

## HTTP 200

All three DTOs keep `storageFileName`: the actual attachment-storage key when a card
exists, null otherwise. Continue downloading through the existing authenticated
`GET /attachments/{storageFileName}` route. No display filename or static-image key
is substituted. Clients should inspect `state` before trying to download.

Roster (`DashboardRosterDto`):

```json
{"storageFileName":"<key>","game":"Genshin","gameUid":"123456789","state":"Complete","code":null}
```

Roster state is `Complete` or `Empty`.

Endgame (`DashboardEndgameDto`):

```json
{"storageFileName":"<key>","game":"Genshin","gameUid":"123456789","state":"Complete","code":null,"mode":"SpiralAbyss","floor":12,"cycleStart":"2026-06-30T20:00:00+00:00","cycleEnd":"2026-07-30T20:00:00+00:00"}
```

Endgame state is `Complete`, `Empty` (no record), or `NotUnlocked`. Empty/locked
responses have null storage keys. Floor and both cycle instants are independently
nullable; Tower (`SimulatedBattleTrial`) has no cycle. Modes are `SpiralAbyss`,
`ImaginariumTheater`, `StygianOnslaught`, `MemoryOfChaos`, `PureFiction`,
`ApocalypticShadow`, `AnomalyArbitration`, `ShiyuDefense`, `DeadlyAssault`,
`SimulatedBattleTrial`. Game values for these routes are `Genshin`, `HonkaiStarRail`,
`ZenlessZoneZero` and (character only) `HonkaiImpact3`.

Character batch (`DashboardCharacterCardsDto`):

```json
{"storageFileName":"<first-generated-key>","game":"Genshin","gameUid":"123456789","state":"Partial","code":null,"requestLimit":null,"items":[{"requestedName":"missing","characterName":null,"characterId":null,"status":"NotFound","storageFileName":null,"error":{"error":"Character not found.","code":"CharacterNotFound"}},{"requestedName":"alias","characterName":"Name","characterId":1,"status":"Generated","storageFileName":"<first-generated-key>","error":null}]}
```

The top-level key is the **first generated item in original outcome order**, not
necessarily the first requested item. Every item is retained in order. States are
`Complete`, `Empty`, `Partial`, `RequestLimitExceeded`, `Failed`; item statuses are
`Generated`, `NotFound`, `Failed`, `NotAttempted`. Failed items have an allowlisted
error; not-found items may have an allowlisted `CharacterNotFound` error. Generated
and not-attempted items have no error. A successful envelope with no generated
images remains HTTP 200, including typed all-item-failed/partial results.

Request-limit responses have `state` and `code` equal to `RequestLimitExceeded`,
positive `requestLimit`, null `gameUid`/`storageFileName`, and ordered `NotAttempted`
items. Otherwise the top-level `code` is null. Character-not-found uses item
`status`/`error.code`; no-record and not-unlocked use top-level `state`.

## HTTP errors

A failed command envelope remains HTTP 500, regardless of a retained partial
payload. The body is `{"error":"<shared safe CommandError.Message>","code":"<CommandErrorCode>"}`;
no retained images/items or arbitrary exception/upstream text is exposed. Missing,
malformed, unsupported or contradictory outcomes use `InvalidResponse` and its
shared safe message. Static image-storage references are rejected because the
existing download route serves only attachment storage.

Command error codes are `Unknown`, `AuthenticationRequired`, `ApiUnavailable`,
`ImageUpdateFailed`, `AttachmentStoreFailed`, `Cancelled`, `Timeout`,
`InvalidRequest`, `InvalidResponse`, `CharacterNotFound`, `RequestLimitExceeded`.
This does not remap Application auth failures to HTTP 401/403.

Existing local authentication/validation responses remain unchanged: validation
400 (`error`, `validationErrors`), unlock required 403 (`error`, `code: "AUTH_REQUIRED"`),
invalid passphrase 401 (`error`), missing profile 404 (`error`), rate limit 429
(`error`). Existing controller model/server validation, missing Discord claim,
attachment validation/download statuses, session claim propagation and
last-used-server update-on-command-success rules are untouched.
