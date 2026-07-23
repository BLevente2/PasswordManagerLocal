using PasswordManagerLocal.Frontend.Localization;
using PasswordManagerLocal.Frontend.Services;
using ReactiveUI;
using System.Reactive;

namespace PasswordManagerLocal.Frontend.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly DeviceAppPreferencesService _deviceAppPreferences;
    private bool _isAppearanceSectionExpanded;
    private bool _isBackgroundSectionExpanded;

    public SettingsViewModel(
        UiPreferencesService uiPreferences,
        DeviceAppPreferencesService deviceAppPreferences,
        Action navigateBack,
        bool isBackgroundSyncSettingAvailable = true)
        : base(uiPreferences)
    {
        _deviceAppPreferences = deviceAppPreferences ?? throw new ArgumentNullException(nameof(deviceAppPreferences));
        ArgumentNullException.ThrowIfNull(navigateBack);
        IsBackgroundSyncSettingAvailable = isBackgroundSyncSettingAvailable;

        NavigateBackCommand = ReactiveCommand.Create(navigateBack);
        ToggleAppearanceSectionCommand = ReactiveCommand.Create(ToggleAppearanceSection);
        ToggleBackgroundSectionCommand = ReactiveCommand.Create(ToggleBackgroundSection);
        LanguageOptions =
        [
            new SettingsLanguageOptionViewModel(AppLanguage.English, GetTranslation("Language_English")),
            new SettingsLanguageOptionViewModel(AppLanguage.Hungarian, GetTranslation("Language_Hungarian"))
        ];
        ThemeOptions =
        [
            new SettingsThemeOptionViewModel(AppThemeMode.Light, GetTranslation("Theme_Light")),
            new SettingsThemeOptionViewModel(AppThemeMode.Dark, GetTranslation("Theme_Dark"))
        ];
        _deviceAppPreferences.PreferencesChanged += HandleDeviceAppPreferencesChanged;
    }

    public ReactiveCommand<Unit, Unit> NavigateBackCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleAppearanceSectionCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleBackgroundSectionCommand { get; }

    public bool IsAppearanceSectionExpanded
    {
        get => _isAppearanceSectionExpanded;
        private set
        {
            if (_isAppearanceSectionExpanded == value)
                return;

            this.RaiseAndSetIfChanged(ref _isAppearanceSectionExpanded, value);
            this.RaisePropertyChanged(nameof(IsAppearanceSectionCollapsed));
        }
    }

    public bool IsAppearanceSectionCollapsed => !IsAppearanceSectionExpanded;

    public bool IsBackgroundSectionExpanded
    {
        get => _isBackgroundSectionExpanded;
        private set
        {
            if (_isBackgroundSectionExpanded == value)
                return;

            this.RaiseAndSetIfChanged(ref _isBackgroundSectionExpanded, value);
            this.RaisePropertyChanged(nameof(IsBackgroundSectionCollapsed));
        }
    }

    public bool IsBackgroundSectionCollapsed => !IsBackgroundSectionExpanded;

    public IReadOnlyList<SettingsLanguageOptionViewModel> LanguageOptions { get; }

    public SettingsLanguageOptionViewModel? SelectedLanguageOption
    {
        get => LanguageOptions.FirstOrDefault(option => option.Language == CurrentLanguage);
        set
        {
            if (value is not null && value.Language != CurrentLanguage)
                UiPreferences.CurrentLanguage = value.Language;
        }
    }

    public IReadOnlyList<SettingsThemeOptionViewModel> ThemeOptions { get; }

    public SettingsThemeOptionViewModel? SelectedThemeOption
    {
        get => ThemeOptions.FirstOrDefault(option => option.Theme == CurrentThemeMode);
        set
        {
            if (value is not null && value.Theme != CurrentThemeMode)
                UiPreferences.CurrentThemeMode = value.Theme;
        }
    }

    public bool IsBackgroundSyncSettingAvailable { get; }

    public bool IsBackgroundSyncEnabled
    {
        get => _deviceAppPreferences.BackgroundSyncEnabled;
        set
        {
            if (!IsBackgroundSyncSettingAvailable ||
                _deviceAppPreferences.BackgroundSyncEnabled == value)
            {
                return;
            }

            _deviceAppPreferences.BackgroundSyncEnabled = value;
        }
    }

    public string Title => GetTranslation("Settings");

    public string Subtitle => GetTranslation("Settings_Subtitle");

    public string BackLabel => GetTranslation("Common_Back");

    public string AppearanceTitle => GetTranslation("Settings_Appearance_Title");

    public string AppearanceDescription => GetTranslation("Settings_Appearance_Description");

    public string LanguageLabel => GetTranslation("Settings_Language");

    public string LanguageDescription => GetTranslation("Settings_Language_Description");

    public string ThemeLabel => GetTranslation("Settings_Theme");

    public string ThemeDescription => GetTranslation("Settings_Theme_Description");

    public string BackgroundOperationTitle => GetTranslation("Settings_Background_Title");

    public string BackgroundOperationDescription => GetTranslation("Settings_Background_Description");

    public string BackgroundSyncLabel => GetTranslation("Settings_BackgroundSync_Label");

    public string BackgroundSyncDescription => GetTranslation("Settings_BackgroundSync_Description");

    public string OnLabel => GetTranslation("Common_On");

    public string OffLabel => GetTranslation("Common_Off");

    protected override void OnLanguageChanged()
    {
        LanguageOptions[0].UpdateDisplayName(GetTranslation("Language_English"));
        LanguageOptions[1].UpdateDisplayName(GetTranslation("Language_Hungarian"));
        ThemeOptions[0].UpdateDisplayName(GetTranslation("Theme_Light"));
        ThemeOptions[1].UpdateDisplayName(GetTranslation("Theme_Dark"));

        RaisePropertiesChanged(
        [
            nameof(Title),
            nameof(Subtitle),
            nameof(BackLabel),
            nameof(AppearanceTitle),
            nameof(AppearanceDescription),
            nameof(LanguageLabel),
            nameof(LanguageDescription),
            nameof(ThemeLabel),
            nameof(ThemeDescription),
            nameof(SelectedLanguageOption),
            nameof(SelectedThemeOption),
            nameof(BackgroundOperationTitle),
            nameof(BackgroundOperationDescription),
            nameof(BackgroundSyncLabel),
            nameof(BackgroundSyncDescription),
            nameof(OnLabel),
            nameof(OffLabel)
        ]);
    }

    protected override void OnThemeChanged() =>
        this.RaisePropertyChanged(nameof(SelectedThemeOption));

    private void ToggleAppearanceSection()
    {
        var shouldExpand = !IsAppearanceSectionExpanded;
        IsAppearanceSectionExpanded = shouldExpand;

        if (shouldExpand)
            IsBackgroundSectionExpanded = false;
    }

    private void ToggleBackgroundSection()
    {
        var shouldExpand = !IsBackgroundSectionExpanded;
        IsBackgroundSectionExpanded = shouldExpand;

        if (shouldExpand)
            IsAppearanceSectionExpanded = false;
    }

    private void HandleDeviceAppPreferencesChanged(
        object? sender,
        DeviceAppPreferencesChangedEventArgs args)
    {
        this.RaisePropertyChanged(nameof(IsBackgroundSyncEnabled));
    }
}
