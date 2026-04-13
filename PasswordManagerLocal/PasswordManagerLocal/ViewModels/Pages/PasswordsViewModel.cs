using PasswordManagerLocal.Helpers;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Abstractions;
using Avalonia.Media;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class PasswordsViewModel : ViewModelBase
{
    private readonly IEndpoints _endpoints;

    private Guid _token;
    private PasswordItemViewModel? _selectedPassword;
    private string? _revealedPassword;
    private string? _statusMessage;
    private bool _isEditorOpen;
    private bool _isCreateMode;
    private string _editorName = string.Empty;
    private string _editorDescription = string.Empty;
    private string _editorColor = "#FFFFD700";
    private string _editorPassword = string.Empty;
    private bool _isEditorPasswordVisible;

    public PasswordsViewModel(UiPreferencesService uiPreferences, IEndpoints endpoints)
        : base(uiPreferences)
    {
        _endpoints = endpoints;

        Passwords = new ObservableCollection<PasswordItemViewModel>();

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        BeginCreatePasswordCommand = ReactiveCommand.Create(BeginCreatePassword);
        EditSelectedPasswordCommand = ReactiveCommand.CreateFromTask(EditSelectedPasswordAsync);
        DeleteSelectedPasswordCommand = ReactiveCommand.CreateFromTask(DeleteSelectedPasswordAsync);
        RevealPasswordCommand = ReactiveCommand.CreateFromTask(RevealPasswordAsync);
        HidePasswordCommand = ReactiveCommand.Create(HidePassword);
        SavePasswordCommand = ReactiveCommand.CreateFromTask(SavePasswordAsync);
        CancelPasswordEditorCommand = ReactiveCommand.Create(CancelPasswordEditor);
        ToggleEditorPasswordVisibilityCommand = ReactiveCommand.Create(ToggleEditorPasswordVisibility);
    }

    public ObservableCollection<PasswordItemViewModel> Passwords { get; }

    public PasswordItemViewModel? SelectedPassword
    {
        get => _selectedPassword;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPassword, value);
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(IsSelectionEmpty));
            HidePassword();
        }
    }

    public bool HasSelection => SelectedPassword is not null;

    public string? RevealedPassword
    {
        get => _revealedPassword;
        private set
        {
            this.RaiseAndSetIfChanged(ref _revealedPassword, value);
            this.RaisePropertyChanged(nameof(HasRevealedPassword));
            this.RaisePropertyChanged(nameof(PasswordDisplayValue));
        }
    }

    public bool HasRevealedPassword => !string.IsNullOrEmpty(RevealedPassword);

    public string PasswordDisplayValue => HasRevealedPassword
        ? RevealedPassword!
        : GetTranslation("Passwords_HiddenValue");

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _statusMessage, value);
            this.RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasPasswords => Passwords.Count > 0;

    public bool IsEmpty => Passwords.Count == 0;

    public bool IsSelectionEmpty => !HasSelection;

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set => this.RaiseAndSetIfChanged(ref _isEditorOpen, value);
    }

    public bool IsCreateMode
    {
        get => _isCreateMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isCreateMode, value);
            this.RaisePropertyChanged(nameof(EditorTitle));
            this.RaisePropertyChanged(nameof(SavePasswordButtonLabel));
            this.RaisePropertyChanged(nameof(EditorPasswordHint));
        }
    }

    public string EditorName
    {
        get => _editorName;
        set => this.RaiseAndSetIfChanged(ref _editorName, value);
    }

    public string EditorDescription
    {
        get => _editorDescription;
        set => this.RaiseAndSetIfChanged(ref _editorDescription, value);
    }

    public string EditorColor
    {
        get => _editorColor;
        set
        {
            this.RaiseAndSetIfChanged(ref _editorColor, value);
            this.RaisePropertyChanged(nameof(EditorColorBrush));
        }
    }

    public IBrush EditorColorBrush => ParseBrush(EditorColor);

    public string EditorPassword
    {
        get => _editorPassword;
        set => this.RaiseAndSetIfChanged(ref _editorPassword, value);
    }

    public bool IsEditorPasswordVisible
    {
        get => _isEditorPasswordVisible;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isEditorPasswordVisible, value);
            this.RaisePropertyChanged(nameof(EditorPasswordMaskCharacter));
            this.RaisePropertyChanged(nameof(EditorPasswordVisibilityToggleText));
        }
    }

    public char EditorPasswordMaskCharacter => IsEditorPasswordVisible ? '\0' : '●';

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> BeginCreatePasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> EditSelectedPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> DeleteSelectedPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> RevealPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> HidePasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> SavePasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelPasswordEditorCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleEditorPasswordVisibilityCommand { get; }

    public string Title => GetTranslation("Passwords_Title");

    public string Subtitle => GetTranslation("Passwords_Subtitle");

    public string AddPasswordButtonLabel => GetTranslation("Passwords_Add");

    public string RefreshButtonLabel => GetTranslation("Common_Refresh");

    public string EmptyStateTitle => GetTranslation("Passwords_Empty_Title");

    public string EmptyStateDescription => GetTranslation("Passwords_Empty_Description");

    public string DetailsTitle => GetTranslation("Passwords_Details_Title");

    public string DetailsEmptyTitle => GetTranslation("Passwords_Details_Empty_Title");

    public string DetailsEmptyDescription => GetTranslation("Passwords_Details_Empty_Description");

    public string NameLabel => GetTranslation("Common_Name");

    public string PasswordLabel => GetTranslation("Common_Password");

    public string DescriptionLabel => GetTranslation("Common_Description");

    public string ColorLabel => GetTranslation("Common_Color");

    public string CreatedAtLabel => GetTranslation("Common_CreatedAt");

    public string UpdatedAtLabel => GetTranslation("Common_UpdatedAt");

    public string RevealPasswordLabel => GetTranslation("Passwords_Reveal");

    public string HidePasswordLabel => GetTranslation("Passwords_Hide");

    public string EditPasswordLabel => GetTranslation("Common_Edit");

    public string DeletePasswordLabel => GetTranslation("Common_Delete");

    public string EditorTitle => GetTranslation(IsCreateMode ? "Passwords_Editor_CreateTitle" : "Passwords_Editor_EditTitle");

    public string SavePasswordButtonLabel => GetTranslation(IsCreateMode ? "Common_Add" : "Common_Save");

    public string CancelButtonLabel => GetTranslation("Common_Cancel");

    public string EditorNamePlaceholder => GetTranslation("Passwords_Editor_NamePlaceholder");

    public string EditorDescriptionPlaceholder => GetTranslation("Passwords_Editor_DescriptionPlaceholder");

    public string EditorColorPlaceholder => GetTranslation("Passwords_Editor_ColorPlaceholder");

    public string EditorPasswordPlaceholder => GetTranslation("Passwords_Editor_PasswordPlaceholder");

    public string EditorPasswordHint => GetTranslation(IsCreateMode ? "Passwords_Editor_PasswordHint_Create" : "Passwords_Editor_PasswordHint_Edit");

    public string EditorPasswordVisibilityToggleText => GetTranslation(IsEditorPasswordVisible ? "Common_Hide" : "Common_Show");

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(Subtitle));
        this.RaisePropertyChanged(nameof(AddPasswordButtonLabel));
        this.RaisePropertyChanged(nameof(RefreshButtonLabel));
        this.RaisePropertyChanged(nameof(EmptyStateTitle));
        this.RaisePropertyChanged(nameof(EmptyStateDescription));
        this.RaisePropertyChanged(nameof(DetailsTitle));
        this.RaisePropertyChanged(nameof(DetailsEmptyTitle));
        this.RaisePropertyChanged(nameof(DetailsEmptyDescription));
        this.RaisePropertyChanged(nameof(NameLabel));
        this.RaisePropertyChanged(nameof(PasswordLabel));
        this.RaisePropertyChanged(nameof(DescriptionLabel));
        this.RaisePropertyChanged(nameof(ColorLabel));
        this.RaisePropertyChanged(nameof(CreatedAtLabel));
        this.RaisePropertyChanged(nameof(UpdatedAtLabel));
        this.RaisePropertyChanged(nameof(RevealPasswordLabel));
        this.RaisePropertyChanged(nameof(HidePasswordLabel));
        this.RaisePropertyChanged(nameof(EditPasswordLabel));
        this.RaisePropertyChanged(nameof(DeletePasswordLabel));
        this.RaisePropertyChanged(nameof(EditorTitle));
        this.RaisePropertyChanged(nameof(SavePasswordButtonLabel));
        this.RaisePropertyChanged(nameof(CancelButtonLabel));
        this.RaisePropertyChanged(nameof(EditorNamePlaceholder));
        this.RaisePropertyChanged(nameof(EditorDescriptionPlaceholder));
        this.RaisePropertyChanged(nameof(EditorColorPlaceholder));
        this.RaisePropertyChanged(nameof(EditorPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(EditorPasswordHint));
        this.RaisePropertyChanged(nameof(EditorPasswordVisibilityToggleText));
        this.RaisePropertyChanged(nameof(PasswordDisplayValue));
    }

    public async Task LoadAsync(Guid token)
    {
        _token = token;
        await RefreshAsync();
    }

    public void Reset()
    {
        _token = Guid.Empty;
        Passwords.Clear();
        this.RaisePropertyChanged(nameof(HasPasswords));
        this.RaisePropertyChanged(nameof(IsEmpty));
        SelectedPassword = null;
        RevealedPassword = null;
        StatusMessage = null;
        IsEditorOpen = false;
        ResetEditorFields();
    }

    private async Task RefreshAsync()
    {
        if (_token == Guid.Empty)
        {
            return;
        }

        var selectedId = SelectedPassword?.Id;

        try
        {
            var passwords = await _endpoints.GetSavedPasswordsAsync(_token);
            Passwords.Clear();
        this.RaisePropertyChanged(nameof(HasPasswords));
        this.RaisePropertyChanged(nameof(IsEmpty));

            foreach (var password in passwords.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Passwords.Add(PasswordItemViewModel.Create(password, BeginEditPasswordAsync, DeletePasswordAsync));
            }

            this.RaisePropertyChanged(nameof(HasPasswords));
            this.RaisePropertyChanged(nameof(IsEmpty));

            SelectedPassword = selectedId is null
                ? Passwords.FirstOrDefault()
                : Passwords.FirstOrDefault(item => item.Id == selectedId) ?? Passwords.FirstOrDefault();

            StatusMessage = GetTranslation("Passwords_Refreshed");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void BeginCreatePassword()
    {
        IsCreateMode = true;
        IsEditorOpen = true;
        StatusMessage = null;
        ResetEditorFields();
    }

    private async Task EditSelectedPasswordAsync()
    {
        if (SelectedPassword is null)
        {
            return;
        }

        await BeginEditPasswordAsync(SelectedPassword);
    }

    private Task BeginEditPasswordAsync(PasswordItemViewModel password)
    {
        SelectedPassword = password;
        IsCreateMode = false;
        IsEditorOpen = true;
        StatusMessage = null;
        EditorName = password.Name;
        EditorDescription = password.Description;
        EditorColor = password.Color;
        EditorPassword = string.Empty;
        IsEditorPasswordVisible = false;
        return Task.CompletedTask;
    }

    private async Task DeleteSelectedPasswordAsync()
    {
        if (SelectedPassword is null)
        {
            return;
        }

        await DeletePasswordAsync(SelectedPassword);
    }

    private async Task DeletePasswordAsync(PasswordItemViewModel password)
    {
        try
        {
            await _endpoints.RemovePasswordAsync(_token, password.Id);
            await RefreshAsync();
            StatusMessage = GetTranslation("Passwords_Delete_Success");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task RevealPasswordAsync()
    {
        if (SelectedPassword is null)
        {
            return;
        }

        try
        {
            var passwordBytes = await _endpoints.GetUnsecurePasswordAsync(_token, SelectedPassword.Id);
            try
            {
                RevealedPassword = Encoding.UTF8.GetString(passwordBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void HidePassword()
    {
        RevealedPassword = null;
    }

    private async Task SavePasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(EditorName))
        {
            StatusMessage = GetTranslation("Validation_PasswordName_Required");
            return;
        }

        try
        {
            string successMessage;

            if (IsCreateMode)
            {
                if (string.IsNullOrWhiteSpace(EditorPassword))
                {
                    StatusMessage = GetTranslation("Validation_RegisterPassword_Required");
                    return;
                }

                var rawPassword = SecretTransform.Utf8Bytes(EditorPassword);
                try
                {
                    await _endpoints.AddNewPasswordAsync(_token, new NewPasswordRequest
                    {
                        Name = EditorName.Trim(),
                        Description = EditorDescription.Trim(),
                        Color = EditorColor.Trim(),
                        Password = rawPassword
                    });
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(rawPassword);
                }

                successMessage = GetTranslation("Passwords_Save_CreateSuccess");
            }
            else if (SelectedPassword is not null)
            {
                byte[]? rawPassword = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(EditorPassword))
                    {
                        rawPassword = SecretTransform.Utf8Bytes(EditorPassword);
                    }

                    await _endpoints.UpdatePasswordAsync(_token, new UpdatePasswordRequest
                    {
                        Id = SelectedPassword.Id,
                        Name = EditorName.Trim(),
                        Description = EditorDescription.Trim(),
                        Color = EditorColor.Trim(),
                        Password = rawPassword
                    });
                }
                finally
                {
                    if (rawPassword is not null)
                    {
                        CryptographicOperations.ZeroMemory(rawPassword);
                    }
                }

                successMessage = GetTranslation("Passwords_Save_UpdateSuccess");
            }
            else
            {
                return;
            }

            IsEditorOpen = false;
            ResetEditorFields();
            await RefreshAsync();
            StatusMessage = successMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void CancelPasswordEditor()
    {
        IsEditorOpen = false;
        ResetEditorFields();
        StatusMessage = null;
    }

    private void ToggleEditorPasswordVisibility() => IsEditorPasswordVisible = !IsEditorPasswordVisible;

    private void ResetEditorFields()
    {
        EditorName = string.Empty;
        EditorDescription = string.Empty;
        EditorColor = "#FFFFD700";
        EditorPassword = string.Empty;
        IsEditorPasswordVisible = false;
    }

    private static IBrush ParseBrush(string color)
    {
        try
        {
            return Brush.Parse(string.IsNullOrWhiteSpace(color) ? "#FFFFD700" : color);
        }
        catch
        {
            return Brush.Parse("#FFFFD700");
        }
    }
}
