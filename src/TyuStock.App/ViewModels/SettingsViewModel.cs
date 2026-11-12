using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TyuStock.App.Services;
using TyuStock.Core.Settings;

namespace TyuStock.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly ProxyMode[] ProxyModes = [ProxyMode.AutoDetect, ProxyMode.System, ProxyMode.Manual, ProxyMode.Direct];
    private readonly SettingsStore _store;
    private readonly LocalizationService _loc;
    private readonly ThemeService _theme;
    private readonly INetworkController? _network;
    private readonly bool _ready;

    [ObservableProperty] public partial int LanguageIndex { get; set; }
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial int UpDownIndex { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsManualProxy))] public partial int ProxyModeIndex { get; set; }
    [ObservableProperty] public partial string ManualProxy { get; set; } = "";
    [ObservableProperty] public partial string EffectiveProxy { get; set; } = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsFixedRate))] public partial int RateModeIndex { get; set; }
    [ObservableProperty] public partial string FixedRateText { get; set; } = "";
    [ObservableProperty] public partial string FixedRateError { get; set; } = "";

    public bool IsFixedRate => RateModeIndex == 1;

    public bool IsManualProxy => ProxyModeIndex == 2;
    public string DataDirectory { get; }
    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public SettingsViewModel(SettingsStore store, LocalizationService loc, ThemeService theme, DataDirectoryInfo dataDir, IServiceProvider sp)
    {
        _store = store; _loc = loc; _theme = theme;
        _network = sp.GetService(typeof(INetworkController)) as INetworkController;
        var s = store.Current;
        LanguageIndex = s.Language == "en-US" ? 1 : 0;
        ThemeIndex = (int)s.Theme;
        UpDownIndex = (int)s.UpDownColors;
        ProxyModeIndex = Math.Max(0, Array.IndexOf(ProxyModes, s.Network.ProxyMode));
        ManualProxy = s.Network.ManualProxy;
        DataDirectory = dataDir.Path;
        EffectiveProxy = _network?.EffectiveDescription ?? "—";
        RateModeIndex = s.Pricing.RateMode == RiskFreeRateMode.Fixed ? 1 : 0;
        FixedRateText = s.Pricing.FixedRatePercent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        _ready = true;
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (!_ready) return;
        var lang = value == 1 ? "en-US" : "zh-CN";
        _loc.Apply(lang);
        _store.Update(s => s.Language = lang);
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (!_ready) return;
        var mode = (AppThemeMode)Math.Clamp(value, 0, 2);
        _theme.Apply(mode);
        _store.Update(s => s.Theme = mode);
    }

    partial void OnUpDownIndexChanged(int value)
    {
        if (!_ready) return;
        var mode = (UpDownColorMode)Math.Clamp(value, 0, 1);
        ThemeService.ApplyUpDownColors(mode);
        _store.Update(s => s.UpDownColors = mode);
    }

    partial void OnProxyModeIndexChanged(int value)
    {
        if (!_ready) return;
        _store.Update(s => s.Network.ProxyMode = ProxyModes[Math.Clamp(value, 0, ProxyModes.Length - 1)]);
        if (!IsManualProxy) _ = ApplyProxyAsync();
    }

    partial void OnManualProxyChanged(string value)
    {
        if (!_ready) return;
        _store.Update(s => s.Network.ManualProxy = value.Trim());
    }

    partial void OnRateModeIndexChanged(int value)
    {
        if (!_ready) return;
        _store.Update(s => s.Pricing.RateMode = value == 1 ? RiskFreeRateMode.Fixed : RiskFreeRateMode.Treasury13Week);
    }

    partial void OnFixedRateTextChanged(string value)
    {
        if (!_ready) return;
        if (double.TryParse(value.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct)
            && pct is >= -1 and <= 20)
        {
            FixedRateError = "";
            _store.Update(s => s.Pricing.FixedRatePercent = pct);
        }
        else FixedRateError = _loc["Set_RateInvalid"];
    }

    [RelayCommand]
    private async Task ApplyProxyAsync()
    {
        if (_network is null) return;
        EffectiveProxy = "…";
        EffectiveProxy = await _network.ReconfigureAsync();
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataDirectory}\"") { UseShellExecute = true });
}
