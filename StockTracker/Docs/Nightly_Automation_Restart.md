# Nightly Automation with Application Restart

## Overview

The StockTracker application now automatically **restarts and logs in** before executing the nightly automation task. This ensures a clean, stable state for the automated processes.

---

## How It Works

### 1. **Timer Trigger (22:00)**
At 22:00 (10:00 PM) every day, the nightly automation timer triggers:
- `MainWindowViewModel.TryRunNightlyAutomationAsync()` is called
- Instead of running directly, the app prepares to restart

### 2. **Application Restart**
The restart process:
```csharp
RestartApplicationForNightlyAutomation()
```
- Gets current process executable path
- Starts new instance with `--nightly-automation` argument
- Shuts down current instance cleanly

### 3. **Auto-Login on Restart**
When the app restarts with `--nightly-automation` flag:
- `App.IsNightlyAutomationRestart` is set to `true`
- LoginWindow detects this and triggers `TryAutoLoginForNightlyAutomation()`
- Automatically uses saved credentials (if available)
- Logs in without user interaction

### 4. **Main Window Initialization**
After successful login:
- `MainWindowViewModel.InitializeAsync()` detects restart flag
- Waits 2 seconds for stabilization
- Calls `RunNightlyAutomationAfterRestartAsync()`

### 5. **Execute Nightly Tasks**
The automation runs with a fresh session:
1. Update TWSE/margin data
2. Refresh tracked stocks
3. Scan entire market (ranking)
4. Export reports (XML/HTML)
5. Publish website to GitHub
6. Send notification emails

### 6. **Continue Normal Operation**
After completion:
- `_nightlyCompletedToday` flag is set
- Timer restarts for next day
- App continues running normally

---

## Code Changes

### 1. App.xaml.cs
```csharp
public static bool IsNightlyAutomationRestart { get; private set; }

protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    IsNightlyAutomationRestart = e.Args.Contains("--nightly-automation");
    var loginWindow = new LoginWindow();
    loginWindow.Show();
}
```

### 2. LoginWindow.xaml.cs
```csharp
public LoginWindow()
{
    InitializeComponent();
    var vm = new LoginViewModel(new CapitalApiService());
    vm.LoginSucceeded += OnLoginSucceeded;
    DataContext = vm;
    LoadSavedCredentials();

    if (App.IsNightlyAutomationRestart)
    {
        Loaded += async (s, e) => await TryAutoLoginForNightlyAutomation();
    }
}

private async Task TryAutoLoginForNightlyAutomation()
{
    if (!(DataContext is LoginViewModel vm))
    {
        return;
    }

    if (string.IsNullOrWhiteSpace(vm.Account) || string.IsNullOrWhiteSpace(vm.Password))
    {
        vm.StatusMessage = "無法自動登入：未儲存帳號密碼";
        return;
    }

    vm.StatusMessage = "夜間排程：自動登入中...";
    await Task.Delay(500);

    if (vm.LoginCommand.CanExecute(null))
    {
        vm.LoginCommand.Execute(null);
    }
}
```

### 3. MainWindowViewModel.cs

#### InitializeAsync() - Detects Restart
```csharp
if (App.IsNightlyAutomationRestart)
{
    SystemMessage = "夜間排程：程式重啟完成，準備執行排程...";
    await Task.Delay(2000);
    _ = RunNightlyAutomationAfterRestartAsync();
}
else
{
    EnsureNightlyAutomationTimer();
}
```

#### TryRunNightlyAutomationAsync() - Triggers Restart
```csharp
private async Task TryRunNightlyAutomationAsync()
{
    if (_isNightlyAutomationRunning)
    {
        return;
    }

    var now = DateTime.Now;
    if (now.Hour == 0 && now.Minute < 5)
    {
        _nightlyCompletedToday = false;
    }
    if (now.Hour != 22 || now.Minute != 0)
    {
        return;
    }

    if (_nightlyCompletedToday)
    {
        return;
    }

    SystemMessage = "夜間排程觸發：準備重啟程式以確保穩定狀態...";
    await Task.Delay(2000);

    RestartApplicationForNightlyAutomation();
}
```

#### RestartApplicationForNightlyAutomation() - Executes Restart
```csharp
private void RestartApplicationForNightlyAutomation()
{
    try
    {
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var exePath = currentProcess.MainModule.FileName;

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--nightly-automation",
            UseShellExecute = true
        };

        System.Diagnostics.Process.Start(startInfo);

        Application.Current.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }
    catch (Exception ex)
    {
        SystemMessage = $"重啟失敗：{ex.Message}";
    }
}
```

#### RunNightlyAutomationAfterRestartAsync() - Post-Restart Runner
```csharp
private async Task RunNightlyAutomationAfterRestartAsync()
{
    _isNightlyAutomationRunning = true;
    try
    {
        await RunNightlyAutomationAsync();
        _nightlyCompletedToday = true;
        SystemMessage = "夜間排程完成。程式將繼續執行並等待明天排程...";
        EnsureNightlyAutomationTimer();
    }
    catch (Exception ex)
    {
        SystemMessage = "排程執行失敗: " + ex.Message;
    }
    finally
    {
        _isNightlyAutomationRunning = false;
    }
}
```

#### RunNightlyAutomationAsync() - Simplified (No Re-login)
```csharp
private async Task RunNightlyAutomationAsync()
{
    IsInitializingMainPage = true;
    MainPageProgressValue = 0;
    SystemMessage = "夜間排程執行中...";

    MainPageProgressValue = 10;
    SystemMessage = "夜間排程：更新法人資料中...";
    await UpdateTwseHistoryAsync();

    MainPageProgressValue = 35;
    SystemMessage = "夜間排程：刷新主頁股票中...";
    await RefreshAllTrackedStocksAsync();

    // ... rest of automation tasks
}
```

