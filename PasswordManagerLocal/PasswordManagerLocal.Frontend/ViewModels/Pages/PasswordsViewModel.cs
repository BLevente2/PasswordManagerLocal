using Avalonia.Media;
using PasswordManagerLocal.Frontend.Helpers;
using PasswordManagerLocal.Frontend.Security;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManagerLocal.Frontend.ViewModels.Pages;

public sealed class PasswordsViewModel : ViewModelBase
{
    private const string ListPane = "list";
    private const string EditorPane = "editor";
    private const string DetailsPane = "details";
    private const string CustomColorListPane = "custom-color-list";
    private const string ColorPane = "color";
    private const string CustomColorKey = "custom";
    private const string SelectedCustomColorKey = "selected-custom";

    private static readonly string[] LocalizedPropertyNames =
    [
        nameof(Title),
        nameof(Subtitle),
        nameof(AddPasswordButtonLabel),
        nameof(AddPasswordIconLabel),
        nameof(RefreshButtonLabel),
        nameof(EmptyStateTitle),
        nameof(EmptyStateDescription),
        nameof(EmptyStateAddLabel),
        nameof(SearchEmptyTitle),
        nameof(SearchEmptyDescription),
        nameof(DetailsTitle),
        nameof(DetailsEmptyTitle),
        nameof(DetailsEmptyDescription),
        nameof(NameLabel),
        nameof(PasswordLabel),
        nameof(DescriptionLabel),
        nameof(ColorLabel),
        nameof(CurrentColorCodeLabel),
        nameof(MoreColorsLabel),
        nameof(ColorPickerTitle),
        nameof(ColorPickerNameLabel),
        nameof(ColorPickerNamePlaceholder),
        nameof(AddColorButtonLabel),
        nameof(CustomColorSaveSuccessMessage),
        nameof(ColorPickerDescription),
        nameof(ColorPickerCodeLabel),
        nameof(ColorPickerCodePlaceholder),
        nameof(ApplyColorCodeLabel),
        nameof(BackToPasswordEditorLabel),
        nameof(AlphaLabel),
        nameof(RedLabel),
        nameof(GreenLabel),
        nameof(BlueLabel),
        nameof(CreatedAtLabel),
        nameof(UpdatedAtLabel),
        nameof(RevealPasswordLabel),
        nameof(HidePasswordLabel),
        nameof(CopyPasswordLabel),
        nameof(RevealEditorPasswordLabel),
        nameof(EditPasswordLabel),
        nameof(DeletePasswordLabel),
        nameof(EditorTitle),
        nameof(SavePasswordButtonLabel),
        nameof(CancelButtonLabel),
        nameof(BackToListLabel),
        nameof(EditorNamePlaceholder),
        nameof(EditorDescriptionPlaceholder),
        nameof(EditorPasswordPlaceholder),
        nameof(EditorPasswordHint),
        nameof(EditorPasswordVisibilityToggleText),
        nameof(SearchLabel),
        nameof(SearchPlaceholder),
        nameof(SearchModeLabel),
        nameof(SearchModeNameLabel),
        nameof(SearchModeDescriptionLabel),
        nameof(SearchModeTagLabel),
        nameof(CustomColorsTitle),
        nameof(CustomColorsBackToEditorLabel),
        nameof(CustomColorsEmptyTitle),
        nameof(CustomColorsEmptyDescription),
        nameof(CustomColorsEmptyAddLabel),
        nameof(CustomColorsSearchEmptyTitle),
        nameof(CustomColorsSearchEmptyDescription),
        nameof(CustomColorsSearchPlaceholder),
        nameof(CustomColorsSearchModeLabel),
        nameof(CustomColorsSearchModeNameLabel),
        nameof(CustomColorsSearchModeColorCodeLabel),
        nameof(CustomColorsSortLabel),
        nameof(CustomColorsSortNameAscMenuLabel),
        nameof(CustomColorsSortNameDescMenuLabel),
        nameof(AddCustomColorLabel),
        nameof(CustomColorDeleteConfirmationTitle),
        nameof(CustomColorDeleteConfirmationMessage),
        nameof(ConfirmDeleteCustomColorLabel),
        nameof(CustomColorDeleteSuccessMessage),
        nameof(BackFromColorPickerLabel),
        nameof(SwitchOnLabel),
        nameof(SwitchOffLabel),
        nameof(SortLabel),
        nameof(ClearSelectionLabel),
        nameof(PasswordRevealHint),
        nameof(DeleteConfirmationTitle),
        nameof(DeleteConfirmationMessage),
        nameof(ConfirmDeletePasswordLabel),
        nameof(ListTabLabel),
        nameof(EditorTabLabel),
        nameof(DetailsTabLabel),
        nameof(EditorClosedTitle),
        nameof(EditorClosedDescription),
        nameof(IsEditorPasswordVisibilityToggleVisible),
        nameof(PasswordStrengthLabel),
        nameof(PasswordStrengthInfoTitle),
        nameof(PasswordStrengthInfoBody),
        nameof(PasswordStrengthInfoAccessibleLabel),
        nameof(GeneratePasswordLabel),
    ];

    private readonly IEndpoints _endpoints;
    private readonly PasswordStrengthEstimator _passwordStrengthEstimator = new();
    private readonly MaximumStrengthPasswordGenerator _passwordGenerator;
    private readonly List<PasswordItemViewModel> _allPasswords = [];
    private readonly List<CustomUserColorInfoResponse> _savedCustomColors = [];
    private readonly List<CustomColorItemViewModel> _allCustomColors = [];

    private Guid _token;
    private PasswordItemViewModel? _selectedPassword;
    private PasswordItemViewModel? _passwordPendingDeletion;
    private CustomColorItemViewModel? _customColorPendingDeletion;
    private string? _revealedPassword;
    private string _currentPane = ListPane;
    private PasswordPaneTransitionViewModel? _currentAnimatedPaneViewModel;
    private bool _isPaneTransitionReversed;
    private bool _isCreateMode;
    private bool _isDeleteConfirmationOpen;
    private bool _isCustomColorDeleteConfirmationOpen;
    private bool _isSavingPassword;
    private bool _isDeletingPassword;
    private bool _isDeletingCustomColor;
    private bool _isSavingCustomColor;
    private Guid? _editingCustomColorId;
    private string _editorName = string.Empty;
    private string _editorDescription = string.Empty;
    private string _editorColor = PasswordColorUtility.DefaultColor;
    private string _editorPassword = string.Empty;
    private bool _isEditorPasswordVisible;
    private bool _isEditorStoredPasswordRevealed;
    private int _editorPasswordStrength;
    private int _revealedPasswordStrength;
    private string _customColorPickerName = string.Empty;
    private Color _customColorPickerColor = Color.FromRgb(20, 184, 166);
    private string _customColorPickerCode = PasswordColorUtility.DefaultColor;
    private bool _isUpdatingCustomColorPickerFields;
    private string _customColorCode = PasswordColorUtility.DefaultColor;
    private double _customAlpha = 255;
    private double _customRed = 20;
    private double _customGreen = 184;
    private double _customBlue = 166;
    private bool _isUpdatingColorFields;
    private string _searchQuery = string.Empty;
    private string _customColorSearchQuery = string.Empty;
    private bool _isPasswordSearchNameEnabled = true;
    private bool _isPasswordSearchDescriptionEnabled = true;
    private bool _isPasswordSearchTagEnabled = true;
    private bool _isCustomColorSearchNameEnabled = true;
    private bool _isCustomColorSearchCodeEnabled = true;
    private string _customColorSortKey = "name-asc";
    private PasswordColorOptionViewModel? _selectedEditorColorOption;
    private PasswordSortOptionViewModel? _selectedSortOption;
    private bool _isPasswordMultiSelectionActive;
    private bool _isCustomColorMultiSelectionActive;

