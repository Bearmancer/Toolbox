Set-Alias oc opencode

Remove-Alias ls
Set-Alias ls eza
function la { eza -la }

Invoke-Expression (& { (zoxide init powershell | Out-String) })