---

## Execution Flow Diagram

```
[22:00 Timer Trigger]
        ↓
[TryRunNightlyAutomationAsync]
        ↓
[SystemMessage: "準備重啟程式..."]
        ↓
[RestartApplicationForNightlyAutomation]
        ↓
[Start new process with --nightly-automation]
        ↓
[Shutdown current instance]
        ↓
        ↓
[New Instance Starts]
        ↓
[App.OnStartup detects --nightly-automation]
        ↓
[LoginWindow opens]
        ↓
[LoadSavedCredentials()]
        ↓
[TryAutoLoginForNightlyAutomation()]
        ↓
[Execute LoginCommand]
        ↓
[OnLoginSucceeded]
        ↓
[MainWindow opens]
        ↓
[MainWindowViewModel.InitializeAsync]
        ↓
[Detect App.IsNightlyAutomationRestart]
        ↓
[RunNightlyAutomationAfterRestartAsync]
        ↓
[RunNightlyAutomationAsync]
        ↓
[1. Update TWSE data]
        ↓
[2. Refresh tracked stocks]
        ↓
[3. Scan market]
        ↓
[4. Export reports]
        ↓
[5. Publish website]
        ↓
[6. Send emails]
        ↓
[Set _nightlyCompletedToday = true]
        ↓
[EnsureNightlyAutomationTimer (for tomorrow)]
        ↓
[Continue normal operation]
```

---

## Requirements

### Saved Credentials Required
For auto-login to work, the user must:
1. Check "記住帳號密碼" when logging in
2. Credentials are saved to:
   ```
   %LocalAppData%\StockTracker\login.dat
   ```

If credentials are not saved:
- Auto-login will fail
- Login window will show: "無法自動登入：未儲存帳號密碼"
- User must login manually

---

## Benefits

### 1. **Fresh State**
- Clears any memory leaks or resource buildup
- Resets API connections cleanly
- Ensures stable environment for automation

### 2. **Clean Login Session**
- New login token from Capital API
- Fresh quote connection
- Avoids session timeout issues

### 3. **Reliability**
- Reduces chance of automation failure
- Consistent environment every night
- Easier to debug issues (always same starting state)

### 4. **Automatic Recovery**
- If app was in bad state, restart fixes it
- No manual intervention needed
- Continues normal operation after completion

---

## Testing

### Manual Test Procedure

1. **Set Test Time:**
   Temporarily modify line 389 in `MainWindowViewModel.cs`:
   ```csharp
   if (now.Hour != 15 || now.Minute != 30)  // Test at 3:30 PM
   ```

2. **Enable Credential Saving:**
   - Login with "記住帳號密碼" checked
   - Verify `login.dat` file exists

3. **Wait for Trigger:**
   - Keep app running until test time
   - Watch SystemMessage for "準備重啟程式..."

4. **Observe Restart:**
   - App should close
   - New instance should start automatically
   - Login window should appear and auto-login

5. **Verify Automation:**
   - Main window should open
   - SystemMessage: "夜間排程：程式重啟完成..."
   - Progress bar should show automation tasks

6. **Check Completion:**
   - SystemMessage: "夜間排程完成..."
   - App continues running normally

### Command-Line Test

You can test the restart behavior manually:
```powershell
.\StockTracker.exe --nightly-automation
```

Expected behavior:
- App starts
- Login window auto-fills credentials
- Auto-login executes
- Main window shows "夜間排程：程式重啟完成..."
- Automation runs immediately

---

## Troubleshooting

### Issue: Auto-login fails
**Cause:** Credentials not saved  
**Solution:** Check "記住帳號密碼" and login once manually

### Issue: App doesn't restart
**Cause:** Exception in restart logic  
**Solution:** Check SystemMessage for error, verify executable path

### Issue: Automation doesn't run after restart
**Cause:** `App.IsNightlyAutomationRestart` not detected  
**Solution:** Verify `--nightly-automation` argument passed correctly

### Issue: Multiple instances running
**Cause:** Old instance didn't shut down  
**Solution:** Add timeout/force kill if needed

---

## Future Enhancements

### 1. Graceful Shutdown
- Save current state before restart
- Restore window positions/sizes
- Preserve user settings

### 2. Retry Logic
- If auto-login fails, retry 3 times
- If restart fails, log error and continue without restart
- Fallback to in-process automation

### 3. Logging
- Log restart events to file
- Track automation success/failure history
- Alert on consecutive failures

### 4. Configuration
- Make restart configurable (enable/disable)
- Allow user to set automation time
- Option to skip restart on weekends

---

## Security Considerations

### Credential Storage
Credentials are stored in **Base64 encoded** format (NOT encrypted):
```
%LocalAppData%\StockTracker\login.dat
```

⚠️ **Warning:** This is NOT secure encryption, just obfuscation.

For production, consider:
- Windows DPAPI for encryption
- Azure Key Vault for cloud storage
- Hardware security modules (HSM)

### Process Elevation
The restart does NOT require admin privileges because:
- Uses `UseShellExecute = true`
- Runs at same privilege level as parent
- No UAC prompt triggered

---

## Build Status

✅ Build successful  
✅ No compilation errors  
✅ .NET Framework 4.7.2 compatible  
✅ All async/await patterns correct  

---

## Summary

The nightly automation now:
1. ✅ Triggers at 22:00 daily
2. ✅ Restarts the application
3. ✅ Auto-logins using saved credentials
4. ✅ Executes automation in fresh state
5. ✅ Continues normal operation after completion

This ensures maximum reliability and stability for the automated nightly tasks!
