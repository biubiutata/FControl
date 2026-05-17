using System.Collections.ObjectModel;
using System.ComponentModel;
using FControl.Models;
using FControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class KeyMappingPage : Page
{
    private readonly AppConfigurationService _configurationService = AppServices.Configuration;
    private bool _isLoadingMappings;

    public ObservableCollection<KeyMappingItem> KeyMappings { get; } = [];

    public KeyMappingPage()
    {
        InitializeComponent();
        Loaded += KeyMappingPage_Loaded;
        Unloaded += KeyMappingPage_Unloaded;
        LoadMappings();
        UpdateHotKeyStatus(AppServices.HotKeys?.LastRegistrationFailures ?? []);
    }

    private void KeyMappingPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (AppServices.HotKeys is not null)
        {
            AppServices.HotKeys.RegistrationChanged += HotKeys_RegistrationChanged;
        }
    }

    private void KeyMappingPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (AppServices.HotKeys is not null)
        {
            AppServices.HotKeys.RegistrationChanged -= HotKeys_RegistrationChanged;
        }
    }

    private void HotKeys_RegistrationChanged(object? sender, IReadOnlyList<HotKeyRegistrationFailure> failures)
    {
        DispatcherQueue.TryEnqueue(() => UpdateHotKeyStatus(failures));
    }

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        _configurationService.ResetToDefaults();
        LoadMappings();
        UpdateHotKeyStatus(AppServices.HotKeys?.LastRegistrationFailures ?? []);
    }

    private void LoadMappings()
    {
        _isLoadingMappings = true;

        foreach (var mapping in KeyMappings)
        {
            mapping.Changed -= Mapping_Changed;
        }

        KeyMappings.Clear();
        foreach (var mapping in _configurationService.Current.KeyMappings)
        {
            var item = KeyMappingItem.FromConfig(mapping);
            item.Changed += Mapping_Changed;
            KeyMappings.Add(item);
        }

        _isLoadingMappings = false;
    }

    private void Mapping_Changed(object? sender, EventArgs e)
    {
        if (_isLoadingMappings)
        {
            return;
        }

        _configurationService.SetKeyMappings(KeyMappings.Select(static mapping => mapping.ToConfig()));
    }

    private void UpdateHotKeyStatus(IReadOnlyList<HotKeyRegistrationFailure> failures)
    {
        if (failures.Count == 0)
        {
            HotKeyStatusInfoBar.IsOpen = false;
            HotKeyStatusInfoBar.Message = string.Empty;
            return;
        }

        HotKeyStatusInfoBar.IsOpen = true;
        HotKeyStatusInfoBar.Message = string.Join(Environment.NewLine, failures.Select(static failure => failure.Message));
    }
}

public sealed class KeyMappingItem : INotifyPropertyChanged
{
    private string _action;
    private double _seconds;
    private Visibility _secondsVisibility;

    private KeyMappingItem(string key, string action, double seconds)
    {
        Key = key;
        _action = action;
        _seconds = seconds;
        _secondsVisibility = HotKeyActionMetadata.GetSecondsVisibility(HotKeyActionMetadata.FromDisplayName(action));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;

    public IReadOnlyList<string> Actions => HotKeyActionMetadata.AvailableActionNames;
    public string Key { get; }

    public string Action
    {
        get => _action;
        set
        {
            if (_action == value)
            {
                return;
            }

            _action = value;
            SecondsVisibility = HotKeyActionMetadata.GetSecondsVisibility(HotKeyActionMetadata.FromDisplayName(_action));
            OnPropertyChanged(nameof(Action));
            OnChanged();
        }
    }

    public double Seconds
    {
        get => _seconds;
        set
        {
            var clampedValue = Math.Clamp(value, 1, 60);
            if (Math.Abs(_seconds - clampedValue) < 0.001)
            {
                return;
            }

            _seconds = clampedValue;
            OnPropertyChanged(nameof(Seconds));
            OnChanged();
        }
    }

    public Visibility SecondsVisibility
    {
        get => _secondsVisibility;
        private set
        {
            if (_secondsVisibility == value)
            {
                return;
            }

            _secondsVisibility = value;
            OnPropertyChanged(nameof(SecondsVisibility));
        }
    }

    public static KeyMappingItem FromConfig(KeyMappingConfig config)
    {
        return new KeyMappingItem(
            config.Key,
            HotKeyActionMetadata.GetDisplayName(config.Action),
            Math.Clamp(config.SeekSeconds, 1, 60));
    }

    public KeyMappingConfig ToConfig()
    {
        return new KeyMappingConfig
        {
            Key = Key,
            Action = HotKeyActionMetadata.FromDisplayName(Action),
            SeekSeconds = (int)Math.Clamp(Math.Round(Seconds), 1, 60)
        };
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
