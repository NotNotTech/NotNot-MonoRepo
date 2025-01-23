<#
.SYNOPSIS
   Deletes matching folders from NuGet global packages directory

.DESCRIPTION
   • Gets NuGet global packages path from dotnet CLI
   • Finds folders matching input filter pattern
   • Deletes matching folders recursively
   • Logs deleted paths to console

.PARAMETER Filter
   Wildcard pattern to match folder names (e.g. "notnot*")

.EXAMPLE
   .\delete-packages.ps1 -Filter "notnot*"
   
.NOTES
   • Requires dotnet CLI installed
   • Uses force deletion - no confirmation prompt
   • Performs recursive deletion
#>

param(
	# Filter pattern for matching folder names
	[Parameter(Mandatory = $true)]
	[string]$Filter
)

# Extract global packages path from dotnet CLI output
# Format: "global-packages: C:\Users\{user}\.nuget\packages\"
$globalPackagesPath = dotnet nuget locals global-packages --list | 
Select-String -Pattern "^global-packages: (.+)$" | 
ForEach-Object { $_.Matches.Groups[1].Value }

Write-Host "Will delete all packages matching '$Filter' from '$globalPackagesPath'"

# Find all directories matching filter pattern
$matchingFolders = Get-ChildItem -Path $globalPackagesPath -Directory -Filter $Filter

# Delete each matching folder and log to console
Write-Host "Deleting folders:"
$matchingFolders | Select-Object -ExpandProperty FullName | ForEach-Object { 
	Write-Host "- $_"
	Remove-Item -Path $_ -Recurse -Force 
}