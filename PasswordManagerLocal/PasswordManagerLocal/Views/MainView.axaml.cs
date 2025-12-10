using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;

namespace PasswordManagerLocal.Views
{
    public partial class MainView : UserControl
    {
        private Button? _settingsButton;
        private ContextMenu? _settingsMenu;

        public MainView()
        {
            InitializeComponent();

            _settingsButton = this.FindControl<Button>("SettingsButton");
            if (_settingsButton != null)
            {
                _settingsButton.Click += SettingsButtonOnClick;
                _settingsMenu = _settingsButton.ContextMenu;

                // Hover only on desktop platforms – Androidon maradjon tap-to-open
                if (!OperatingSystem.IsAndroid() && _settingsMenu != null)
                {
                    _settingsButton.PointerEntered += SettingsButtonOnPointerEntered;
                }
            }
        }

        private void SettingsButtonOnClick(object? sender, RoutedEventArgs e)
        {
            if (_settingsButton == null || _settingsMenu == null)
            {
                return;
            }

            if (_settingsMenu.IsOpen)
            {
                _settingsMenu.Close();
            }
            else
            {
                _settingsMenu.Open(_settingsButton);
            }
        }

        private void SettingsButtonOnPointerEntered(object? sender, PointerEventArgs e)
        {
            if (_settingsButton == null || _settingsMenu == null)
            {
                return;
            }

            // Hoverre csak akkor nyitunk, ha még nincs nyitva
            if (!_settingsMenu.IsOpen)
            {
                _settingsMenu.Open(_settingsButton);
            }
        }
    }
}