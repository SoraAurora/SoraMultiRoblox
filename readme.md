# Roblox Multi-Instance
<img width="1341" height="695" alt="image" src="https://github.com/user-attachments/assets/d2d7a137-97d8-4e17-bf23-0bd7e674f5a6" />

Roblox only allows one instances of roblox on a computer at any point of time , can be bypass by closing a handled called 

`\Sessions\*\BaseNamedObjects\ROBLOX_singletonEvent` 

So this code js automatically finds `RobloxPlayerBeta.exe` processes and closes the handle named `\Sessions\*\BaseNamedObjects\ROBLOX_singletonEvent`. It checks every 15 for any matching processes.

## How to Use

1. Compile

1. Open Command Prompt as Administrator
2. Navigate to the folder containing `main.cs`
3. Run: `csc /out:main.exe main.cs`
4. Run: `main.exe`

## Output 

```
Roblox Handle Closer - Started
Monitoring for RobloxPlayerBeta.exe processes...
Target handle: *\BaseNamedObjects\ROBLOX_singletonEvent (any session)
Press Ctrl+C to exit

[15:14:04] Checking for processes...
[15:14:04] No RobloxPlayerBeta.exe processes found.
[15:14:19] Checking for processes...
[15:14:19] Found 1 RobloxPlayerBeta.exe process(es).
  - PID 8252: Scanning handles...
    Found target handle: \Sessions\1\BaseNamedObjects\ROBLOX_singletonEvent
    Closing handle 0x714...
    Successfully closed handle!
    Closed 1 target handle(s)
[15:14:35] Checking for processes...
[15:14:35] Found 2 RobloxPlayerBeta.exe process(es).
  - PID 8252: Scanning handles...
    No target handles found
  - PID 7836: Scanning handles...
    Found target handle: \Sessions\1\BaseNamedObjects\ROBLOX_singletonEvent
    Closing handle 0x750...
    Successfully closed handle!
    Closed 1 target handle(s)
[15:15:24] Checking for processes...
```

## Important Notes

- ⚠️ **Must run as Administrator** - Required to access other processes' handles
