# Gate 5 owner comparison checklist

Gate 5 cannot pass on automation. The probe proves that the fields arrive, and never records their values, so only the account owner can confirm that PilotTrade shows the same figures as Capital.com. Until every row below reads yes, gate 5 stays open.

Write only yes or no in this file. Do not write balances, account numbers, deal IDs or screenshots here: this file is tracked by git.

## How to compare

1. Start PilotTrade in `capital_demo` mode with `TRADING_ACCOUNT_ID` set to your Demo account, as described in `docs/DEMO_AUTHENTICATION_AND_LOCAL_SETUP.md`.
2. Sign in to the Capital.com web platform and open the same Demo account.
3. Refresh both within a minute of each other. Profit and loss moves while a position is open, so compare balance first and allow for market movement on equity.
4. Fill in the table.

| # | Item | Where in PilotTrade | Where on Capital.com | Same? (yes or no) |
| --- | --- | --- | --- | --- |
| 1 | Account identity: the ID and name are the Demo account you configured | Account selector | Account switcher | |
| 2 | Account is Demo, not Live | Environment shown as Demo | Demo toggle | |
| 3 | Account currency | Account panel | Account details | |
| 4 | Balance | Account panel, balance | Account details, balance | |
| 5 | Equity, or shown as unknown when it cannot be proven | Account panel, equity | Account details, equity | |
| 6 | Available funds | Account panel, free margin | Account details, available | |
| 7 | Margin used shows as unknown, not zero, because the API does not provide it | Account panel, margin used | Not compared | |
| 8 | Realized profit and loss shows as unknown, not zero, because the API does not provide it | Account panel, realized | Not compared | |
| 9 | Open positions: same count, instrument, direction and size | Positions table | Positions tab | |
| 10 | Working orders: same count, instrument, direction, size and level | Working orders table | Orders tab | |

If there are no positions or working orders, rows 9 and 10 only prove that both sides are empty. Mapping of non-empty holdings is then still covered by mock tests alone, and the acceptance report must say so. Opening a position to test this belongs to the separately authorized Demo execution phase.

## Sign-off

| Field | Value |
| --- | --- |
| Commit compared | |
| Date (UTC) | |
| Any row answered no | |
| Holdings were non-empty (yes or no) | |
| Owner confirms gate 5 (yes or no) | |
