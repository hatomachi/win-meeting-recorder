$od = "C:\Users\sohik\OneDrive"
Get-ChildItem -Path $od -Directory | ForEach-Object {
    $dirName = $_.Name
    # Keep only desktop
    if ($dirName -notlike "*desktop*" -and $dirName -notmatch "[\x80-\xFF].*[\x80-\xFF]") {
        $lnk = Join-Path $_.FullName "WinMeetingRecorder.lnk"
        if (Test-Path $lnk) {
            Remove-Item $lnk -Force
        }
    }
}
Write-Output "Cleaned up other folders"
