Option Explicit

Dim shell, files, basePath, quote, sevenZip, browser, arguments, item, exitCode, logPath
Dim appPath, payloadPath, stampPath, stamp
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")

basePath = files.GetParentFolderName(WScript.ScriptFullName)
logPath = basePath & "\Tech-Browser-launcher.log"
quote = Chr(34)
sevenZip = quote & basePath & "\7za.exe" & quote
appPath = basePath & "\app"
payloadPath = basePath & "\payload.7z"
stampPath = appPath & "\payload.stamp"
browser = quote & appPath & "\Tech Browser.exe" & quote

LogMessage "Launcher started; BasePath=" & basePath

' The stamp records which payload the app folder was extracted from, so a newer package replaces it.
' payload.id is written by the build (version and hash); the size is a fallback for hand-made packages.
stamp = CStr(files.GetFile(payloadPath).Size)
If files.FileExists(basePath & "\payload.id") Then stamp = ReadFirstLine(basePath & "\payload.id")

If Not IsExtracted() Then
    LogMessage "Browser missing or from another package; starting 7-Zip"
    ' Extract beside the target and rename at the end: an interrupted run must never look complete.
    RemoveFolder appPath & ".tmp"
    exitCode = shell.Run(sevenZip & " x " & quote & payloadPath & quote & " -o" & quote & appPath & ".tmp" & quote & " -y", 0, True)
    LogMessage "7-Zip exit code=" & exitCode
    If exitCode <> 0 Then
        RemoveFolder appPath & ".tmp"
        MsgBox "Tech Browser konnte nicht entpackt werden. Fehlercode: " & exitCode, vbCritical, "Tech Browser"
        WScript.Quit exitCode
    End If

    If ReplaceApp() Then
        LogMessage "Extraction complete"
    Else
        ' Renaming fails while the old version is running; keep using it.
        LogMessage "Existing version is in use; keeping it"
        RemoveFolder appPath & ".tmp"
    End If
End If

arguments = ""
For Each item In WScript.Arguments
    arguments = arguments & " " & quote & Replace(item, quote, quote & quote) & quote
Next

If Not files.FileExists(appPath & "\Tech Browser.exe") Then
    LogMessage "No usable browser folder after extraction"
    MsgBox "Tech Browser konnte nicht bereitgestellt werden.", vbCritical, "Tech Browser"
    WScript.Quit 1
End If

' Arguments can contain an address and are not logged.
LogMessage "Starting browser"
exitCode = shell.Run(browser & arguments, 1, True)
LogMessage "Browser exit code=" & exitCode
WScript.Quit exitCode

Function IsExtracted()
    IsExtracted = False
    If Not files.FileExists(appPath & "\Tech Browser.exe") Then Exit Function
    If Not files.FileExists(stampPath) Then Exit Function
    IsExtracted = (ReadFirstLine(stampPath) = stamp)
End Function

Function ReadFirstLine(path)
    Dim stream
    ReadFirstLine = ""
    Set stream = files.OpenTextFile(path, 1)
    If Not stream.AtEndOfStream Then ReadFirstLine = stream.ReadLine
    stream.Close
End Function

Function ReplaceApp()
    Dim stream
    ReplaceApp = False
    RemoveFolder appPath & ".old"
    On Error Resume Next
    If files.FolderExists(appPath) Then
        files.MoveFolder appPath, appPath & ".old"
        If Err.Number <> 0 Then Exit Function
    End If
    Set stream = files.CreateTextFile(appPath & ".tmp\payload.stamp", True)
    stream.WriteLine stamp
    stream.Close
    files.MoveFolder appPath & ".tmp", appPath
    If Err.Number <> 0 Then
        ' Put the previous version back so there is still something to start.
        Err.Clear
        If files.FolderExists(appPath & ".old") Then files.MoveFolder appPath & ".old", appPath
        Exit Function
    End If
    On Error GoTo 0
    RemoveFolder appPath & ".old"
    ReplaceApp = True
End Function

Sub RemoveFolder(path)
    On Error Resume Next
    If files.FolderExists(path) Then files.DeleteFolder path, True
    On Error GoTo 0
End Sub

Sub LogMessage(message)
    Dim stream
    On Error Resume Next
    Set stream = files.OpenTextFile(logPath, 8, True)
    stream.WriteLine Now & "  " & message
    stream.Close
    On Error GoTo 0
End Sub
