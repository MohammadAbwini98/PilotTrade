# Security Threat Model

| Threat | Control |
|---|---|
| Secrets at rest | Windows Credential Manager or macOS keychain; never in the repo, logs or API responses |
| Secret in the process list on macOS | The keychain value goes through stdin to `security -i`, with control characters refused |
| Environment confusion | Separate Demo and Live targets; explicit modes; Live stops startup |
| Another local page or site calling the API | Owner token on all `/api` routes, plus parsed Origin and single-use CSRF on browser writes |
| DNS rebinding or a remote caller | Loopback caller and loopback Host required |
| Leaked broker details | Fixed error codes only; raw broker bodies never returned; request logging removed from the broker client |
| Evidence leaking data | The account ID is masked and tokens are excluded |
| Prompt injection | Market data is untrusted; models have no tools and cannot execute |
| Stale decisions | Selection versions, and risk re-evaluated before submission |
| Duplicate orders | Durable journal; no automatic resubmission after an uncertain outcome; reconciliation; durable kill switch |
| Secrets in a release | `scripts/secret-scan.ps1` checks tracked files and publish output in CI. It catches known shapes only |

Known limits: any process that can run as the same OS user can read the owner token through `--show-owner-token`, and older commits contain build logs (no secrets found in them by this scan, which covers current files only).
