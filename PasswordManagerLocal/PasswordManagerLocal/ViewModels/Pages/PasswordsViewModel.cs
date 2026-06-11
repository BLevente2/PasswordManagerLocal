using Avalonia.Media;
using PasswordManagerLocal.Helpers;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class PasswordsViewModel : ViewModelBase
{
    private const string ListPane = "list";
    private const string EditorPane = "editor";
    private const string DetailsPane = "details";
    private const string ColorPane = "color";
    private const string CustomColorKey = "custom";

    private readonly IEndpoints _endpoints;
    private readonly List<PasswordItemViewModel> _allPasswords = [];

    private Guid _token;
    private PasswordItemViewModel? _selectedPassword;
    private PasswordItemViewModel? _passwordPendingDeletion;
    private string? _revealedPassword;
    private string? _statusMessage;
    private string _currentPane = ListPane;
    private bool _isCreateMode;
    private bool _isDeleteConfirmationOpen;
    private bool _isSavingPassword;
    private bool _isDeletingPassword;
    private string _editorName = string.Empty;
    private string _editorDescription = string.Empty;
    private string _editorColor = "#FFFFD700";
    private string _editorPassword = string.Empty;
    private bool _isEditorPasswordVisible;
    private bool _isEditorStoredPasswordRevealed;
    private string _customColorCode = "#FFFFD700";
    private double _customAlpha = 255;
    private double _customRed = 255;
    private double _customGreen = 215;
    private double _customBlue;
    private bool _isUpdatingColorFields;
    private string _searchQuery = string.Empty;
    private PasswordColorOptionViewModel? _selectedEditorColorOption;
    private PasswordSortOptionViewModel? _selectedSortOption;

    public PasswordsViewModel(UiPreferencesService uiPreferences, IEndpoints endpoints)
        : base(uiPreferences)
    {
        _endpoints = endpoints;

        Passwords = new ObservableCollection<PasswordItemViewModel>();
        PresetColors = new ObservableCollection<PasswordColorOptionViewModel>();
        SortOptions = new ObservableCollection<PasswordSortOptionViewModel>();

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        ExecutePrimaryActionCommand = ReactiveCommand.CreateFromTask(ExecutePrimaryActionAsync);
        SearchCommand = ReactiveCommand.Create(ApplyCurrentSearch);
        BeginCreatePasswordCommand = ReactiveCommand.Create(BeginCreatePassword);
        EditSelectedPasswordCommand = ReactiveCommand.CreateFromTask(EditSelectedPasswordAsync);
        BeginDeleteSelectedPasswordCommand = ReactiveCommand.Create(BeginDeleteSelectedPassword);
        ConfirmDeletePasswordCommand = ReactiveCommand.CreateFromTask(ConfirmDeletePasswordAsync);
        CancelDeletePasswordCommand = ReactiveCommand.Create(CancelDeletePassword);
        RevealPasswordCommand = ReactiveCommand.CreateFromTask(RevealPasswordAsync);
        HidePasswordCommand = ReactiveCommand.Create(HidePassword);
        CopyRevealedPasswordCommand = ReactiveCommand.CreateFromTask(CopyRevealedPasswordAsync);
        RevealEditorPasswordCommand = ReactiveCommand.CreateFromTask(RevealEditorPasswordAsync);
        SavePasswordCommand = ReactiveCommand.CreateFromTask(SavePasswordAsync);
        CancelPasswordEditorCommand = ReactiveCommand.Create(CancelPasswordEditor);
        ToggleEditorPasswordVisibilityCommand = ReactiveCommand.Create(ToggleEditorPasswordVisibility);
        OpenCustomColorPickerCommand = ReactiveCommand.Create(OpenCustomColorPicker);
        BackToPasswordEditorCommand = ReactiveCommand.Create(BackToPasswordEditor);
        ApplyManualColorCodeCommand = ReactiveCommand.Create(ApplyManualColorCode);
        BackToListCommand = ReactiveCommand.Create(BackToList);
        ClearSelectionCommand = ReactiveCommand.Create(BackToList);

        RebuildPresetColors();
        RebuildSortOptions();
        SelectDefaultPresetColor();
        SelectDefaultSortOption();
    }

    public ObservableCollection<PasswordItemViewModel> Passwords { get; }

    public ObservableCollection<PasswordColorOptionViewModel> PresetColors { get; }

    public ObservableCollection<PasswordSortOptionViewModel> SortOptions { get; }

    public PasswordItemViewModel? SelectedPassword
    {
        get => _selectedPassword;
        set
        {
            if (ReferenceEquals(_selectedPassword, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedPassword, value);
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(IsSelectionEmpty));
            this.RaisePropertyChanged(nameof(IsPasswordHidden));

            HidePassword();

            if (value is not null && CurrentPane == ListPane)
            {
                CurrentPane = DetailsPane;
            }
        }
    }

    public bool HasSelection => SelectedPassword is not null;

    public bool IsSelectionEmpty => !HasSelection;

    public PasswordItemViewModel? PasswordPendingDeletion
    {
        get => _passwordPendingDeletion;
        private set
        {
            this.RaiseAndSetIfChanged(ref _passwordPendingDeletion, value);
            this.RaisePropertyChanged(nameof(DeleteConfirmationMessage));
            this.RaisePropertyChanged(nameof(PasswordPendingDeletionName));
        }
    }

    public string PasswordPendingDeletionName => PasswordPendingDeletion?.Name ?? string.Empty;

    public string? RevealedPassword
    {
        get => _revealedPassword;
        private set
        {
            this.RaiseAndSetIfChanged(ref _revealedPassword, value);
            this.RaisePropertyChanged(nameof(HasRevealedPassword));
            this.RaisePropertyChanged(nameof(IsPasswordHidden));
        }
    }

    public bool HasRevealedPassword => !string.IsNullOrEmpty(RevealedPassword);

    public bool IsPasswordHidden => HasSelection && !HasRevealedPassword;

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

    public bool HasStoredPasswords => _allPasswords.Count > 0;

    public bool IsVaultEmpty => _allPasswords.Count == 0;

    public bool IsSearchResultEmpty => HasStoredPasswords && Passwords.Count == 0;

    public string CurrentPane
    {
        get => _currentPane;
        private set
        {
            if (string.Equals(_currentPane, value, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _currentPane, value);
            this.RaisePropertyChanged(nameof(IsListPaneVisible));
            this.RaisePropertyChanged(nameof(IsEditorPaneVisible));
            this.RaisePropertyChanged(nameof(IsDetailsPaneVisible));
            this.RaisePropertyChanged(nameof(IsColorPaneVisible));
            this.RaisePropertyChanged(nameof(IsEditorOpen));
            this.RaisePropertyChanged(nameof(IsEditorClosed));
        }
    }

    public bool IsListPaneVisible => CurrentPane == ListPane;

    public bool IsEditorPaneVisible => CurrentPane == EditorPane;

    public bool IsDetailsPaneVisible => CurrentPane == DetailsPane;

    public bool IsColorPaneVisible => CurrentPane == ColorPane;

    public bool IsEditorOpen => IsEditorPaneVisible;

    public bool IsEditorClosed => !IsEditorPaneVisible;

    public bool IsCreateMode
    {
        get => _isCreateMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isCreateMode, value);
            this.RaisePropertyChanged(nameof(IsEditMode));
            this.RaisePropertyChanged(nameof(EditorTitle));
            this.RaisePropertyChanged(nameof(SavePasswordButtonLabel));
            this.RaisePropertyChanged(nameof(EditorPasswordHint));
            this.RaisePropertyChanged(nameof(CanRevealEditorStoredPassword));
        }
    }

    public bool IsEditMode => !IsCreateMode;

    public bool IsDeleteConfirmationOpen
    {
        get => _isDeleteConfirmationOpen;
        private set => this.RaiseAndSetIfChanged(ref _isDeleteConfirmationOpen, value);
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
        private set
        {
            this.RaiseAndSetIfChanged(ref _editorColor, value);
            this.RaisePropertyChanged(nameof(EditorColorBrush));
            this.RaisePropertyChanged(nameof(EditorColorCode));
        }
    }

    public PasswordColorOptionViewModel? SelectedEditorColorOption
    {
        get => _selectedEditorColorOption;
        set
        {
            if (value?.Key == CustomColorKey)
            {
                this.RaisePropertyChanged(nameof(SelectedEditorColorOption));
                OpenCustomColorPicker();
                return;
            }

            if (ReferenceEquals(_selectedEditorColorOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedEditorColorOption, value);

            if (value is not null)
            {
                ApplyEditorColor(value.HexValue, updateSelectedPreset: false);
            }
        }
    }

    public IBrush EditorColorBrush => ParseBrush(EditorColor);

    public string EditorColorCode => EditorColor;

    public string CustomColorCode
    {
        get => _customColorCode;
        set
        {
            value ??= string.Empty;
            this.RaiseAndSetIfChanged(ref _customColorCode, value);
        }
    }

    public double CustomAlpha
    {
        get => _customAlpha;
        set
        {
            var normalized = NormalizeColorComponent(value);
            if (Math.Abs(_customAlpha - normalized) < 0.01)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _customAlpha, normalized);
            this.RaisePropertyChanged(nameof(CustomAlphaText));
            ApplyColorFromSliders();
        }
    }

    public double CustomRed
    {
        get => _customRed;
        set
        {
            var normalized = NormalizeColorComponent(value);
            if (Math.Abs(_customRed - normalized) < 0.01)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _customRed, normalized);
            this.RaisePropertyChanged(nameof(CustomRedText));
            ApplyColorFromSliders();
        }
    }

    public double CustomGreen
    {
        get => _customGreen;
        set
        {
            var normalized = NormalizeColorComponent(value);
            if (Math.Abs(_customGreen - normalized) < 0.01)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _customGreen, normalized);
            this.RaisePropertyChanged(nameof(CustomGreenText));
            ApplyColorFromSliders();
        }
    }

    public double CustomBlue
    {
        get => _customBlue;
        set
        {
            var normalized = NormalizeColorComponent(value);
            if (Math.Abs(_customBlue - normalized) < 0.01)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _customBlue, normalized);
            this.RaisePropertyChanged(nameof(CustomBlueText));
            ApplyColorFromSliders();
        }
    }

    public string CustomAlphaText => ToColorComponentByte(CustomAlpha).ToString();

    public string CustomRedText => ToColorComponentByte(CustomRed).ToString();

    public string CustomGreenText => ToColorComponentByte(CustomGreen).ToString();

    public string CustomBlueText => ToColorComponentByte(CustomBlue).ToString();

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

    public bool IsEditorStoredPasswordRevealed
    {
        get => _isEditorStoredPasswordRevealed;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isEditorStoredPasswordRevealed, value);
            this.RaisePropertyChanged(nameof(CanRevealEditorStoredPassword));
            this.RaisePropertyChanged(nameof(EditorPasswordHint));
        }
    }

    public bool CanRevealEditorStoredPassword => IsEditMode && !IsEditorStoredPasswordRevealed;

    public char EditorPasswordMaskCharacter => IsEditorPasswordVisible ? '\0' : '●';

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_searchQuery, value, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            ApplyFiltersAndSorting(SelectedPassword?.Id, preserveSelection: true);
        }
    }

    public PasswordSortOptionViewModel? SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (ReferenceEquals(_selectedSortOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedSortOption, value);
            ApplyFiltersAndSorting(SelectedPassword?.Id, preserveSelection: true);
        }
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> ExecutePrimaryActionCommand { get; }

    public ReactiveCommand<Unit, Unit> SearchCommand { get; }

    public ReactiveCommand<Unit, Unit> BeginCreatePasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> EditSelectedPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> BeginDeleteSelectedPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> ConfirmDeletePasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelDeletePasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> RevealPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> HidePasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> CopyRevealedPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> RevealEditorPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> SavePasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelPasswordEditorCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleEditorPasswordVisibilityCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenCustomColorPickerCommand { get; }

    public ReactiveCommand<Unit, Unit> BackToPasswordEditorCommand { get; }

    public ReactiveCommand<Unit, Unit> ApplyManualColorCodeCommand { get; }

    public ReactiveCommand<Unit, Unit> BackToListCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearSelectionCommand { get; }

    public string Title => GetTranslation("Passwords_Title");

    public string Subtitle => GetTranslation("Passwords_Subtitle");

    public string AddPasswordButtonLabel => GetTranslation("Passwords_Add");

    public string AddPasswordIconLabel => GetTranslation("Passwords_Add_Icon");

    public string RefreshButtonLabel => GetTranslation("Common_Refresh");

    public string EmptyStateTitle => GetTranslation("Passwords_Empty_Title");

    public string EmptyStateDescription => GetTranslation("Passwords_Empty_Description");

    public string EmptyStateAddLabel => GetTranslation("Passwords_Empty_Add");

    public string SearchEmptyTitle => GetTranslation("Passwords_SearchEmpty_Title");

    public string SearchEmptyDescription => GetTranslation("Passwords_SearchEmpty_Description");

    public string DetailsTitle => GetTranslation("Passwords_Details_Title");

    public string DetailsEmptyTitle => GetTranslation("Passwords_Details_Empty_Title");

    public string DetailsEmptyDescription => GetTranslation("Passwords_Details_Empty_Description");

    public string NameLabel => GetTranslation("Common_Name");

    public string PasswordLabel => GetTranslation("Common_Password");

    public string DescriptionLabel => GetTranslation("Common_Description");

    public string ColorLabel => GetTranslation("Common_Color");

    public string CurrentColorCodeLabel => GetTranslation("Passwords_ColorPicker_CurrentColor");

    public string MoreColorsLabel => GetTranslation("Passwords_Color_More");

    public string ColorPickerTitle => GetTranslation("Passwords_ColorPicker_Title");

    public string ColorPickerDescription => GetTranslation("Passwords_ColorPicker_Description");

    public string ColorPickerCodeLabel => GetTranslation("Passwords_ColorPicker_CodeLabel");

    public string ColorPickerCodePlaceholder => GetTranslation("Passwords_ColorPicker_CodePlaceholder");

    public string ApplyColorCodeLabel => GetTranslation("Passwords_ColorPicker_ApplyCode");

    public string BackToPasswordEditorLabel => GetTranslation("Passwords_ColorPicker_BackToEditor");

    public string AlphaLabel => GetTranslation("Common_Alpha");

    public string RedLabel => GetTranslation("Common_Red");

    public string GreenLabel => GetTranslation("Common_Green");

    public string BlueLabel => GetTranslation("Common_Blue");

    public string CreatedAtLabel => GetTranslation("Common_CreatedAt");

    public string UpdatedAtLabel => GetTranslation("Common_UpdatedAt");

    public string RevealPasswordLabel => GetTranslation("Passwords_Reveal");

    public string HidePasswordLabel => GetTranslation("Passwords_Hide");

    public string CopyPasswordLabel => GetTranslation("Passwords_Copy");

    public string RevealEditorPasswordLabel => GetTranslation("Passwords_Editor_RevealStoredPassword");

    public string EditPasswordLabel => GetTranslation("Common_Edit");

    public string DeletePasswordLabel => GetTranslation("Common_Delete");

    public string EditorTitle => GetTranslation(IsCreateMode ? "Passwords_Editor_CreateTitle" : "Passwords_Editor_EditTitle");

    public string SavePasswordButtonLabel => GetTranslation(IsCreateMode ? "Common_Add" : "Common_Save");

    public string CancelButtonLabel => GetTranslation("Common_Cancel");

    public string BackToListLabel => GetTranslation("Passwords_BackToList");

    public string EditorNamePlaceholder => GetTranslation("Passwords_Editor_NamePlaceholder");

    public string EditorDescriptionPlaceholder => GetTranslation("Passwords_Editor_DescriptionPlaceholder");

    public string EditorPasswordPlaceholder => GetTranslation("Passwords_Editor_PasswordPlaceholder");

    public string EditorPasswordHint => IsCreateMode
        ? GetTranslation("Passwords_Editor_PasswordHint_Create")
        : IsEditorStoredPasswordRevealed
            ? GetTranslation("Passwords_Editor_PasswordHint_Revealed")
            : GetTranslation("Passwords_Editor_PasswordHint_Edit");

    public string EditorPasswordVisibilityToggleText => GetTranslation(IsEditorPasswordVisible ? "Common_Hide" : "Common_Show");

    public string SearchLabel => GetTranslation("Common_Search");

    public string SearchPlaceholder => GetTranslation("Passwords_Search_Placeholder");

    public string SortLabel => GetTranslation("Passwords_Sort_Label");

    public string ClearSelectionLabel => GetTranslation("Common_ClearSelection");

    public string PasswordRevealHint => GetTranslation("Passwords_Reveal_Hint");

    public string DeleteConfirmationTitle => GetTranslation("Passwords_DeleteConfirm_Title");

    public string DeleteConfirmationMessage => string.Format(GetTranslation("Passwords_DeleteConfirm_Message"), PasswordPendingDeletionName);

    public string ConfirmDeletePasswordLabel => GetTranslation("Passwords_DeleteConfirm_Confirm");

    public string ListTabLabel => GetTranslation("Passwords_Tab_List");

    public string EditorTabLabel => GetTranslation("Passwords_Tab_Editor");

    public string DetailsTabLabel => GetTranslation("Passwords_Tab_Details");

    public string EditorClosedTitle => GetTranslation("Passwords_Editor_Closed_Title");

    public string EditorClosedDescription => GetTranslation("Passwords_Editor_Closed_Description");

    protected override void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(Subtitle));
        this.RaisePropertyChanged(nameof(AddPasswordButtonLabel));
        this.RaisePropertyChanged(nameof(AddPasswordIconLabel));
        this.RaisePropertyChanged(nameof(RefreshButtonLabel));
        this.RaisePropertyChanged(nameof(EmptyStateTitle));
        this.RaisePropertyChanged(nameof(EmptyStateDescription));
        this.RaisePropertyChanged(nameof(EmptyStateAddLabel));
        this.RaisePropertyChanged(nameof(SearchEmptyTitle));
        this.RaisePropertyChanged(nameof(SearchEmptyDescription));
        this.RaisePropertyChanged(nameof(DetailsTitle));
        this.RaisePropertyChanged(nameof(DetailsEmptyTitle));
        this.RaisePropertyChanged(nameof(DetailsEmptyDescription));
        this.RaisePropertyChanged(nameof(NameLabel));
        this.RaisePropertyChanged(nameof(PasswordLabel));
        this.RaisePropertyChanged(nameof(DescriptionLabel));
        this.RaisePropertyChanged(nameof(ColorLabel));
        this.RaisePropertyChanged(nameof(CurrentColorCodeLabel));
        this.RaisePropertyChanged(nameof(MoreColorsLabel));
        this.RaisePropertyChanged(nameof(ColorPickerTitle));
        this.RaisePropertyChanged(nameof(ColorPickerDescription));
        this.RaisePropertyChanged(nameof(ColorPickerCodeLabel));
        this.RaisePropertyChanged(nameof(ColorPickerCodePlaceholder));
        this.RaisePropertyChanged(nameof(ApplyColorCodeLabel));
        this.RaisePropertyChanged(nameof(BackToPasswordEditorLabel));
        this.RaisePropertyChanged(nameof(AlphaLabel));
        this.RaisePropertyChanged(nameof(RedLabel));
        this.RaisePropertyChanged(nameof(GreenLabel));
        this.RaisePropertyChanged(nameof(BlueLabel));
        this.RaisePropertyChanged(nameof(CreatedAtLabel));
        this.RaisePropertyChanged(nameof(UpdatedAtLabel));
        this.RaisePropertyChanged(nameof(RevealPasswordLabel));
        this.RaisePropertyChanged(nameof(HidePasswordLabel));
        this.RaisePropertyChanged(nameof(CopyPasswordLabel));
        this.RaisePropertyChanged(nameof(RevealEditorPasswordLabel));
        this.RaisePropertyChanged(nameof(EditPasswordLabel));
        this.RaisePropertyChanged(nameof(DeletePasswordLabel));
        this.RaisePropertyChanged(nameof(EditorTitle));
        this.RaisePropertyChanged(nameof(SavePasswordButtonLabel));
        this.RaisePropertyChanged(nameof(CancelButtonLabel));
        this.RaisePropertyChanged(nameof(BackToListLabel));
        this.RaisePropertyChanged(nameof(EditorNamePlaceholder));
        this.RaisePropertyChanged(nameof(EditorDescriptionPlaceholder));
        this.RaisePropertyChanged(nameof(EditorPasswordPlaceholder));
        this.RaisePropertyChanged(nameof(EditorPasswordHint));
        this.RaisePropertyChanged(nameof(EditorPasswordVisibilityToggleText));
        this.RaisePropertyChanged(nameof(SearchLabel));
        this.RaisePropertyChanged(nameof(SearchPlaceholder));
        this.RaisePropertyChanged(nameof(SortLabel));
        this.RaisePropertyChanged(nameof(ClearSelectionLabel));
        this.RaisePropertyChanged(nameof(PasswordRevealHint));
        this.RaisePropertyChanged(nameof(DeleteConfirmationTitle));
        this.RaisePropertyChanged(nameof(DeleteConfirmationMessage));
        this.RaisePropertyChanged(nameof(ConfirmDeletePasswordLabel));
        this.RaisePropertyChanged(nameof(ListTabLabel));
        this.RaisePropertyChanged(nameof(EditorTabLabel));
        this.RaisePropertyChanged(nameof(DetailsTabLabel));
        this.RaisePropertyChanged(nameof(EditorClosedTitle));
        this.RaisePropertyChanged(nameof(EditorClosedDescription));

        var currentEditorColor = EditorColor;
        var selectedSortKey = SelectedSortOption?.Key;

        RebuildPresetColors();
        RebuildSortOptions();
        ApplyEditorColor(currentEditorColor);

        SelectedSortOption = SortOptions.FirstOrDefault(item => item.Key == selectedSortKey)
            ?? SortOptions.FirstOrDefault();
    }

    public async Task LoadAsync(Guid token)
    {
        _token = token;
        await RefreshAsync();
    }

    public async Task RefreshCurrentDataAsync() => await RefreshAsync();

    public void Reset()
    {
        _token = Guid.Empty;
        _allPasswords.Clear();
        Passwords.Clear();
        RaisePasswordCollectionStateChanged();
        SelectedPassword = null;
        PasswordPendingDeletion = null;
        RevealedPassword = null;
        StatusMessage = null;
        IsCreateMode = true;
        IsDeleteConfirmationOpen = false;
        SearchQuery = string.Empty;
        SelectDefaultSortOption();
        CurrentPane = ListPane;
        ResetEditorFields();
    }


    private async Task ExecutePrimaryActionAsync()
    {
        if (IsDeleteConfirmationOpen)
        {
            await ConfirmDeletePasswordAsync();
            return;
        }

        if (IsEditorPaneVisible)
        {
            await SavePasswordAsync();
            return;
        }

        if (IsColorPaneVisible)
        {
            ApplyManualColorCode();
            return;
        }

        if (IsListPaneVisible)
        {
            ApplyCurrentSearch();
        }
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
            _allPasswords.Clear();

            foreach (var password in passwords)
            {
                _allPasswords.Add(PasswordItemViewModel.Create(password, BeginViewPasswordAsync, BeginEditPasswordAsync, BeginDeletePasswordAsync));
            }

            ApplyFiltersAndSorting(selectedId, preserveSelection: selectedId.HasValue);
            StatusMessage = GetTranslation("Passwords_Refreshed");
        }
        catch (Exception ex)
        {
            StatusMessage = GetSafeErrorMessage(ex);
        }
    }

    private void ApplyCurrentSearch() => ApplyFiltersAndSorting(SelectedPassword?.Id, preserveSelection: true);

    private Task BeginViewPasswordAsync(PasswordItemViewModel password)
    {
        SelectedPassword = password;
        HidePassword();
        StatusMessage = null;
        CurrentPane = DetailsPane;
        return Task.CompletedTask;
    }

    private void BeginCreatePassword()
    {
        IsCreateMode = true;
        StatusMessage = null;
        SelectedPassword = null;
        HidePassword();
        ResetEditorFields();
        CurrentPane = EditorPane;
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
        StatusMessage = null;
        EditorName = password.Name;
        EditorDescription = password.Description;
        EditorPassword = string.Empty;
        IsEditorPasswordVisible = false;
        IsEditorStoredPasswordRevealed = false;
        ApplyEditorColor(password.Color);
        CurrentPane = EditorPane;
        return Task.CompletedTask;
    }

    private void BeginDeleteSelectedPassword()
    {
        if (SelectedPassword is null)
        {
            return;
        }

        BeginDeletePassword(SelectedPassword);
    }

    private Task BeginDeletePasswordAsync(PasswordItemViewModel password)
    {
        BeginDeletePassword(password);
        return Task.CompletedTask;
    }

    private void BeginDeletePassword(PasswordItemViewModel password)
    {
        PasswordPendingDeletion = password;
        IsDeleteConfirmationOpen = true;
    }

    private void CancelDeletePassword()
    {
        IsDeleteConfirmationOpen = false;
        PasswordPendingDeletion = null;
    }

    private async Task ConfirmDeletePasswordAsync()
    {
        if (_isDeletingPassword || PasswordPendingDeletion is null)
        {
            return;
        }

        var password = PasswordPendingDeletion;

        try
        {
            _isDeletingPassword = true;
            await _endpoints.RemovePasswordAsync(_token, password.Id);
            CancelDeletePassword();

            if (SelectedPassword?.Id == password.Id)
            {
                SelectedPassword = null;
                HidePassword();
                CurrentPane = ListPane;
            }

            await RefreshAsync();
            StatusMessage = GetTranslation("Passwords_Delete_Success");
        }
        catch (Exception ex)
        {
            StatusMessage = GetSafeErrorMessage(ex);
        }
        finally
        {
            _isDeletingPassword = false;
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
            StatusMessage = GetSafeErrorMessage(ex);
        }
    }

    private void HidePassword()
    {
        RevealedPassword = null;
    }

    private async Task CopyRevealedPasswordAsync()
    {
        if (!HasRevealedPassword)
        {
            return;
        }

        try
        {
            StatusMessage = await TryCopyTextToClipboardAsync(RevealedPassword)
                ? GetTranslation("Passwords_Copy_Success")
                : GetTranslation("Error_ClipboardUnavailable");
        }
        catch
        {
            StatusMessage = GetTranslation("Error_ClipboardUnavailable");
        }
    }


    private async Task RevealEditorPasswordAsync()
    {
        if (SelectedPassword is null || IsCreateMode)
        {
            return;
        }

        try
        {
            var passwordBytes = await _endpoints.GetUnsecurePasswordAsync(_token, SelectedPassword.Id);
            try
            {
                EditorPassword = Encoding.UTF8.GetString(passwordBytes);
                IsEditorPasswordVisible = true;
                IsEditorStoredPasswordRevealed = true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = GetSafeErrorMessage(ex);
        }
    }

    private async Task SavePasswordAsync()
    {
        if (_isSavingPassword)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EditorName))
        {
            StatusMessage = GetTranslation("Validation_PasswordName_Required");
            return;
        }

        try
        {
            _isSavingPassword = true;
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
                        Color = EditorColor,
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
                        Color = EditorColor,
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

            HidePassword();
            ResetEditorFields();
            CurrentPane = ListPane;
            await RefreshAsync();
            StatusMessage = successMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = GetSafeErrorMessage(ex);
        }
        finally
        {
            _isSavingPassword = false;
        }
    }

    private void CancelPasswordEditor()
    {
        ResetEditorFields();
        HidePassword();
        StatusMessage = null;
        CurrentPane = ListPane;
    }

    private void ToggleEditorPasswordVisibility() => IsEditorPasswordVisible = !IsEditorPasswordVisible;

    private void BackToList()
    {
        SelectedPassword = null;
        HidePassword();
        ResetEditorFields();
        StatusMessage = null;
        CurrentPane = ListPane;
    }

    private void ResetEditorFields()
    {
        EditorName = string.Empty;
        EditorDescription = string.Empty;
        ApplyEditorColor("#FFFFD700");
        EditorPassword = string.Empty;
        IsEditorPasswordVisible = false;
        IsEditorStoredPasswordRevealed = false;
    }

    private void ApplyFiltersAndSorting(Guid? preferredSelectionId, bool preserveSelection)
    {
        IEnumerable<PasswordItemViewModel> query = _allPasswords;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var searchTerm = SearchQuery.Trim();
            query = query.Where(item =>
                item.Name.Contains(searchTerm, StringComparison.CurrentCultureIgnoreCase)
                || item.Description.Contains(searchTerm, StringComparison.CurrentCultureIgnoreCase));
        }

        query = SelectedSortOption?.Key switch
        {
            "name-desc" => query.OrderByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "created-desc" => query.OrderByDescending(item => item.CreatedAt),
            "created-asc" => query.OrderBy(item => item.CreatedAt),
            "updated-asc" => query.OrderBy(item => item.LastUpdatedAt),
            "updated-desc" => query.OrderByDescending(item => item.LastUpdatedAt),
            _ => query.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        var filtered = query.ToList();
        Passwords.Clear();
        foreach (var password in filtered)
        {
            Passwords.Add(password);
        }

        RaisePasswordCollectionStateChanged();

        if (preserveSelection && preferredSelectionId.HasValue)
        {
            SelectedPassword = filtered.FirstOrDefault(item => item.Id == preferredSelectionId.Value);
        }
        else
        {
            SelectedPassword = null;
        }
    }

    private void RaisePasswordCollectionStateChanged()
    {
        this.RaisePropertyChanged(nameof(HasPasswords));
        this.RaisePropertyChanged(nameof(IsEmpty));
        this.RaisePropertyChanged(nameof(HasStoredPasswords));
        this.RaisePropertyChanged(nameof(IsVaultEmpty));
        this.RaisePropertyChanged(nameof(IsSearchResultEmpty));
    }

    private void RebuildPresetColors()
    {
        PresetColors.Clear();
        PresetColors.Add(new PasswordColorOptionViewModel("gold", GetTranslation("Passwords_Color_Gold"), "#FFFFD700"));
        PresetColors.Add(new PasswordColorOptionViewModel("blue", GetTranslation("Passwords_Color_Blue"), "#FF3B82F6"));
        PresetColors.Add(new PasswordColorOptionViewModel("green", GetTranslation("Passwords_Color_Green"), "#FF22C55E"));
        PresetColors.Add(new PasswordColorOptionViewModel("red", GetTranslation("Passwords_Color_Red"), "#FFEF4444"));
        PresetColors.Add(new PasswordColorOptionViewModel("purple", GetTranslation("Passwords_Color_Purple"), "#FFA855F7"));
        PresetColors.Add(new PasswordColorOptionViewModel("orange", GetTranslation("Passwords_Color_Orange"), "#FFF97316"));
        PresetColors.Add(new PasswordColorOptionViewModel("teal", GetTranslation("Passwords_Color_Teal"), "#FF14B8A6"));
        PresetColors.Add(new PasswordColorOptionViewModel("gray", GetTranslation("Passwords_Color_Gray"), "#FF94A3B8"));
        PresetColors.Add(new PasswordColorOptionViewModel(CustomColorKey, GetTranslation("Passwords_Color_More"), EditorColor));
    }

    private void RebuildSortOptions()
    {
        SortOptions.Clear();
        SortOptions.Add(new PasswordSortOptionViewModel("name-asc", GetTranslation("Passwords_Sort_NameAsc")));
        SortOptions.Add(new PasswordSortOptionViewModel("name-desc", GetTranslation("Passwords_Sort_NameDesc")));
        SortOptions.Add(new PasswordSortOptionViewModel("created-desc", GetTranslation("Passwords_Sort_CreatedNewest")));
        SortOptions.Add(new PasswordSortOptionViewModel("created-asc", GetTranslation("Passwords_Sort_CreatedOldest")));
        SortOptions.Add(new PasswordSortOptionViewModel("updated-desc", GetTranslation("Passwords_Sort_UpdatedNewest")));
        SortOptions.Add(new PasswordSortOptionViewModel("updated-asc", GetTranslation("Passwords_Sort_UpdatedOldest")));
    }

    private void SelectDefaultPresetColor() => ApplyEditorColor("#FFFFD700");

    private void SelectDefaultSortOption() => SelectedSortOption = SortOptions.FirstOrDefault(item => item.Key == "name-asc") ?? SortOptions.FirstOrDefault();

    private void OpenCustomColorPicker()
    {
        SyncColorFieldsFromEditorColor();
        StatusMessage = null;
        CurrentPane = ColorPane;
    }

    private void BackToPasswordEditor()
    {
        StatusMessage = null;
        CurrentPane = EditorPane;
    }

    private void ApplyManualColorCode()
    {
        if (!TryNormalizeHexColor(CustomColorCode, out var normalizedColor))
        {
            StatusMessage = GetTranslation("Passwords_ColorPicker_InvalidCode");
            return;
        }

        ApplyEditorColor(normalizedColor);
        StatusMessage = null;
    }

    private void ApplyColorFromSliders()
    {
        if (_isUpdatingColorFields)
        {
            return;
        }

        var normalizedColor = $"#{ToColorComponentByte(CustomAlpha):X2}{ToColorComponentByte(CustomRed):X2}{ToColorComponentByte(CustomGreen):X2}{ToColorComponentByte(CustomBlue):X2}";
        ApplyEditorColor(normalizedColor, updateColorFields: false);
    }

    private void ApplyEditorColor(string color, bool updateSelectedPreset = true, bool updateColorFields = true)
    {
        if (!TryNormalizeHexColor(color, out var normalizedColor))
        {
            normalizedColor = "#FFFFD700";
        }

        EditorColor = normalizedColor;
        CustomColorCode = normalizedColor;

        if (updateColorFields)
        {
            SyncColorFieldsFromEditorColor();
        }

        if (updateSelectedPreset)
        {
            SelectMatchingPresetColor(normalizedColor);
        }
    }

    private void SelectMatchingPresetColor(string normalizedColor)
    {
        var match = PresetColors.FirstOrDefault(item => item.Key != CustomColorKey
            && string.Equals(NormalizeKnownColor(item.HexValue), normalizedColor, StringComparison.OrdinalIgnoreCase));

        if (ReferenceEquals(_selectedEditorColorOption, match))
        {
            return;
        }

        _selectedEditorColorOption = match;
        this.RaisePropertyChanged(nameof(SelectedEditorColorOption));
    }

    private void SyncColorFieldsFromEditorColor()
    {
        if (!TryNormalizeHexColor(EditorColor, out var normalizedColor))
        {
            normalizedColor = "#FFFFD700";
        }

        var alpha = Convert.ToByte(normalizedColor.Substring(1, 2), 16);
        var red = Convert.ToByte(normalizedColor.Substring(3, 2), 16);
        var green = Convert.ToByte(normalizedColor.Substring(5, 2), 16);
        var blue = Convert.ToByte(normalizedColor.Substring(7, 2), 16);

        _isUpdatingColorFields = true;
        try
        {
            SetColorField(ref _customAlpha, alpha, nameof(CustomAlpha), nameof(CustomAlphaText));
            SetColorField(ref _customRed, red, nameof(CustomRed), nameof(CustomRedText));
            SetColorField(ref _customGreen, green, nameof(CustomGreen), nameof(CustomGreenText));
            SetColorField(ref _customBlue, blue, nameof(CustomBlue), nameof(CustomBlueText));
            CustomColorCode = normalizedColor;
        }
        finally
        {
            _isUpdatingColorFields = false;
        }
    }

    private void SetColorField(ref double field, double value, string propertyName, string textPropertyName)
    {
        if (Math.Abs(field - value) < 0.01)
        {
            return;
        }

        field = value;
        this.RaisePropertyChanged(propertyName);
        this.RaisePropertyChanged(textPropertyName);
    }

    private static string NormalizeKnownColor(string color) => TryNormalizeHexColor(color, out var normalizedColor)
        ? normalizedColor
        : "#FFFFD700";

    private static bool TryNormalizeHexColor(string? input, out string normalizedColor)
    {
        normalizedColor = "#FFFFD700";

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var hex = input.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length == 3)
        {
            hex = $"FF{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        }
        else if (hex.Length == 4)
        {
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}";
        }
        else if (hex.Length == 6)
        {
            hex = $"FF{hex}";
        }
        else if (hex.Length != 8)
        {
            return false;
        }

        if (!hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        normalizedColor = $"#{hex.ToUpperInvariant()}";
        return true;
    }

    private static double NormalizeColorComponent(double value) => Math.Clamp(Math.Round(value), 0, 255);

    private static byte ToColorComponentByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

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
