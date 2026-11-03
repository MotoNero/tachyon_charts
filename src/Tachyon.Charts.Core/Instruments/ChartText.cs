using System.Globalization;

namespace Tachyon.Charts.Core.Instruments;

/// <summary>The languages of the texts in a chart.</summary>
public enum ChartLanguage
{
    /// <summary>English, with a decimal point.</summary>
    English,

    /// <summary>German, with a decimal comma.</summary>
    German,
}

/// <summary>The texts the instruments show. More languages are one more column in the table.</summary>
public enum ChartTextKey
{
    /// <summary>The mean of a waveform.</summary>
    Mean,

    /// <summary>A trace above its mask.</summary>
    MaskFail,

    /// <summary>A trace within its limits.</summary>
    Pass,

    /// <summary>A trace outside of its limits.</summary>
    Fail,

    /// <summary>The trigger fired.</summary>
    Triggered,

    /// <summary>The trigger waits for an edge.</summary>
    Waiting,

    /// <summary>A single acquisition is done.</summary>
    Stopped,

    /// <summary>The display runs without a trigger.</summary>
    Auto,

    /// <summary>The direction around the center of a pattern.</summary>
    Azimuth,

    /// <summary>The height of a direction.</summary>
    Elevation,

    /// <summary>The gain of an antenna.</summary>
    Gain,

    /// <summary>A value at a place.</summary>
    Value,

    /// <summary>Data on their way into the chart.</summary>
    Loading,
}

/// <summary>Switches the language of all charts in the process at any time.</summary>
public static class ChartText
{
    private static readonly string[][] _table =
    [
        ["Mean", "Mask FAIL", "PASS", "FAIL", "Trig'd", "Wait", "Stop", "Auto", "Azimuth", "Elevation", "Gain", "Value", "Loading"],
        ["Mittel", "Maske verletzt", "OK", "FEHLER", "Ausgelöst", "Warten", "Stopp", "Auto", "Azimut", "Elevation", "Gewinn", "Wert", "Laden"],
    ];

    private static readonly CultureInfo[] _cultures = [CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("de-DE")];
    private static int _language;
    private static long _version;

    /// <summary>Gets or sets the language.</summary>
    public static ChartLanguage Language
    {
        get => (ChartLanguage)Volatile.Read(ref _language);
        set
        {
            Volatile.Write(ref _language, (int)value);
            Interlocked.Increment(ref _version);
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>Occurs on the thread that set the language after a change.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Gets the format of numbers in the language.</summary>
    public static CultureInfo Culture => _cultures[Volatile.Read(ref _language)];

    /// <summary>Gets a version that grows with each change, so that charts draw again.</summary>
    public static long Version => Interlocked.Read(ref _version);

    /// <summary>Finds a text in the current language.</summary>
    /// <param name="key">The text.</param>
    /// <returns>The words.</returns>
    public static string Get(ChartTextKey key) => Get(key, Language);

    /// <summary>Finds a text.</summary>
    /// <param name="key">The text.</param>
    /// <param name="language">The language.</param>
    /// <returns>The words.</returns>
    public static string Get(ChartTextKey key, ChartLanguage language) => _table[(int)language][(int)key];
}
