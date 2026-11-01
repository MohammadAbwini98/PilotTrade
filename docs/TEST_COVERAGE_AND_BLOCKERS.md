# Test Coverage and Blockers

Last local run: 2026-11-01, Windows x64, commit `455a2fd`.

| Suite | Passed | Skipped | Failed |
|---|---|---|---|
| Unit | 100 | 0 | 0 |
| Integration, contract and API | 224 | 3 (macOS keychain, real store) | 0 |

The `Credentials` and `OwnerAuthorization` categories also run as their own CI step on each OS. Tests use in-memory stores, a temporary database and a fake broker; on Windows only the throwaway `PilotTrade/Test/RoundTrip` target touches the real store.

## Not verified

- Real Capital.com behaviour: the one recorded run failed its quote step, and that fix has only run against mocks. The mocks follow the documentation, and a mock mistake is what hid this bug, so passing tests here do not prove the real API.
- Owner flow of the published-app smoke test: skipped locally to keep the real owner token untouched; CI runs it on fresh machines.
- UI against the real API: the built UI was driven against a local stub only, and has no automated test suite.

## Blockers

See `WINDOWS_RELEASE_BASELINE.md` and `DEMO_RELEASE_GO_NO_GO.md`.
