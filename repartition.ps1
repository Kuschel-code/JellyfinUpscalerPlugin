Remove-Partition -DiskNumber 2 -PartitionNumber 1 -Confirm:$false
Remove-Partition -DiskNumber 2 -PartitionNumber 2 -Confirm:$false
Remove-Partition -DiskNumber 2 -PartitionNumber 3 -Confirm:$false
Remove-Partition -DiskNumber 2 -PartitionNumber 4 -Confirm:$false
New-Partition -DiskNumber 2 -Size 450GB -DriveLetter E
New-Partition -DiskNumber 2 -UseMaximumSize -DriveLetter F
Format-Volume -DriveLetter E -FileSystem NTFS -Confirm:$false
Format-Volume -DriveLetter F -FileSystem NTFS -Confirm:$false
