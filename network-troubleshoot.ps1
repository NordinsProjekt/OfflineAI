# Network Troubleshooting Script for OfflineAI API
Write-Host "=== OfflineAI API Network Troubleshooting ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check IP Configuration
Write-Host "1. Current IP Configuration:" -ForegroundColor Yellow
$ipConfig = Get-NetIPAddress -AddressFamily IPv4 | Where-Object {$_.IPAddress -like "192.168.*"}
$ipConfig | Format-Table IPAddress, InterfaceAlias, PrefixLength

# 2. Check if ports are listening
Write-Host "`n2. Listening Ports (5118, 7015):" -ForegroundColor Yellow
$listeningPorts = netstat -an | Select-String "LISTENING" | Select-String "5118|7015"
if ($listeningPorts) {
    $listeningPorts
} else {
    Write-Host "  ? Ports 5118 and 7015 are NOT listening" -ForegroundColor Red
    Write-Host "  ? Make sure the API is running!" -ForegroundColor Red
}

# 3. Check all network connections on these ports
Write-Host "`n3. All connections on ports 5118 and 7015:" -ForegroundColor Yellow
netstat -an | Select-String "5118|7015"

# 4. Check Firewall Rules
Write-Host "`n4. Firewall Rules:" -ForegroundColor Yellow
$rules = Get-NetFirewallRule | Where-Object {
    $_.DisplayName -like "*5118*" -or 
    $_.DisplayName -like "*7015*" -or 
    $_.DisplayName -like "*OfflineAI*"
} | Select-Object DisplayName, Enabled, Direction, Action
$rules | Format-Table -AutoSize

# 5. Check Windows Firewall Status
Write-Host "`n5. Windows Firewall Status:" -ForegroundColor Yellow
Get-NetFirewallProfile | Select-Object Name, Enabled | Format-Table

# 6. Test local connectivity
Write-Host "`n6. Testing Local Connectivity:" -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://192.168.50.240:5118/health" -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
    Write-Host "  ? HTTP (5118): $($response.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "  ? HTTP (5118): Failed - $($_.Exception.Message)" -ForegroundColor Red
}

try {
    $response = Invoke-WebRequest -Uri "https://192.168.50.240:7015/health" -TimeoutSec 5 -SkipCertificateCheck -UseBasicParsing -ErrorAction Stop
    Write-Host "  ? HTTPS (7015): $($response.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "  ? HTTPS (7015): Failed - $($_.Exception.Message)" -ForegroundColor Red
}

# 7. Check network profile type
Write-Host "`n7. Network Profile Type:" -ForegroundColor Yellow
Get-NetConnectionProfile | Select-Object Name, NetworkCategory, InterfaceAlias | Format-Table

# 8. Suggestions
Write-Host "`n=== Troubleshooting Steps ===" -ForegroundColor Cyan
Write-Host "1. Make sure the API is running (check if ports are LISTENING above)"
Write-Host "2. Network should be 'Private' not 'Public' for easier access"
Write-Host "3. Try disabling Windows Firewall temporarily to test: Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False"
Write-Host "4. Check if antivirus is blocking connections"
Write-Host "5. Try accessing from this machine first: http://192.168.50.240:5118/swagger"
Write-Host "6. Verify the API is bound to 192.168.50.240 in launchSettings.json"
Write-Host ""