    public PasswordsViewModel(UiPreferencesService uiPreferences, IEndpoints endpoints)
        : base(uiPreferences)
    {
        _endpoints = endpoints;
        _passwordGenerator = new MaximumStrengthPasswordGenerator(_passwordStrengthEstimator);

        Passwords = new ObservableCollection<PasswordItemViewModel>();
        CustomColors = new ObservableCollection<CustomColorItemViewModel>();
        PresetColors = new ObservableCollection<PasswordColorOptionViewModel>();
        SortOptions = new ObservableCollection<PasswordSortOptionViewModel>();

        RefreshCommand = ReactiveCommand.CreateFromTask(async () => { await RefreshAsync(true); });
        ExecutePrimaryActionCommand = ReactiveCommand.CreateFromTask(ExecutePrimaryActionAsync);
        SearchCommand = ReactiveCommand.Create(ApplyCurrentSearch);
        SelectSortOptionCommand = ReactiveCommand.Create<string>(SelectSortOptionByKey);
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
        GenerateEditorPasswordCommand = ReactiveCommand.Create(GenerateEditorPassword);
        OpenCustomColorPickerCommand = ReactiveCommand.Create(OpenCustomColorPicker);
        BackFromColorPickerCommand = ReactiveCommand.Create(BackFromColorPicker);
        SaveCustomColorCommand = ReactiveCommand.CreateFromTask(SaveCustomColorAsync);
        BackFromCustomColorListCommand = ReactiveCommand.Create(BackFromCustomColorList);
        SearchCustomColorsCommand = ReactiveCommand.Create(ApplyCustomColorFiltersAndSorting);
        SelectCustomColorSortOptionCommand = ReactiveCommand.Create<string>(SelectCustomColorSortOption);
        ConfirmDeleteCustomColorCommand = ReactiveCommand.CreateFromTask(ConfirmDeleteCustomColorAsync);
        CancelDeleteCustomColorCommand = ReactiveCommand.Create(CancelDeleteCustomColor);
        ApplyManualColorCodeCommand = ReactiveCommand.Create(ApplyManualColorCode);
        BackToListCommand = ReactiveCommand.Create(BackToList);
        ClearSelectionCommand = ReactiveCommand.Create(BackToList);

        RebuildPresetColors();
        RebuildSortOptions();
        SelectDefaultPresetColor();
        SelectDefaultSortOption();
    }

    public ObservableCollection<PasswordItemViewModel> Passwords { get; }

    public ObservableCollection<CustomColorItemViewModel> CustomColors { get; }

    public bool IsPasswordMultiSelectionActive
    {
        get => _isPasswordMultiSelectionActive;
        private set => this.RaiseAndSetIfChanged(ref _isPasswordMultiSelectionActive, value);
    }

    public bool IsCustomColorMultiSelectionActive
    {
        get => _isCustomColorMultiSelectionActive;
        private set => this.RaiseAndSetIfChanged(ref _isCustomColorMultiSelectionActive, value);
    }

