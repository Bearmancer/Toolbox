param (
    [string]$Path = ".\"
)

Write-Host "Checking for inline PackageReference versions in .csproj files..." -ForegroundColor Cyan

$projects = Get-ChildItem -Path $Path -Recurse -Filter "*.csproj"

foreach ($project in $projects) {
    try {
        [xml]$projXml = Get-Content $project.FullName
        if ($projXml.Project.ItemGroup) {
            foreach ($ig in $projXml.Project.ItemGroup) {
                if ($ig.PackageReference) {
                    foreach ($pkg in $ig.PackageReference) {
                        $hasVersionAttr = $null -ne $pkg.GetAttribute("Version") -and $pkg.GetAttribute("Version") -ne ""
                        $hasVersionNode = $null -ne $pkg.Version -and $pkg.Version -ne ""
                        
                        if ($hasVersionAttr -or $hasVersionNode) {
                            $pkgName = ""
                            if ($null -ne $pkg.Include) { $pkgName = $pkg.Include }
                            elseif ($null -ne $pkg.Update) { $pkgName = $pkg.Update }
                            
                            Write-Host "Inline version found in $($project.Name): Package '$pkgName' has an explicit version." -ForegroundColor Red
                        }
                    }
                }
            }
        }
    } catch {
        Write-Warning "Failed to parse $($project.FullName): $_"
    }
}
Write-Host "Package version check complete." -ForegroundColor Cyan
