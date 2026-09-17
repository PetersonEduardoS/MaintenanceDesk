param([string]$BaseUrl = 'http://localhost:5187')
$ErrorActionPreference = 'Stop'
function Send-Json($Method, $Path, $Body) {
    Invoke-RestMethod -Method $Method -Uri "$BaseUrl$Path" -ContentType 'application/json' -Body ($Body | ConvertTo-Json)
}
$ticket = Send-Json POST /tickets @{ title='Motor overheating'; equipment='Demo Conveyor 02'; priority='High' }
Write-Host "Created $($ticket.id), status $($ticket.status), version $($ticket.version)"
$ticket = Send-Json PATCH "/tickets/$($ticket.id)/status" @{ status='InProgress'; version=$ticket.version }
Write-Host "Started work: $($ticket.status), version $($ticket.version)"
try {
    Send-Json PATCH "/tickets/$($ticket.id)/status" @{ status='Resolved'; version=1; resolution='Outdated update' }
    throw 'Expected a concurrency conflict.'
} catch {
    if ([int]$_.Exception.Response.StatusCode -ne 409) { throw }
    Write-Host 'Stale update rejected with HTTP 409.'
}
$ticket = Send-Json PATCH "/tickets/$($ticket.id)/status" @{ status='Resolved'; version=$ticket.version; resolution='Replaced bearing and verified temperature.' }
Write-Host "Resolved: $($ticket.status), version $($ticket.version)"
$ticket | ConvertTo-Json
Invoke-RestMethod "$BaseUrl/tickets?priority=High&page=1&pageSize=10" | ConvertTo-Json -Depth 4