    public event EventHandler? ListScrollToTopRequested;

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
            RefreshRevealedPasswordStrength();
        }
    }

    public bool HasRevealedPassword => !string.IsNullOrEmpty(RevealedPassword);

    public bool IsPasswordHidden => HasSelection && !HasRevealedPassword;

    public int RevealedPasswordStrength => _revealedPasswordStrength;

    public bool HasPasswords => Passwords.Count > 0;

    public bool IsEmpty => Passwords.Count == 0;

    public bool HasStoredPasswords => _allPasswords.Count > 0;

    public bool IsVaultEmpty => _allPasswords.Count == 0;

    public bool IsSearchResultEmpty => HasStoredPasswords && Passwords.Count == 0;

    public string CurrentPane
    {
        get => _currentPane;
        private set => SetCurrentPane(value, false);
    }

    public PasswordPaneTransitionViewModel CurrentAnimatedPaneViewModel
    {
        get => _currentAnimatedPaneViewModel ??= CreatePaneTransitionViewModel(CurrentPane);
        private set => this.RaiseAndSetIfChanged(ref _currentAnimatedPaneViewModel, value);
    }

    public bool IsPaneTransitionReversed
    {
        get => _isPaneTransitionReversed;
        private set => this.RaiseAndSetIfChanged(ref _isPaneTransitionReversed, value);
    }

    public bool IsAndroidPaneTransitionEnabled => OperatingSystem.IsAndroid();

    public bool IsStaticPaneContentVisible => !IsAndroidPaneTransitionEnabled;

    public bool IsListPaneVisible => CurrentPane == ListPane;

    public bool IsEditorPaneVisible => CurrentPane == EditorPane;

    public bool IsDetailsPaneVisible => CurrentPane == DetailsPane;

    public bool IsCustomColorListPaneVisible => CurrentPane == CustomColorListPane;

    public bool IsColorPaneVisible => CurrentPane == ColorPane;

    public bool IsEditorOpen => IsEditorPaneVisible;

    public bool IsEditorClosed => !IsEditorPaneVisible;

    private void SetCurrentPane(string value, bool isBackNavigation)
    {
        if (string.Equals(_currentPane, value, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(_currentPane, ListPane, StringComparison.Ordinal)
            && !string.Equals(value, ListPane, StringComparison.Ordinal))
        {
            ExitPasswordMultiSelection();
        }

        if (string.Equals(_currentPane, CustomColorListPane, StringComparison.Ordinal)
            && !string.Equals(value, CustomColorListPane, StringComparison.Ordinal))
        {
            ExitCustomColorMultiSelection();
        }

        IsPaneTransitionReversed = isBackNavigation;
        ClearStatusMessage();
        this.RaiseAndSetIfChanged(ref _currentPane, value);
        this.RaisePropertyChanged(nameof(IsListPaneVisible));
        this.RaisePropertyChanged(nameof(IsEditorPaneVisible));
        this.RaisePropertyChanged(nameof(IsDetailsPaneVisible));
        this.RaisePropertyChanged(nameof(IsCustomColorListPaneVisible));
        this.RaisePropertyChanged(nameof(IsColorPaneVisible));
        this.RaisePropertyChanged(nameof(IsEditorOpen));
        this.RaisePropertyChanged(nameof(IsEditorClosed));
        CurrentAnimatedPaneViewModel = CreatePaneTransitionViewModel(value);
    }

    private PasswordPaneTransitionViewModel CreatePaneTransitionViewModel(string pane) =>
        pane switch
        {
            EditorPane => new PasswordEditorPaneTransitionViewModel(this),
            DetailsPane => new PasswordDetailsPaneTransitionViewModel(this),
            CustomColorListPane => new PasswordCustomColorListPaneTransitionViewModel(this),
            ColorPane => new PasswordColorPaneTransitionViewModel(this),
            _ => new PasswordListPaneTransitionViewModel(this)
        };

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
            this.RaisePropertyChanged(nameof(IsEditorPasswordFieldVisible));
            this.RaisePropertyChanged(nameof(IsEditorPasswordVisibilityToggleVisible));
            this.RaisePropertyChanged(nameof(IsEditorPasswordStrengthVisible));
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
        set
        {
            this.RaiseAndSetIfChanged(ref _editorName, value);
            RefreshEditorPasswordStrength();
        }
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
            if (value?.IsManageColorsOption == true)
            {
                OpenCustomColorList();
                return;
            }

            if (ReferenceEquals(_selectedEditorColorOption, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedEditorColorOption, value);

            if (value is not null)
            {
                ApplyEditorColor(value.HexValue);
            }
        }
    }

    public IBrush EditorColorBrush => PasswordColorUtility.ParseBrush(EditorColor);

    public string EditorColorCode => EditorColor;

    public string CustomColorPickerName
    {
        get => _customColorPickerName;
        set => this.RaiseAndSetIfChanged(ref _customColorPickerName, value ?? string.Empty);
    }

    public Color CustomColorPickerColor
    {
        get => _customColorPickerColor;
        set
        {
            if (_customColorPickerColor == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _customColorPickerColor, value);
            SyncCustomColorPickerCodeFromColor();
        }
    }

    public string CustomColorPickerCode
    {
        get => _customColorPickerCode;
        set
        {
            value ??= string.Empty;
            if (_customColorPickerCode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _customColorPickerCode, value);

            if (_isUpdatingCustomColorPickerFields
                || !PasswordColorUtility.TryNormalizeHexColor(value, out var normalizedColor))
            {
                return;
            }

            SetCustomColorPickerColorFromNormalizedCode(normalizedColor);
            ClearStatusMessage();
        }
    }

    public int CustomColorNameMaxLength => DataLengthConstants.CustomUserColorNameMaxLength;

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
            var normalized = PasswordColorUtility.NormalizeComponent(value);
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
            var normalized = PasswordColorUtility.NormalizeComponent(value);
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
            var normalized = PasswordColorUtility.NormalizeComponent(value);
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
            var normalized = PasswordColorUtility.NormalizeComponent(value);
            if (Math.Abs(_customBlue - normalized) < 0.01)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _customBlue, normalized);
            this.RaisePropertyChanged(nameof(CustomBlueText));
            ApplyColorFromSliders();
        }
    }

    public string CustomAlphaText => PasswordColorUtility.ToComponentByte(CustomAlpha).ToString();

    public string CustomRedText => PasswordColorUtility.ToComponentByte(CustomRed).ToString();

    public string CustomGreenText => PasswordColorUtility.ToComponentByte(CustomGreen).ToString();

    public string CustomBlueText => PasswordColorUtility.ToComponentByte(CustomBlue).ToString();

    public string EditorPassword
    {
        get => _editorPassword;
        set
        {
            this.RaiseAndSetIfChanged(ref _editorPassword, value);
            RefreshEditorPasswordStrength();
        }
    }

    public int EditorPasswordStrength => _editorPasswordStrength;

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
            this.RaisePropertyChanged(nameof(IsEditorPasswordFieldVisible));
            this.RaisePropertyChanged(nameof(IsEditorPasswordVisibilityToggleVisible));
            this.RaisePropertyChanged(nameof(IsEditorPasswordStrengthVisible));
        }
    }

    public bool CanRevealEditorStoredPassword => IsEditMode && !IsEditorStoredPasswordRevealed;

    public bool IsEditorPasswordFieldVisible => IsCreateMode || IsEditorStoredPasswordRevealed;

    public bool IsEditorPasswordVisibilityToggleVisible => IsEditorPasswordFieldVisible;

    public bool IsEditorPasswordStrengthVisible => IsCreateMode || IsEditorStoredPasswordRevealed;

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

    public bool IsPasswordSearchNameEnabled
    {
        get => _isPasswordSearchNameEnabled;
        set => SetPasswordSearchMode(ref _isPasswordSearchNameEnabled, value, nameof(IsPasswordSearchNameEnabled));
    }

    public bool IsPasswordSearchDescriptionEnabled
    {
        get => _isPasswordSearchDescriptionEnabled;
        set => SetPasswordSearchMode(ref _isPasswordSearchDescriptionEnabled, value, nameof(IsPasswordSearchDescriptionEnabled));
    }

    public bool IsPasswordSearchTagEnabled
    {
        get => _isPasswordSearchTagEnabled;
        set => SetPasswordSearchMode(ref _isPasswordSearchTagEnabled, value, nameof(IsPasswordSearchTagEnabled));
    }

    public bool CanTogglePasswordSearchName => CanTogglePasswordSearchMode(_isPasswordSearchNameEnabled);

    public bool CanTogglePasswordSearchDescription => CanTogglePasswordSearchMode(_isPasswordSearchDescriptionEnabled);

    public bool CanTogglePasswordSearchTag => CanTogglePasswordSearchMode(_isPasswordSearchTagEnabled);

    public string CustomColorSearchQuery
    {
        get => _customColorSearchQuery;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_customColorSearchQuery, value, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _customColorSearchQuery, value);
            ApplyCustomColorFiltersAndSorting();
        }
    }

    public bool IsCustomColorSearchNameEnabled
    {
        get => _isCustomColorSearchNameEnabled;
        set => SetCustomColorSearchMode(ref _isCustomColorSearchNameEnabled, value, nameof(IsCustomColorSearchNameEnabled));
    }

    public bool IsCustomColorSearchCodeEnabled
    {
        get => _isCustomColorSearchCodeEnabled;
        set => SetCustomColorSearchMode(ref _isCustomColorSearchCodeEnabled, value, nameof(IsCustomColorSearchCodeEnabled));
    }

    public bool CanToggleCustomColorSearchName => CanToggleCustomColorSearchMode(_isCustomColorSearchNameEnabled);

    public bool CanToggleCustomColorSearchCode => CanToggleCustomColorSearchMode(_isCustomColorSearchCodeEnabled);

    public bool HasCustomColors => CustomColors.Count > 0;

    public bool HasStoredCustomColors => _allCustomColors.Count > 0;

    public bool IsCustomColorListEmpty => _allCustomColors.Count == 0;

    public bool IsCustomColorSearchResultEmpty => HasStoredCustomColors && CustomColors.Count == 0;

    public CustomColorItemViewModel? CustomColorPendingDeletion
    {
        get => _customColorPendingDeletion;
        private set
        {
            this.RaiseAndSetIfChanged(ref _customColorPendingDeletion, value);
            this.RaisePropertyChanged(nameof(CustomColorDeleteConfirmationMessage));
        }
    }

    public bool IsCustomColorDeleteConfirmationOpen
    {
        get => _isCustomColorDeleteConfirmationOpen;
        private set => this.RaiseAndSetIfChanged(ref _isCustomColorDeleteConfirmationOpen, value);
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
            UpdateSortOptionSelectionMarks();
            RaiseSortMenuLabelProperties();
            ApplyFiltersAndSorting(SelectedPassword?.Id, preserveSelection: true);
        }
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> ExecutePrimaryActionCommand { get; }

    public ReactiveCommand<Unit, Unit> SearchCommand { get; }

    public ReactiveCommand<string, Unit> SelectSortOptionCommand { get; }

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

    public ReactiveCommand<Unit, Unit> GenerateEditorPasswordCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenCustomColorPickerCommand { get; }

    public ReactiveCommand<Unit, Unit> BackFromColorPickerCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveCustomColorCommand { get; }

    public ReactiveCommand<Unit, Unit> BackFromCustomColorListCommand { get; }

    public ReactiveCommand<Unit, Unit> SearchCustomColorsCommand { get; }

    public ReactiveCommand<string, Unit> SelectCustomColorSortOptionCommand { get; }

    public ReactiveCommand<Unit, Unit> ConfirmDeleteCustomColorCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelDeleteCustomColorCommand { get; }

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

    public string ColorPickerTitle => GetTranslation(
        IsEditingCustomColor ? "Passwords_ColorPicker_EditTitle" : "Passwords_ColorPicker_Title");

    public string ColorPickerNameLabel => GetTranslation("Passwords_ColorPicker_NameLabel");

    public string ColorPickerNamePlaceholder => GetTranslation("Passwords_ColorPicker_NamePlaceholder");

    public string AddColorButtonLabel => GetTranslation(
        IsEditingCustomColor ? "Passwords_ColorPicker_Save" : "Passwords_ColorPicker_Add");

    public string CustomColorSaveSuccessMessage => GetTranslation(
        IsEditingCustomColor ? "Passwords_CustomColors_Update_Success" : "Passwords_CustomColors_Add_Success");

    public bool IsEditingCustomColor => _editingCustomColorId.HasValue;

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

    public string EditorPasswordHint => GetTranslation("Passwords_Editor_PasswordHint_Create");

    public string EditorPasswordVisibilityToggleText => GetTranslation(IsEditorPasswordVisible ? "Common_Hide" : "Common_Show");

    public string PasswordStrengthLabel => GetTranslation("PasswordStrength_Label");

    public string PasswordStrengthInfoTitle => GetTranslation("PasswordStrength_Info_Title");

    public string PasswordStrengthInfoBody => GetTranslation("PasswordStrength_Info_Body");

    public string PasswordStrengthInfoAccessibleLabel => GetTranslation("PasswordStrength_Info_AccessibleLabel");

    public string GeneratePasswordLabel => GetTranslation("Passwords_Editor_GeneratePassword");

    public string SearchLabel => GetTranslation("Common_Search");

    public string SearchPlaceholder => GetTranslation("Passwords_Search_Placeholder");

    public string SearchModeLabel => GetTranslation("Passwords_SearchMode_Label");

    public string SearchModeNameLabel => GetTranslation("Common_Name");

    public string SearchModeDescriptionLabel => GetTranslation("Common_Description");

    public string SearchModeTagLabel => GetTranslation("Passwords_SearchMode_Tag");

    public string CustomColorsTitle => GetTranslation("Passwords_CustomColors_Title");

    public string CustomColorsBackToEditorLabel => GetTranslation("Passwords_CustomColors_BackToEditor");

    public string CustomColorsEmptyTitle => GetTranslation("Passwords_CustomColors_Empty_Title");

    public string CustomColorsEmptyDescription => GetTranslation("Passwords_CustomColors_Empty_Description");

    public string CustomColorsEmptyAddLabel => GetTranslation("Passwords_CustomColors_Empty_Add");

    public string CustomColorsSearchEmptyTitle => GetTranslation("Passwords_CustomColors_SearchEmpty_Title");

    public string CustomColorsSearchEmptyDescription => GetTranslation("Passwords_CustomColors_SearchEmpty_Description");

    public string CustomColorsSearchPlaceholder => GetTranslation("Passwords_CustomColors_Search_Placeholder");

    public string CustomColorsSearchModeLabel => GetTranslation("Passwords_CustomColors_SearchMode_Label");

    public string CustomColorsSearchModeNameLabel => GetTranslation("Common_Name");

    public string CustomColorsSearchModeColorCodeLabel => GetTranslation("Passwords_CustomColors_SearchMode_ColorCode");

    public string CustomColorsSortLabel => GetTranslation("Passwords_CustomColors_Sort_Label");

    public string CustomColorsSortNameAscMenuLabel => BuildCustomColorSortMenuLabel("name-asc", "Passwords_Sort_NameAsc");

    public string CustomColorsSortNameDescMenuLabel => BuildCustomColorSortMenuLabel("name-desc", "Passwords_Sort_NameDesc");

    public string AddCustomColorLabel => GetTranslation("Passwords_CustomColors_Add");

    public string CustomColorDeleteConfirmationTitle => GetTranslation("Passwords_CustomColors_DeleteConfirm_Title");

    public string CustomColorDeleteConfirmationMessage => string.Format(
        GetTranslation("Passwords_CustomColors_DeleteConfirm_Message"),
        CustomColorPendingDeletion?.DisplayName ?? string.Empty);

    public string ConfirmDeleteCustomColorLabel => GetTranslation("Passwords_CustomColors_DeleteConfirm_Confirm");

    public string CustomColorDeleteSuccessMessage => GetTranslation("Passwords_CustomColors_Delete_Success");

    public string BackFromColorPickerLabel => GetTranslation("Passwords_CustomColors_BackFromPicker");

    public string SwitchOnLabel => GetTranslation("Common_On");

    public string SwitchOffLabel => GetTranslation("Common_Off");

    public string SortLabel => GetTranslation("Passwords_Sort_Label");

    public string SortNameAscMenuLabel => BuildSortMenuLabel("name-asc", "Passwords_Sort_NameAsc");

    public string SortNameDescMenuLabel => BuildSortMenuLabel("name-desc", "Passwords_Sort_NameDesc");

    public string SortCreatedNewestMenuLabel => BuildSortMenuLabel("created-desc", "Passwords_Sort_CreatedNewest");

    public string SortCreatedOldestMenuLabel => BuildSortMenuLabel("created-asc", "Passwords_Sort_CreatedOldest");

    public string SortUpdatedNewestMenuLabel => BuildSortMenuLabel("updated-desc", "Passwords_Sort_UpdatedNewest");

    public string SortUpdatedOldestMenuLabel => BuildSortMenuLabel("updated-asc", "Passwords_Sort_UpdatedOldest");

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
        RaisePropertiesChanged(LocalizedPropertyNames);

        foreach (var password in _allPasswords)
        {
            password.ApplyActionLabels(EditPasswordLabel, DeletePasswordLabel);

            if (string.Equals(
                PasswordColorUtility.NormalizeKnownColor(password.Color),
                PasswordColorUtility.DefaultColor,
                StringComparison.OrdinalIgnoreCase))
            {
                password.ApplyColorName(GetTranslation("Passwords_Color_Teal"));
            }
        }

        foreach (var customColor in _allCustomColors)
            customColor.ApplyDeleteLabel(DeletePasswordLabel);

        var currentEditorColor = EditorColor;
        var selectedSortKey = SelectedSortOption?.Key;
        RebuildPresetColors();
        RebuildSortOptions();
        ApplyEditorColor(currentEditorColor);
        SelectedSortOption = SortOptions.FirstOrDefault(item => item.Key == selectedSortKey)
            ?? SortOptions.FirstOrDefault();
        UpdateSortOptionSelectionMarks();
        RaiseSortMenuLabelProperties();
        RaiseCustomColorSortMenuLabelProperties();
        ApplyFiltersAndSorting(SelectedPassword?.Id, preserveSelection: true);
        ApplyCustomColorFiltersAndSorting();
    }

    public async Task<bool> LoadAsync(Guid token)
    {
        _token = token;
        return await RefreshAsync(false);
    }

    public void SetSessionToken(Guid token) => _token = token;

    public async Task<bool> RefreshCurrentDataAsync(bool showSuccessMessage = true) => await RefreshAsync(showSuccessMessage);

    public void RequestListScrollToTop() => ListScrollToTopRequested?.Invoke(this, EventArgs.Empty);

    public void ShowMainPage()
    {
        IsDeleteConfirmationOpen = false;
        PasswordPendingDeletion = null;
        IsCustomColorDeleteConfirmationOpen = false;
        CustomColorPendingDeletion = null;
        SelectedPassword = null;
        RevealedPassword = null;
        ClearStatusMessage();
        SearchQuery = string.Empty;
        IsCreateMode = true;
        SetCustomColorPickerMode(null);
        ResetCustomColorPickerDraft();
        CurrentPane = ListPane;
        ResetEditorFields();
    }

    public void Reset()
    {
        _token = Guid.Empty;
        ExitPasswordMultiSelection();
        ExitCustomColorMultiSelection();
        _allPasswords.Clear();
        _savedCustomColors.Clear();
        _allCustomColors.Clear();
        Passwords.Clear();
        CustomColors.Clear();
        RebuildPresetColors();
        RaisePasswordCollectionStateChanged();
        RaiseCustomColorCollectionStateChanged();
        SelectedPassword = null;
        PasswordPendingDeletion = null;
        CustomColorPendingDeletion = null;
        RevealedPassword = null;
        ClearStatusMessage();
        IsCreateMode = true;
        IsDeleteConfirmationOpen = false;
        IsCustomColorDeleteConfirmationOpen = false;
        SearchQuery = string.Empty;
        CustomColorSearchQuery = string.Empty;
        _customColorSortKey = "name-asc";
        RaiseCustomColorSortMenuLabelProperties();
        SelectDefaultSortOption();
        SetCustomColorPickerMode(null);
        ResetCustomColorPickerDraft();
        CurrentPane = ListPane;
        ResetEditorFields();
    }


    public void HideVisibleSensitiveData()
    {
        if (HasRevealedPassword)
        {
            HidePassword();
        }

        if (IsEditorPasswordVisible)
        {
            IsEditorPasswordVisible = false;
        }
    }


    public void BeginPasswordMultiSelection(PasswordItemViewModel password)
    {
        if (!IsListPaneVisible || !_allPasswords.Contains(password))
        {
            return;
        }

        if (!IsPasswordMultiSelectionActive)
        {
            IsPasswordMultiSelectionActive = true;
            SetSelectionMode(_allPasswords, true);
        }

        password.IsSelected = true;
    }

    public void BeginCustomColorMultiSelection(CustomColorItemViewModel customColor)
    {
        if (!IsCustomColorListPaneVisible || !_allCustomColors.Contains(customColor))
        {
            return;
        }

        if (!IsCustomColorMultiSelectionActive)
        {
            IsCustomColorMultiSelectionActive = true;
            SetSelectionMode(_allCustomColors, true);
        }

        customColor.IsSelected = true;
    }

    private void ExitPasswordMultiSelection()
    {
        IsPasswordMultiSelectionActive = false;
        SetSelectionMode(_allPasswords, false);
    }

    private void ExitCustomColorMultiSelection()
    {
        IsCustomColorMultiSelectionActive = false;
        SetSelectionMode(_allCustomColors, false);
    }

    private static void SetSelectionMode<TItem>(IEnumerable<TItem> items, bool isActive)
        where TItem : MultiSelectableListItemViewModel
    {
        foreach (var item in items)
        {
            item.SetSelectionModeActive(isActive);
        }
    }

    public bool TryExitMultiSelection()
    {
        if (IsCustomColorMultiSelectionActive)
        {
            ExitCustomColorMultiSelection();
            return true;
        }

        if (IsPasswordMultiSelectionActive)
        {
            ExitPasswordMultiSelection();
            return true;
        }

        return false;
    }


    public bool TryNavigateBack()
    {
        if (IsCustomColorDeleteConfirmationOpen)
        {
            CancelDeleteCustomColor();
            return true;
        }

        if (IsDeleteConfirmationOpen)
        {
            CancelDeletePassword();
            return true;
        }

        if (TryExitMultiSelection())
            return true;

        if (IsColorPaneVisible)
        {
            BackFromColorPicker();
            return true;
        }

        if (IsCustomColorListPaneVisible)
        {
            BackFromCustomColorList();
            return true;
        }

        if (IsEditorPaneVisible)
        {
            CancelPasswordEditor();
            return true;
        }

        if (IsDetailsPaneVisible)
        {
            BackToList();
            return true;
        }

        return false;
    }


    private async Task ExecutePrimaryActionAsync()
    {
        if (IsCustomColorDeleteConfirmationOpen)
        {
            await ConfirmDeleteCustomColorAsync();
            return;
        }

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
            await SaveCustomColorAsync();
            return;
        }

        if (IsCustomColorListPaneVisible)
        {
            ApplyCustomColorFiltersAndSorting();
            return;
        }

        if (IsListPaneVisible)
        {
            ApplyCurrentSearch();
        }
    }


    private async Task<bool> RefreshAsync(bool showSuccessMessage)
    {
        if (_token == Guid.Empty)
        {
            return false;
        }

        ClearStatusMessage();
        ExitPasswordMultiSelection();
        ExitCustomColorMultiSelection();
        var selectedId = SelectedPassword?.Id;

        try
        {
            var response = await _endpoints.GetSavedPasswordsAsync(_token);
            var tagNameById = response.Tags.ToDictionary(tag => tag.Id, tag => tag.Name);
            var currentEditorColor = EditorColor;

            _savedCustomColors.Clear();
            _savedCustomColors.AddRange(response.CustomColors);
            RebuildPresetColors();
            RebuildCustomColorItems();
            ApplyEditorColor(currentEditorColor);

            var colorNameByCode = BuildColorNameByCodeLookup(response.CustomColors);

            _allPasswords.Clear();

            foreach (var password in response.Passwords)
            {
                var tagNames = password.TagIds
                    .Select(tagId => tagNameById.TryGetValue(tagId, out var tagName) ? tagName : null)
                    .Where(tagName => !string.IsNullOrWhiteSpace(tagName))
                    .Select(tagName => tagName!)
                    .ToList();

                var normalizedPasswordColor = PasswordColorUtility.NormalizeKnownColor(password.Color);
                colorNameByCode.TryGetValue(normalizedPasswordColor, out var colorName);

                _allPasswords.Add(PasswordItemViewModel.Create(
                    password,
                    tagNames,
                    colorName,
                    EditPasswordLabel,
                    DeletePasswordLabel,
                    BeginViewPasswordAsync,
                    BeginEditPasswordAsync,
                    BeginDeletePasswordAsync));
            }

            ApplyFiltersAndSorting(selectedId, preserveSelection: selectedId.HasValue);
            if (showSuccessMessage)
                ShowSuccessMessage(GetTranslation("Passwords_Refreshed"));

            return true;
        }
        catch (Exception ex)
        {
            ShowErrorMessage(GetSafeErrorMessage(ex));
            return false;
        }
    }

    private Dictionary<string, string> BuildColorNameByCodeLookup(
        IEnumerable<CustomUserColorInfoResponse> customColors)
    {
        var namesByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var customColor in customColors)
        {
            if (string.IsNullOrWhiteSpace(customColor.ColorName))
            {
                continue;
            }

            var normalizedColorCode = PasswordColorUtility.NormalizeKnownColor(customColor.ColorCode);
            namesByCode[normalizedColorCode] = customColor.ColorName.Trim();
        }

        namesByCode[PasswordColorUtility.DefaultColor] = GetTranslation("Passwords_Color_Teal");

        return namesByCode;
    }

    private void ApplyCurrentSearch() => ApplyFiltersAndSorting(SelectedPassword?.Id, preserveSelection: true);

    private Task BeginViewPasswordAsync(PasswordItemViewModel password)
    {
        SelectedPassword = password;
        HidePassword();
        ClearStatusMessage();
        CurrentPane = DetailsPane;
        return Task.CompletedTask;
    }

    private void BeginCreatePassword()
    {
        IsCreateMode = true;
        ClearStatusMessage();
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
        ClearStatusMessage();
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
        ClearStatusMessage();
        PasswordPendingDeletion = password;
        IsDeleteConfirmationOpen = true;
    }

    private void CancelDeletePassword()
    {
        ClearStatusMessage();
        IsDeleteConfirmationOpen = false;
        PasswordPendingDeletion = null;
    }

    private async Task ConfirmDeletePasswordAsync()
    {
        if (_isDeletingPassword || PasswordPendingDeletion is null)
        {
            return;
        }

        ClearStatusMessage();
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

            if (!await RefreshAsync(false))
                return;

            ShowSuccessMessage(GetTranslation("Passwords_Delete_Success"));
        }
        catch (Exception ex)
        {
            ShowErrorMessage(GetSafeErrorMessage(ex));
        }
        finally
        {
            _isDeletingPassword = false;
        }
    }

    private async Task RevealPasswordAsync()
    {
        if (SelectedPassword is null || HasRevealedPassword)
        {
            return;
        }

        ClearStatusMessage();

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
            ShowErrorMessage(GetSafeErrorMessage(ex));
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

        ClearStatusMessage();

        try
        {
            if (await TryCopyTextToClipboardAsync(RevealedPassword))
                ShowSuccessMessage(GetTranslation("Passwords_Copy_Success"));
            else
                ShowErrorMessage(GetTranslation("Error_ClipboardUnavailable"));
        }
        catch
        {
            ShowErrorMessage(GetTranslation("Error_ClipboardUnavailable"));
        }
    }


    private async Task RevealEditorPasswordAsync()
    {
        if (SelectedPassword is null || IsCreateMode)
        {
            return;
        }

        ClearStatusMessage();

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
            ShowErrorMessage(GetSafeErrorMessage(ex));
        }
    }

    private async Task SavePasswordAsync()
    {
        if (_isSavingPassword || !ValidatePasswordEditor())
            return;

        try
        {
            _isSavingPassword = true;
            var successMessage = await PersistPasswordEditorAsync();
            if (successMessage is null)
                return;

            HidePassword();
            ResetEditorFields();
            CurrentPane = ListPane;
            if (await RefreshAsync(false))
                ShowSuccessMessage(successMessage);
        }
        catch (Exception ex)
        {
            ShowErrorMessage(GetSafeErrorMessage(ex));
        }
        finally
        {
            _isSavingPassword = false;
        }
    }

    private bool ValidatePasswordEditor()
    {
        ClearStatusMessage();
        if (string.IsNullOrWhiteSpace(EditorName))
        {
            ShowErrorMessage(GetTranslation("Validation_PasswordName_Required"));
            return false;
        }

        if (IsCreateMode && string.IsNullOrWhiteSpace(EditorPassword))
        {
            ShowErrorMessage(GetTranslation("Validation_RegisterPassword_Required"));
            return false;
        }

        return true;
    }

    private async Task<string?> PersistPasswordEditorAsync()
    {
        if (IsCreateMode)
        {
            await CreatePasswordFromEditorAsync();
            return GetTranslation("Passwords_Save_CreateSuccess");
        }

        if (SelectedPassword is null)
            return null;

        await UpdatePasswordFromEditorAsync(SelectedPassword.Id);
        return GetTranslation("Passwords_Save_UpdateSuccess");
    }

    private async Task CreatePasswordFromEditorAsync()
    {
        var rawPassword = SecretTransform.Utf8Bytes(EditorPassword);
        try
        {
            await _endpoints.AddNewPasswordAsync(_token, new NewPasswordRequest
            {
                Name = EditorName.Trim(),
                Description = EditorDescription.Trim(),
                Color = EditorColor,
                Password = rawPassword,
                TagIds = []
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawPassword);
        }
    }

    private async Task UpdatePasswordFromEditorAsync(Guid passwordId)
    {
        byte[]? rawPassword = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(EditorPassword))
                rawPassword = SecretTransform.Utf8Bytes(EditorPassword);

            await _endpoints.UpdatePasswordAsync(_token, new UpdatePasswordRequest
            {
                Id = passwordId,
                Name = EditorName.Trim(),
                Description = EditorDescription.Trim(),
                Color = EditorColor,
                Password = rawPassword,
                TagIds = []
            });
        }
        finally
        {
            if (rawPassword is not null)
                CryptographicOperations.ZeroMemory(rawPassword);
        }
    }

    private void CancelPasswordEditor()
    {
        ResetEditorFields();
        HidePassword();
        ClearStatusMessage();
        SetCurrentPane(ListPane, true);
    }

    private void RefreshEditorPasswordStrength()
    {
        var result = _passwordStrengthEstimator.Evaluate(
            EditorPassword,
            [EditorName]);

        if (_editorPasswordStrength == result.Score)
            return;

        _editorPasswordStrength = result.Score;
        this.RaisePropertyChanged(nameof(EditorPasswordStrength));
    }

    private void RefreshRevealedPasswordStrength()
    {
        var result = _passwordStrengthEstimator.Evaluate(
            RevealedPassword,
            [SelectedPassword?.Name]);

        if (_revealedPasswordStrength == result.Score)
            return;

        _revealedPasswordStrength = result.Score;
        this.RaisePropertyChanged(nameof(RevealedPasswordStrength));
    }

    private void GenerateEditorPassword()
    {
        EditorPassword = _passwordGenerator.Generate([EditorName]);
        IsEditorPasswordVisible = true;
    }

    private void ToggleEditorPasswordVisibility() => IsEditorPasswordVisible = !IsEditorPasswordVisible;

    private void BackToList()
    {
        HidePassword();
        ResetEditorFields();
        ClearStatusMessage();
        SetCurrentPane(ListPane, true);
    }

    private void ResetEditorFields()
    {
        EditorName = string.Empty;
        EditorDescription = string.Empty;
        ApplyEditorColor(PasswordColorUtility.DefaultColor);
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
                IsPasswordSearchNameEnabled && item.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                || IsPasswordSearchDescriptionEnabled && item.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                || IsPasswordSearchTagEnabled && item.HasTagMatching(searchTerm));
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

    private void RebuildCustomColorItems()
    {
        _allCustomColors.Clear();

        foreach (var customColor in _savedCustomColors)
        {
            var normalizedColor = PasswordColorUtility.NormalizeKnownColor(customColor.ColorCode);
            if (string.Equals(normalizedColor, PasswordColorUtility.DefaultColor, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _allCustomColors.Add(CustomColorItemViewModel.Create(
                customColor,
                DeletePasswordLabel,
                OpenCustomColorPickerForEditing,
                BeginDeleteCustomColorAsync));
        }

        ApplyCustomColorFiltersAndSorting();
    }

    private void ApplyCustomColorFiltersAndSorting()
    {
        IEnumerable<CustomColorItemViewModel> query = _allCustomColors;

        if (!string.IsNullOrWhiteSpace(CustomColorSearchQuery))
        {
            var searchTerm = CustomColorSearchQuery.Trim();
            query = query.Where(item =>
                (IsCustomColorSearchNameEnabled
                    && !string.IsNullOrWhiteSpace(item.Name)
                    && item.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                || (IsCustomColorSearchCodeEnabled
                    && item.ColorCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        query = _customColorSortKey switch
        {
            "name-desc" => query.OrderByDescending(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };

        CustomColors.Clear();
        foreach (var customColor in query)
        {
            CustomColors.Add(customColor);
        }

        RaiseCustomColorCollectionStateChanged();
    }

    private void RaiseCustomColorCollectionStateChanged()
    {
        this.RaisePropertyChanged(nameof(HasCustomColors));
        this.RaisePropertyChanged(nameof(HasStoredCustomColors));
        this.RaisePropertyChanged(nameof(IsCustomColorListEmpty));
        this.RaisePropertyChanged(nameof(IsCustomColorSearchResultEmpty));
    }

    private void RebuildPresetColors()
    {
        PresetColors.Clear();
        PresetColors.Add(new PasswordColorOptionViewModel("teal", GetTranslation("Passwords_Color_Teal"), PasswordColorUtility.DefaultColor));

        foreach (var customColor in _savedCustomColors)
        {
            var displayName = string.IsNullOrWhiteSpace(customColor.ColorName)
                ? customColor.ColorCode
                : customColor.ColorName;
            PresetColors.Add(new PasswordColorOptionViewModel($"saved-custom:{customColor.Id:N}", displayName, customColor.ColorCode));
        }

        PresetColors.Add(new PasswordColorOptionViewModel(CustomColorKey, GetTranslation("Passwords_Color_More"), EditorColor, isManageColorsOption: true));
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

    private void SelectDefaultPresetColor() => ApplyEditorColor(PasswordColorUtility.DefaultColor);

    private void SelectDefaultSortOption() => SelectedSortOption = SortOptions.FirstOrDefault(item => item.Key == "name-asc") ?? SortOptions.FirstOrDefault();

    private void SetPasswordSearchMode(ref bool field, bool value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        if (!value && EnabledPasswordSearchModeCount <= 1)
        {
            this.RaisePropertyChanged(propertyName);
            RaisePasswordSearchModeToggleProperties();
            return;
        }

        this.RaiseAndSetIfChanged(ref field, value, propertyName);
        RaisePasswordSearchModeToggleProperties();
        ApplyFiltersAndSorting(SelectedPassword?.Id, preserveSelection: true);
    }

    private bool CanTogglePasswordSearchMode(bool isEnabled) => !isEnabled || EnabledPasswordSearchModeCount > 1;

    private void RaisePasswordSearchModeToggleProperties()
    {
        this.RaisePropertyChanged(nameof(CanTogglePasswordSearchName));
        this.RaisePropertyChanged(nameof(CanTogglePasswordSearchDescription));
        this.RaisePropertyChanged(nameof(CanTogglePasswordSearchTag));
    }

    private int EnabledPasswordSearchModeCount =>
        (_isPasswordSearchNameEnabled ? 1 : 0)
        + (_isPasswordSearchDescriptionEnabled ? 1 : 0)
        + (_isPasswordSearchTagEnabled ? 1 : 0);

    private void SetCustomColorSearchMode(ref bool field, bool value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        if (!value && EnabledCustomColorSearchModeCount <= 1)
        {
            this.RaisePropertyChanged(propertyName);
            RaiseCustomColorSearchModeToggleProperties();
            return;
        }

        this.RaiseAndSetIfChanged(ref field, value, propertyName);
        RaiseCustomColorSearchModeToggleProperties();
        ApplyCustomColorFiltersAndSorting();
    }

    private bool CanToggleCustomColorSearchMode(bool isEnabled) => !isEnabled || EnabledCustomColorSearchModeCount > 1;

    private void RaiseCustomColorSearchModeToggleProperties()
    {
        this.RaisePropertyChanged(nameof(CanToggleCustomColorSearchName));
        this.RaisePropertyChanged(nameof(CanToggleCustomColorSearchCode));
    }

    private int EnabledCustomColorSearchModeCount =>
        (_isCustomColorSearchNameEnabled ? 1 : 0)
        + (_isCustomColorSearchCodeEnabled ? 1 : 0);

    private void UpdateSortOptionSelectionMarks()
    {
        foreach (var option in SortOptions)
            option.IsSelected = ReferenceEquals(option, SelectedSortOption);
    }

    private void SelectSortOptionByKey(string key)
    {
        var option = SortOptions.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));

        if (option is not null)
            SelectedSortOption = option;
    }

    private string BuildSortMenuLabel(string key, string translationKey) =>
        $"{(string.Equals(SelectedSortOption?.Key, key, StringComparison.Ordinal) ? "✓ " : "   ")}{GetTranslation(translationKey)}";

    private void RaiseSortMenuLabelProperties()
    {
        this.RaisePropertyChanged(nameof(SortNameAscMenuLabel));
        this.RaisePropertyChanged(nameof(SortNameDescMenuLabel));
        this.RaisePropertyChanged(nameof(SortCreatedNewestMenuLabel));
        this.RaisePropertyChanged(nameof(SortCreatedOldestMenuLabel));
        this.RaisePropertyChanged(nameof(SortUpdatedNewestMenuLabel));
        this.RaisePropertyChanged(nameof(SortUpdatedOldestMenuLabel));
    }

    private void SelectCustomColorSortOption(string key)
    {
        if (key is not ("name-asc" or "name-desc")
            || string.Equals(_customColorSortKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _customColorSortKey = key;
        RaiseCustomColorSortMenuLabelProperties();
        ApplyCustomColorFiltersAndSorting();
    }

    private string BuildCustomColorSortMenuLabel(string key, string translationKey) =>
        $"{(string.Equals(_customColorSortKey, key, StringComparison.Ordinal) ? "✓ " : "   ")}{GetTranslation(translationKey)}";

    private void RaiseCustomColorSortMenuLabelProperties()
    {
        this.RaisePropertyChanged(nameof(CustomColorsSortNameAscMenuLabel));
        this.RaisePropertyChanged(nameof(CustomColorsSortNameDescMenuLabel));
    }

    private void OpenCustomColorList()
    {
        ClearStatusMessage();
        IsCustomColorDeleteConfirmationOpen = false;
        CustomColorPendingDeletion = null;
        CurrentPane = CustomColorListPane;
    }

    private void BackFromCustomColorList()
    {
        if (IsCustomColorMultiSelectionActive)
        {
            ExitCustomColorMultiSelection();
            return;
        }

        ClearStatusMessage();
        SetCurrentPane(EditorPane, true);

        // "Manage colors" is a navigation action, not a real color option.
        // Re-notify the binding after returning so the ComboBox restores the
        // actual color that was selected before the user opened this page.
        this.RaisePropertyChanged(nameof(SelectedEditorColorOption));
    }

    private void OpenCustomColorPicker()
    {
        SetCustomColorPickerMode(null);
        ResetCustomColorPickerDraft();
        ClearStatusMessage();
        CurrentPane = ColorPane;
    }

    private void OpenCustomColorPickerForEditing(CustomColorItemViewModel customColor)
    {
        SetCustomColorPickerMode(customColor.Id);
        LoadCustomColorPickerDraft(customColor.Name, customColor.ColorCode);
        ClearStatusMessage();
        CurrentPane = ColorPane;
    }

    private void BackFromColorPicker()
    {
        ClearStatusMessage();
        ResetCustomColorPickerDraft();
        SetCustomColorPickerMode(null);
        SetCurrentPane(CustomColorListPane, true);
    }

    private async Task SaveCustomColorAsync()
    {
        if (_isSavingCustomColor)
        {
            return;
        }

        ClearStatusMessage();

        if (!PasswordColorUtility.TryNormalizeHexColor(CustomColorPickerCode, out var colorCode))
        {
            ShowErrorMessage(GetTranslation("Passwords_ColorPicker_InvalidCode"));
            return;
        }

        if (string.Equals(colorCode, PasswordColorUtility.DefaultColor, StringComparison.OrdinalIgnoreCase))
        {
            ShowErrorMessage(GetTranslation("Passwords_CustomColors_Add_DefaultColor"));
            return;
        }

        var colorName = string.IsNullOrWhiteSpace(CustomColorPickerName)
            ? null
            : CustomColorPickerName.Trim();
        var editingCustomColorId = _editingCustomColorId;

        try
        {
            _isSavingCustomColor = true;

            if (editingCustomColorId.HasValue)
            {
                await _endpoints.UpdateCustomUserColorAsync(_token, new UpdateCustomUserColorRequest
                {
                    Id = editingCustomColorId.Value,
                    ColorName = colorName,
                    ClearColorName = colorName is null,
                    ColorCode = colorCode
                });
            }
            else
            {
                await _endpoints.AddCustomUserColorsAsync(_token,
                [
                    new NewCustomUserColorRequest
                    {
                        ColorName = colorName,
                        ColorCode = colorCode
                    }
                ]);
            }

            if (!await RefreshAsync(false))
            {
                return;
            }

            var successMessage = CustomColorSaveSuccessMessage;
            ResetCustomColorPickerDraft();
            SetCustomColorPickerMode(null);
            SetCurrentPane(CustomColorListPane, true);
            ShowSuccessMessage(successMessage);
        }
        catch (Exception ex)
        {
            ShowErrorMessage(GetSafeErrorMessage(ex));
        }
        finally
        {
            _isSavingCustomColor = false;
        }
    }

    private void SetCustomColorPickerMode(Guid? customColorId)
    {
        if (_editingCustomColorId == customColorId)
        {
            return;
        }

        _editingCustomColorId = customColorId;
        this.RaisePropertyChanged(nameof(IsEditingCustomColor));
        this.RaisePropertyChanged(nameof(ColorPickerTitle));
        this.RaisePropertyChanged(nameof(AddColorButtonLabel));
        this.RaisePropertyChanged(nameof(CustomColorSaveSuccessMessage));
    }

    private void ResetCustomColorPickerDraft() =>
        LoadCustomColorPickerDraft(string.Empty, PasswordColorUtility.DefaultColor);

    private void LoadCustomColorPickerDraft(string? name, string colorCode)
    {
        var normalizedColor = PasswordColorUtility.NormalizeKnownColor(colorCode);

        _isUpdatingCustomColorPickerFields = true;
        try
        {
            CustomColorPickerName = name ?? string.Empty;
            _customColorPickerCode = normalizedColor;
            this.RaisePropertyChanged(nameof(CustomColorPickerCode));
            _customColorPickerColor = ColorFromNormalizedArgbHex(normalizedColor);
            this.RaisePropertyChanged(nameof(CustomColorPickerColor));
        }
        finally
        {
            _isUpdatingCustomColorPickerFields = false;
        }
    }

    private void SyncCustomColorPickerCodeFromColor()
    {
        if (_isUpdatingCustomColorPickerFields)
        {
            return;
        }

        _isUpdatingCustomColorPickerFields = true;
        try
        {
            _customColorPickerCode = ToArgbHex(CustomColorPickerColor);
            this.RaisePropertyChanged(nameof(CustomColorPickerCode));
        }
        finally
        {
            _isUpdatingCustomColorPickerFields = false;
        }
    }

    private void SetCustomColorPickerColorFromNormalizedCode(string normalizedColor)
    {
        _isUpdatingCustomColorPickerFields = true;
        try
        {
            _customColorPickerColor = ColorFromNormalizedArgbHex(normalizedColor);
            this.RaisePropertyChanged(nameof(CustomColorPickerColor));
        }
        finally
        {
            _isUpdatingCustomColorPickerFields = false;
        }
    }

    private static Color ColorFromNormalizedArgbHex(string normalizedColor) =>
        Color.FromArgb(
            Convert.ToByte(normalizedColor.Substring(1, 2), 16),
            Convert.ToByte(normalizedColor.Substring(3, 2), 16),
            Convert.ToByte(normalizedColor.Substring(5, 2), 16),
            Convert.ToByte(normalizedColor.Substring(7, 2), 16));

    private static string ToArgbHex(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private Task BeginDeleteCustomColorAsync(CustomColorItemViewModel customColor)
    {
        ClearStatusMessage();
        CustomColorPendingDeletion = customColor;
        IsCustomColorDeleteConfirmationOpen = true;
        return Task.CompletedTask;
    }

    private void CancelDeleteCustomColor()
    {
        ClearStatusMessage();
        IsCustomColorDeleteConfirmationOpen = false;
        CustomColorPendingDeletion = null;
    }

    private async Task ConfirmDeleteCustomColorAsync()
    {
        if (_isDeletingCustomColor || CustomColorPendingDeletion is null)
        {
            return;
        }

        ClearStatusMessage();
        var customColor = CustomColorPendingDeletion;

        try
        {
            _isDeletingCustomColor = true;
            await _endpoints.DeleteCustomUserColorAsync(_token, customColor.Id);
            CancelDeleteCustomColor();

            if (!await RefreshAsync(false))
            {
                return;
            }

            ShowSuccessMessage(CustomColorDeleteSuccessMessage);
        }
        catch (Exception ex)
        {
            ShowErrorMessage(GetSafeErrorMessage(ex));
        }
        finally
        {
            _isDeletingCustomColor = false;
        }
    }

    private void ApplyManualColorCode()
    {
        ClearStatusMessage();

        if (!PasswordColorUtility.TryNormalizeHexColor(CustomColorCode, out var normalizedColor))
        {
            ShowErrorMessage(GetTranslation("Passwords_ColorPicker_InvalidCode"));
            return;
        }

        ApplyEditorColor(normalizedColor);
        ClearStatusMessage();
    }

    private void ApplyColorFromSliders()
    {
        if (_isUpdatingColorFields)
        {
            return;
        }

        var normalizedColor = $"#{PasswordColorUtility.ToComponentByte(CustomAlpha):X2}{PasswordColorUtility.ToComponentByte(CustomRed):X2}{PasswordColorUtility.ToComponentByte(CustomGreen):X2}{PasswordColorUtility.ToComponentByte(CustomBlue):X2}";
        ApplyEditorColor(normalizedColor, updateColorFields: false);
    }

    private void ApplyEditorColor(string color, bool updateSelectedPreset = true, bool updateColorFields = true)
    {
        if (!PasswordColorUtility.TryNormalizeHexColor(color, out var normalizedColor))
        {
            normalizedColor = PasswordColorUtility.DefaultColor;
        }

        EditorColor = normalizedColor;
        CustomColorCode = normalizedColor;
        UpdateMoreColorsOption(normalizedColor);

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
            && item.Key != SelectedCustomColorKey
            && string.Equals(PasswordColorUtility.NormalizeKnownColor(item.HexValue), normalizedColor, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            RemoveSelectedCustomColorOption();
            SetSelectedEditorColorOption(match);
            return;
        }

        var customOption = PresetColors.FirstOrDefault(item => item.Key == SelectedCustomColorKey);
        if (customOption is null)
        {
            customOption = new PasswordColorOptionViewModel(SelectedCustomColorKey, normalizedColor, normalizedColor);
            var moreColorsIndex = PresetColors.ToList().FindIndex(item => item.Key == CustomColorKey);
            PresetColors.Insert(moreColorsIndex >= 0 ? moreColorsIndex : PresetColors.Count, customOption);
        }
        else
        {
            customOption.Update(normalizedColor, normalizedColor);
        }

        SetSelectedEditorColorOption(customOption);
    }

    private void UpdateMoreColorsOption(string normalizedColor)
    {
        var moreColorsOption = PresetColors.FirstOrDefault(item => item.Key == CustomColorKey);
        moreColorsOption?.Update(GetTranslation("Passwords_Color_More"), normalizedColor);
    }

    private void RemoveSelectedCustomColorOption()
    {
        var customOption = PresetColors.FirstOrDefault(item => item.Key == SelectedCustomColorKey);
        if (customOption is not null)
        {
            PresetColors.Remove(customOption);
        }
    }

    private void SetSelectedEditorColorOption(PasswordColorOptionViewModel option)
    {
        if (ReferenceEquals(_selectedEditorColorOption, option))
        {
            return;
        }

        _selectedEditorColorOption = option;
        this.RaisePropertyChanged(nameof(SelectedEditorColorOption));
    }

    private void SyncColorFieldsFromEditorColor()
    {
        if (!PasswordColorUtility.TryNormalizeHexColor(EditorColor, out var normalizedColor))
        {
            normalizedColor = PasswordColorUtility.DefaultColor;
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


}
