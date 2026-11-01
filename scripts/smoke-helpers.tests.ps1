# Checks the smoke-test helpers. Runs under PowerShell 7 and Windows PowerShell 5.1, with no module needed.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'smoke-helpers.ps1')

$script:failures = 0
function Check($name, [bool]$passed) {
    if ($passed) { Write-Output "PASS: $name" }
    else { $script:failures++; Write-Output "FAIL: $name" }
}
function Throws($scriptBlock) {
    try { & $scriptBlock | Out-Null; return $false } catch { return $true }
}

Write-Output "PowerShell $($PSVersionTable.PSVersion)"

Check 'Empty array flattens to nothing' (@(ConvertTo-FlatList -InputObject @()).Count -eq 0)
Check 'Null flattens to nothing' (@(ConvertTo-FlatList -InputObject $null).Count -eq 0)
$single = @(ConvertTo-FlatList -InputObject ('[{"epic":"A"}]' | ConvertFrom-Json))
Check 'Single result stays one item' ($single.Count -eq 1 -and $single[0].epic -eq 'A')
$many = @(ConvertTo-FlatList -InputObject ('[{"epic":"A"},{"epic":"B"},{"epic":"C"}]' | ConvertFrom-Json))
Check 'Several results stay separate' ($many.Count -eq 3 -and $many[0].epic -is [string] -and $many[2].epic -eq 'C')
$nested = @(ConvertTo-FlatList -InputObject @(, @(@{ epic = 'A' }, @{ epic = 'B' })))
Check 'An array inside an array is unrolled' ($nested.Count -eq 2)
$lone = @(ConvertTo-FlatList -InputObject ('{"epic":"A"}' | ConvertFrom-Json))
Check 'A lone object is one item' ($lone.Count -eq 1)
Check 'A string is not split into characters' (@(ConvertTo-FlatList -InputObject 'abc').Count -eq 1)

$body = New-SelectionBody 'ACC' 'EURUSD' | ConvertFrom-Json
Check 'Body holds scalar strings' ($body.accountId -is [string] -and $body.epic -is [string] -and $body.epic -eq 'EURUSD')
Check 'Epic array is refused' (Throws { New-SelectionBody 'ACC' @('A', 'B') })
Check 'Account array is refused' (Throws { New-SelectionBody @('ACC') 'A' })
Check 'Empty epic is refused' (Throws { New-SelectionBody 'ACC' '' })
Check 'Blank account is refused' (Throws { New-SelectionBody '  ' 'A' })
Check 'Null epic is refused' (Throws { New-SelectionBody 'ACC' $null })
Check 'Non-string epic is refused' (Throws { New-SelectionBody 'ACC' 5 })
Check 'Object epic is refused' (Throws { New-SelectionBody 'ACC' ('{"epic":"A"}' | ConvertFrom-Json) })

$named = Format-HttpFailure 'POST' '/api/instruments/select' 403 '{"error":"AccountNotConfigured"}'
Check 'Failure names the route, status and code' ($named -eq 'POST /api/instruments/select answered 403, error code AccountNotConfigured')
$canary = 'CANARY-SECRET-VALUE'
Check 'Free text in an error is dropped' ((Format-HttpFailure 'POST' '/x' 400 "{`"error`":`"token $canary`"}") -notmatch $canary)
Check 'A body that is not JSON is dropped' ((Format-HttpFailure 'POST' '/x' 400 $canary) -notmatch $canary)
Check 'An empty body is handled' ((Format-HttpFailure 'POST' '/x' 400 $null) -match 'error code none')
Check 'A query string is dropped' ((Format-HttpFailure 'GET' "/x?accountId=$canary" 400 '') -notmatch $canary)

if ($script:failures -gt 0) { throw "$script:failures helper check(s) failed" }
