Remove-PartitionAccessPath -DiskNumber 2 -PartitionNumber 1 -AccessPath "E:\"
Add-PartitionAccessPath -DiskNumber 2 -PartitionNumber 3 -AccessPath "E:\"
Format-Volume -DriveLetter E -FileSystem NTFS -Confirm:$false
