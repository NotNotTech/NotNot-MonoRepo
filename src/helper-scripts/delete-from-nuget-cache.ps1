# Get NuGet global packages path from dotnet CLI
$globalPackagesPath = dotnet nuget locals global-packages --list | Select-String -Pattern "^global-packages: (.+)$" | ForEach-Object { $_.Matches.Groups[1].Value }

# Find notnot* folders
$notnotFolders = Get-ChildItem -Path $globalPackagesPath -Directory -Filter "notnot*"

# Preview folders
Write-Host "Found folders to delete:"
$notnotFolders | Select-Object -ExpandProperty FullName | ForEach-Object { Write-Host "- $_" }

# Confirm deletion
$confirmation = Read-Host "Delete these folders? (y/n)"
if ($confirmation -eq 'y') {
	$notnotFolders | Remove-Item -Recurse -Force
	Write-Host "Folders deleted"
}
else {
	Write-Host "Cancelled"
}