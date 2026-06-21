using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PasswordManagerLocal.Helpers;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.ViewModels.Auth;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly IEndpoints _endpoints;
    private readonly Action _navigateToRegistration;
    private readonly Func<Guid, Task> _onAuthenticationSucceededAsync;
    private Func<Task>? _navigateBackAsync;

    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _rememberMe;
    private bool _isPasswordVisible;
    private bool _isBusy;
    private bool _isBackButtonVisible;
    private bool _isDeviceTransferIntroVisible;
    private bool _isDeviceTransferCodeVisible;
    private bool _isDeviceTransferFinished;
    private bool _isDeviceTransferSuccess;
    private string _deviceTransferCode = string.Empty;
    private Bitmap? _deviceTransferQrCode;
    private CancellationTokenSource? _deviceTransferPolling;

    public LoginViewModel(
        UiPreferencesService uiPreferences,
        IEndpoints endpoints,
        Action navigateToRegistration,
        Func<Guid, Task> onAuthenticationSucceededAsync)
        : base(uiPreferences)
    {
        _endpoints = endpoints;
        _navigateToRegistration = navigateToRegistration;
        _onAuthenticationSucceededAsync = onAuthenticationSucceededAsync;

        LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
        ExecutePrimaryActionCommand = ReactiveCommand.CreateFromTask(ExecutePrimaryActionAsync);
        NavigateToRegistrationCommand = ReactiveCommand.Create(_navigateToRegistration);
        NavigateBackCommand = ReactiveCommand.CreateFromTask(NavigateBackAsync);
        TogglePasswordVisibilityCommand = ReactiveCommand.Create(TogglePasswordVisibility);
        ShowDeviceTransferIntroCommand = ReactiveCommand.Create(ShowDeviceTransferIntro);
        StartDeviceTransferCommand = ReactiveCommand.CreateFromTask(StartDeviceTransferAsync);
        CancelDeviceTransferCommand = ReactiveCommand.CreateFromTask(CancelDeviceTransferAsync);
        CheckDeviceTransferStatusCommand = ReactiveCommand.CreateFromTask(CheckDeviceTransferStatusAsync);
        FinishDeviceTransferCommand = ReactiveCommand.Create(FinishDeviceTransfer);
        CopyDeviceTransferCodeCommand = ReactiveCommand.CreateFromTask(CopyDeviceTransferCodeAsync);
    }

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public bool RememberMe
    {
        get => _rememberMe;
        set => this.RaiseAndSetIfChanged(ref _rememberMe, value);
    }

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isPasswordVisible, value);
            this.RaisePropertyChanged(nameof(PasswordMaskCharacter));
            this.RaisePropertyChanged(nameof(PasswordVisibilityToggleText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool IsBackButtonVisible
    {
        get => _isBackButtonVisible;
        private set => this.RaiseAndSetIfChanged(ref _isBackButtonVisible, value);
    }

    public bool IsLoginFormVisible => !IsDeviceTransferIntroVisible && !IsDeviceTransferCodeVisible && !IsDeviceTransferFinished;

    public bool IsDeviceTransferIntroVisible
    {
        get => _isDeviceTransferIntroVisible;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDeviceTransferIntroVisible, value);
            this.RaisePropertyChanged(nameof(IsLoginFormVisible));
        }
    }

    public bool IsDeviceTransferCodeVisible
    {
        get => _isDeviceTransferCodeVisible;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDeviceTransferCodeVisible, value);
            this.RaisePropertyChanged(nameof(IsLoginFormVisible));
        }
    }

    public bool IsDeviceTransferFinished
    {
        get => _isDeviceTransferFinished;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDeviceTransferFinished, value);
            this.RaisePropertyChanged(nameof(IsLoginFormVisible));
        }
    }

    public bool IsDeviceTransferSuccess
    {
        get => _isDeviceTransferSuccess;
        private set => this.RaiseAndSetIfChanged(ref _isDeviceTransferSuccess, value);
    }

    public string DeviceTransferCode
    {
        get => _deviceTransferCode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _deviceTransferCode, value);
            this.RaisePropertyChanged(nameof(ReadableDeviceTransferCode));
            DeviceTransferQrCode = EnrollmentQrCodeService.CreateQrCodeBitmap(value);
        }
    }

    public string ReadableDeviceTransferCode => BuildReadableCode(DeviceTransferCode);

    public Bitmap? DeviceTransferQrCode
    {
        get => _deviceTransferQrCode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _deviceTransferQrCode, value);
            this.RaisePropertyChanged(nameof(HasDeviceTransferQrCode));
        }
    }

    public bool HasDeviceTransferQrCode => DeviceTransferQrCode is not null;

    public OperationMessageState DeviceTransferStatus { get; } = new();

    public char PasswordMaskCharacter => IsPasswordVisible ? '\0' : '●';

    public ReactiveCommand<Unit, Unit> LoginCommand { get; }

    public ReactiveCommand<Unit, Unit> ExecutePrimaryActionCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateToRegistrationCommand { get; }

    public ReactiveCommand<Unit, Unit> NavigateBackCommand { get; }

    public ReactiveCommand<Unit, Unit> TogglePasswordVisibilityCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowDeviceTransferIntroCommand { get; }

    public ReactiveCommand<Unit, Unit> StartDeviceTransferCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelDeviceTransferCommand { get; }

    public ReactiveCommand<Unit, Unit> CheckDeviceTransferStatusCommand { get; }

    public ReactiveCommand<Unit, Unit> FinishDeviceTransferCommand { get; }

    public ReactiveCommand<Unit, Unit> CopyDeviceTransferCodeCommand { get; }

    public string Title => GetTranslation("Login_Title");

    public string BackLabel => GetTranslation("Common_Back");

    public string Subtitle => GetTranslation("Login_Subtitle");

    public string UsernameLabel => GetTranslation("Login_Username_Label");

    public string PasswordLabel => GetTranslation("Login_Password_Label");

    public string RememberMeLabel => GetTranslation("Login_RememberMe_Label");

    public string LoginButtonLabel => GetTranslation("Login_Button");

    public string NavigateToRegisterLabel => GetTranslation("Login_NavigateToRegister_Button");

    public string NoAccountText => GetTranslation("Login_NoAccount_Text");

    public string UsernamePlaceholder => GetTranslation("Login_Username_Placeholder");

    public string PasswordPlaceholder => GetTranslation("Login_Password_Placeholder");

    public string PasswordVisibilityToggleText => GetTranslation(IsPasswordVisible ? "Common_Hide" : "Common_Show");

    public string DeviceTransferButtonLabel => GetTranslation("Login_DeviceTransfer_Button");

    public string DeviceTransferTitle => GetTranslation("Login_DeviceTransfer_Title");

    public string DeviceTransferDescription => GetTranslation("Login_DeviceTransfer_Description");

    public string DeviceTransferStartLabel => GetTranslation("Login_DeviceTransfer_Start");

    public string DeviceTransferCodeTitle => GetTranslation("Login_DeviceTransfer_CodeTitle");

    public string DeviceTransferCodeDescription => GetTranslation("Login_DeviceTransfer_CodeDescription");

    public string DeviceTransferCodeReadabilityHint => GetTranslation("Login_DeviceTransfer_CodeReadabilityHint");

    public string DeviceTransferQrCodeLabel => GetTranslation("Login_DeviceTransfer_QrCodeLabel");

    public string DeviceTransferWaitingText => GetTranslation("Login_DeviceTransfer_Waiting");

    public string DeviceTransferCheckStatusLabel => GetTranslation("Login_DeviceTransfer_CheckStatus");

    public string DeviceTransferCopyCodeLabel => GetTranslation("Common_Copy");

    public string DeviceTransferFinishTitle => IsDeviceTransferSuccess
        ? GetTranslation("Login_DeviceTransfer_SuccessTitle")
        : GetTranslation("Login_DeviceTransfer_ErrorTitle");

    public string DeviceTransferFinishLabel => GetTranslation("Common_Ok");

    public string CancelLabel => GetTranslation("Common_Cancel");

    public string BusyText => GetTranslation("Common_Loading");

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(BackLabel));
        this.RaisePropertyChanged(nameof(Subtitle));
        this.RaisePropertyChanged(nameof(UsernameLabel));
        this.RaisePropertyChanged(nameof(PasswordLabel));
        this.RaisePropertyChanged(nameof(RememberMeLabel));
        this.RaisePropertyChanged(nameof(LoginButtonLabel));
        this.RaisePropertyChanged(nameof(NavigateToRegisterLabel));
        this.RaisePropertyChanged(nameof(NoAccountText));
        this.RaisePropertyChanged(nameof(UsernamePlaceholder));
        this.RaisePropertyChanged(nameof(PasswordPlaceholder));
        this.RaisePropertyChanged(nameof(PasswordVisibilityToggleText));
        this.RaisePropertyChanged(nameof(DeviceTransferButtonLabel));
        this.RaisePropertyChanged(nameof(DeviceTransferTitle));
        this.RaisePropertyChanged(nameof(DeviceTransferDescription));
        this.RaisePropertyChanged(nameof(DeviceTransferStartLabel));
        this.RaisePropertyChanged(nameof(DeviceTransferCodeTitle));
        this.RaisePropertyChanged(nameof(DeviceTransferCodeDescription));
        this.RaisePropertyChanged(nameof(DeviceTransferCodeReadabilityHint));
        this.RaisePropertyChanged(nameof(DeviceTransferQrCodeLabel));
        this.RaisePropertyChanged(nameof(DeviceTransferWaitingText));
        this.RaisePropertyChanged(nameof(DeviceTransferCheckStatusLabel));
        this.RaisePropertyChanged(nameof(DeviceTransferCopyCodeLabel));
        this.RaisePropertyChanged(nameof(DeviceTransferFinishTitle));
        this.RaisePropertyChanged(nameof(DeviceTransferFinishLabel));
        this.RaisePropertyChanged(nameof(CancelLabel));
        this.RaisePropertyChanged(nameof(BusyText));
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        ResetDeviceTransferState(true);
    }

    public void Reset()
    {
        Username = string.Empty;
        Password = string.Empty;
        RememberMe = false;
        IsPasswordVisible = false;
        ClearStatusMessage();
        ResetDeviceTransferState(false);
    }


    public async Task<bool> TryNavigateBackAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        if (IsDeviceTransferCodeVisible)
        {
            await CancelDeviceTransferAsync();
            return true;
        }

        if (IsDeviceTransferIntroVisible || IsDeviceTransferFinished)
        {
            ResetDeviceTransferState(true);
            return true;
        }

        return false;
    }



    public void SetBackNavigation(bool isVisible, Func<Task>? navigateBackAsync)
    {
        IsBackButtonVisible = isVisible;
        _navigateBackAsync = navigateBackAsync;
    }


    private async Task NavigateBackAsync()
    {
        if (IsBusy)
            return;

        if (await TryNavigateBackAsync())
            return;

        if (_navigateBackAsync is not null)
            await _navigateBackAsync();
    }

    private async Task ExecutePrimaryActionAsync()
    {
        if (IsBusy)
            return;

        if (IsLoginFormVisible)
        {
            await LoginAsync();
            return;
        }

        if (IsDeviceTransferIntroVisible)
        {
            await StartDeviceTransferAsync();
            return;
        }

        if (IsDeviceTransferCodeVisible)
        {
            await CheckDeviceTransferStatusAsync();
            return;
        }

        if (IsDeviceTransferFinished)
        {
            FinishDeviceTransfer();
        }
    }


    private async Task LoginAsync()
    {
        if (IsBusy || !IsLoginFormVisible)
            return;

        ClearStatusMessage();

        if (string.IsNullOrWhiteSpace(Username))
        {
            ShowErrorMessage(GetTranslation("Validation_Username_Required"));
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ShowErrorMessage(GetTranslation("Validation_Password_Required"));
            return;
        }

        var passwordHash = SecretTransform.HashPassword(Password);
        Password = string.Empty;

        try
        {
            IsBusy = true;
            var token = await _endpoints.LoginAsync(new LoginRequest
            {
                Username = Username.Trim(),
                Password = passwordHash,
                RememberMe = RememberMe
            });

            await _onAuthenticationSucceededAsync(token);
            Reset();
        }
        catch (Exception ex)
        {
            ShowErrorMessage(GetSafeErrorMessage(ex));
        }
        finally
        {
            IsBusy = false;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordHash);
        }
    }

    private void ShowDeviceTransferIntro()
    {
        ClearStatusMessage();
        IsDeviceTransferIntroVisible = true;
        IsDeviceTransferCodeVisible = false;
        IsDeviceTransferFinished = false;
        DeviceTransferStatus.Clear();
    }

    private async Task StartDeviceTransferAsync()
    {
        DeviceTransferStatus.Clear();

        try
        {
            IsBusy = true;
            var response = await _endpoints.StartDeviceEnrollmentAsync();
            DeviceTransferCode = response.Code;
            DeviceTransferStatus.ShowInformation(GetTranslation("Login_DeviceTransfer_CodeReady"), autoDismiss: true);
            IsDeviceTransferIntroVisible = false;
            IsDeviceTransferCodeVisible = true;
            IsDeviceTransferFinished = false;
            StartStatusPolling();
        }
        catch (Exception ex)
        {
            DeviceTransferStatus.ShowError(GetSafeErrorMessage(ex));
            IsDeviceTransferSuccess = false;
            IsDeviceTransferIntroVisible = false;
            IsDeviceTransferCodeVisible = false;
            IsDeviceTransferFinished = true;
            this.RaisePropertyChanged(nameof(DeviceTransferFinishTitle));
        }
        finally
        {
            IsBusy = false;
        }
    }



    private async Task CopyDeviceTransferCodeAsync()
    {
        try
        {
            if (await TryCopyTextToClipboardAsync(DeviceTransferCode))
            {
                if (!DeviceTransferStatus.IsError)
                    DeviceTransferStatus.ShowSuccess(GetTranslation("Common_Copied"));
            }
            else
                DeviceTransferStatus.ShowError(GetTranslation("Error_ClipboardUnavailable"));
        }
        catch
        {
            DeviceTransferStatus.ShowError(GetTranslation("Error_ClipboardUnavailable"));
        }
    }

    private async Task CancelDeviceTransferAsync()
    {
        _deviceTransferPolling?.Cancel();
        _deviceTransferPolling = null;

        try
        {
            await _endpoints.CancelDeviceEnrollmentAsync();
        }
        catch
        {
        }

        ResetDeviceTransferState(true);
    }

    private async Task CheckDeviceTransferStatusAsync()
    {
        DeviceTransferStatus.Clear();

        try
        {
            var status = await _endpoints.GetDeviceEnrollmentStatusAsync();
            ApplyDeviceTransferStatus(status);
        }
        catch (Exception ex)
        {
            DeviceTransferStatus.ShowError(GetSafeErrorMessage(ex));
        }
    }

    private void FinishDeviceTransfer() =>
        ResetDeviceTransferState(true);

    private void StartStatusPolling()
    {
        _deviceTransferPolling?.Cancel();
        _deviceTransferPolling = new CancellationTokenSource();
        var ct = _deviceTransferPolling.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    if (ct.IsCancellationRequested)
                        return;

                    var status = await _endpoints.GetDeviceEnrollmentStatusAsync(ct);
                    await Dispatcher.UIThread.InvokeAsync(() => ApplyDeviceTransferStatus(status));

                    if (status.IsFinished)
                        return;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => DeviceTransferStatus.ShowError(GetSafeErrorMessage(ex)));
            }
        }, ct);
    }

    private void ApplyDeviceTransferStatus(DeviceEnrollmentStatusResponse status)
    {
        if (status.State == DeviceEnrollmentState.Waiting)
        {
            if (DeviceTransferStatus.Kind is not OperationMessageKind.Error and not OperationMessageKind.Success)
                DeviceTransferStatus.ShowInformation(GetTranslation("Login_DeviceTransfer_Waiting"));

            return;
        }

        if (status.State == DeviceEnrollmentState.Completed)
        {
            _deviceTransferPolling?.Cancel();
            IsDeviceTransferSuccess = true;
            DeviceTransferStatus.ShowSuccess(GetTranslation("Login_DeviceTransfer_SuccessMessage"));
            IsDeviceTransferCodeVisible = false;
            IsDeviceTransferFinished = true;
            this.RaisePropertyChanged(nameof(DeviceTransferFinishTitle));
            return;
        }

        if (status.State == DeviceEnrollmentState.Failed || status.State == DeviceEnrollmentState.Expired)
        {
            _deviceTransferPolling?.Cancel();
            IsDeviceTransferSuccess = false;
            DeviceTransferStatus.ShowError(status.ErrorCode == PasswordManagerLocalBackend.Exceptions.DeviceEnrollmentErrorCode.Unknown
                ? GetTranslation("Login_DeviceTransfer_ErrorMessage")
                : GetDeviceEnrollmentErrorMessage(status.ErrorCode));
            IsDeviceTransferCodeVisible = false;
            IsDeviceTransferFinished = true;
            this.RaisePropertyChanged(nameof(DeviceTransferFinishTitle));
        }
    }

    private void ResetDeviceTransferState(bool clearCode)
    {
        _deviceTransferPolling?.Cancel();
        _deviceTransferPolling = null;
        IsDeviceTransferIntroVisible = false;
        IsDeviceTransferCodeVisible = false;
        IsDeviceTransferFinished = false;
        IsDeviceTransferSuccess = false;
        DeviceTransferStatus.Clear();

        if (clearCode)
            DeviceTransferCode = string.Empty;
    }

    private static string BuildReadableCode(string code) =>
        string.IsNullOrEmpty(code)
            ? string.Empty
            : code.Replace("0", "0\u0338", StringComparison.Ordinal);

    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;
}
