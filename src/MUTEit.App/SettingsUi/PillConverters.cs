using Avalonia.Data.Converters;

namespace MUTEit.App.SettingsUi;

/// <summary>Converters for result pills (green / red).</summary>
public static class PillConverters
{
    public static readonly IValueConverter IsOk = new FuncValueConverter<PillState, bool>(s => s == PillState.Ok);

    public static readonly IValueConverter IsError = new FuncValueConverter<PillState, bool>(s => s == PillState.Error);
}
