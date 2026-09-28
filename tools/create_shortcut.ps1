$wsh = New-Object -ComObject WScript.Shell

# Standard desktop
$lnk1 = "C:\Users\sohik\Desktop\WinMeetingRecorder.lnk"
$s1 = $wsh.CreateShortcut($lnk1)
$s1.TargetPath = "C:\work\WinMeetingRecorder.exe"
$s1.WorkingDirectory = "C:\work"
$s1.Save()
Write-Output "Created: $lnk1"

# OneDrive directories
$od = "C:\Users\sohik\OneDrive"
if (Test-Path $od) {
    Get-ChildItem -Path $od -Directory | ForEach-Object {
        $dir = $_.FullName
        $lnk = Join-Path $dir "WinMeetingRecorder.lnk"
        $s = $wsh.CreateShortcut($lnk)
        $s.TargetPath = "C:\work\WinMeetingRecorder.exe"
        $s.WorkingDirectory = "C:\work"
        $s.Save()
        Write-Output "Created in OneDrive subdir: $lnk"
    }
}
