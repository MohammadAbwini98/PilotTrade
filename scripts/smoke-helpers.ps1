# Helpers for smoke-test.ps1, kept apart so smoke-helpers.tests.ps1 can check them without a running app.
# They behave the same under PowerShell 7 and Windows PowerShell 5.1.

# ConvertFrom-Json can hand a JSON array over as one object, depending on the version. This emits each item,
# so wrap the call in @() to get a flat array.
function ConvertTo-FlatList {
    param($InputObject)
    if ($null -eq $InputObject) { return }
    $isList = $InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string] -and $InputObject -isnot [System.Collections.IDictionary]
    if (-not $isList) { return $InputObject }
    foreach ($item in $InputObject) { ConvertTo-FlatList -InputObject $item }
}

function Assert-ScalarText($name, $value) {
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) { throw "FAIL: $name must be one non-empty string" }
}

# The select body must be an object of two strings. The API answers anything else with a 400.
function New-SelectionBody($AccountId, $Epic) {
    Assert-ScalarText 'accountId' $AccountId
    Assert-ScalarText 'epic' $Epic
    return (@{ accountId = $AccountId; epic = $Epic } | ConvertTo-Json -Compress)
}

# Only the status, route and a plain error code may be reported. Headers and free text could carry secrets.
function Format-HttpFailure($Method, $Route, $StatusCode, $Body) {
    $code = 'none'
    try {
        $error_ = ($Body | ConvertFrom-Json).error
        if ($error_ -is [string] -and $error_ -cmatch '^[A-Za-z_]{1,40}$') { $code = $error_ }
    }
    catch { }
    return "$Method $($Route -replace '\?.*$', '') answered $StatusCode, error code $code"
}
