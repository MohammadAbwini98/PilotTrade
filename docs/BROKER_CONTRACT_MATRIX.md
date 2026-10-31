# Broker Contract Matrix

| Endpoint | Method | Status | Notes |
|---|---|---|---|
| /api/v1/session | POST | PARTIAL | Headers CST, X-SECURITY-TOKEN parsed in tests |
| /api/v1/markets | GET | PARTIAL | search term; markets array |
| /api/v1/markets/{epic} | GET | PARTIAL | name |
| /api/v1/positions | GET | NOT IMPLEMENTED parse | returns empty |
| /api/v1/orders | POST | BLOCKED | not verified |
| /api/v1/confirms/{dealReference} | GET | BLOCKED | not verified |