param (
    [string]$Path = ".\",
    [string]$GlobalPropsPath = ".\Directory.Build.props"
)

Write-Host "Comparing .csproj properties against $GlobalPropsPath..." -ForegroundColor Cyan

if (-not (Test-Path $GlobalPropsPath)) {
    Write-Warning "Global props file not found at $GlobalPropsPath"
    return
}

[xml]$globalProps = Get-Content $GlobalPropsPath
$globalPropertyNames = @()
if ($globalProps.Project.PropertyGroup) {
    foreach ($pg in $globalProps.Project.PropertyGroup) {
        if ($pg.ChildNodes) {
            $globalPropertyNames += $pg.ChildNodes | Where-Object { $_.NodeType -eq 'Element' } | Select-Object -ExpandProperty Name
        }
    }
}
$globalPropertyNames = $globalPropertyNames | Select-Object -Unique

Write-Host "Global properties found: $($globalPropertyNames.Count)" -ForegroundColor DarkGray

$projects = Get-ChildItem -Path $Path -Recurse -Filter "*.csproj"

foreach ($project in $projects) {
    try {
        [xml]$projXml = Get-Content $project.FullName
        if ($projXml.Project.PropertyGroup) {
            foreach ($pg in $projXml.Project.PropertyGroup) {
                if ($pg.ChildNodes) {
                    $projProperties = $pg.ChildNodes | Where-Object { $_.NodeType -eq 'Element' } | Select-Object -ExpandProperty Name
                    
                    foreach ($prop in $projProperties) {
                        if ($globalPropertyNames -contains $prop) {
                            Write-Host "Duplicate property found in $($project.Name): '$prop' is already defined in Directory.Build.props" -ForegroundColor Red
                        }
                    }
                }
            }
        }
    } catch {
        Write-Warning "Failed to parse $($project.FullName): $_"
    }
}
Write-Host "Property comparison complete." -ForegroundColor Cyan
