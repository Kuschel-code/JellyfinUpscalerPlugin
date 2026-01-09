Set-Disk -Number 2 -IsReadOnly $false
Set-Partition -DriveLetter E -IsReadOnly $false
Set-Partition -DriveLetter F -IsReadOnly $false
Get-Disk -Number 2 | Format-List
Get-Partition -DiskNumber 2 | Format-List
