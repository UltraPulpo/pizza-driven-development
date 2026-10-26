# Simulates the restaurant: advances an order one stage (NotStarted -> Started -> ReadyForPickup -> Delivered).
param(
    [Parameter(Mandatory)][string]$OrderId,
    [string]$ApiUrl = 'http://localhost:5105'
)
$ErrorActionPreference = 'Stop'
$order = "$ApiUrl/api/orders/$OrderId"
try {
    $current = (Invoke-RestMethod $order).status
    $body = @{ expectedStatus = $current } | ConvertTo-Json -Compress
    $next = (Invoke-RestMethod "$order/advance" -Method Post -ContentType 'application/json' -Body $body).status
    "$current -> $next"
}
catch {
    $detail = if ($_.ErrorDetails.Message) { ($_.ErrorDetails.Message | ConvertFrom-Json).detail } else { $_.Exception.Message }
    [Console]::Error.WriteLine($detail)
    exit 1
}
