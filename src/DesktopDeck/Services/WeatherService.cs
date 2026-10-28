using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace DesktopDeck.Services;

/// <summary>Weather from Open-Meteo (free, no key or account). Only the place's coordinates are sent.</summary>
public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public sealed record Place(string Name, string Region, double Latitude, double Longitude)
    {
        public string Label => string.IsNullOrWhiteSpace(Region) ? Name : $"{Name}, {Region}";
    }

    public sealed record Day(DateOnly Date, int Code, double High, double Low, int? RainChance);
    public sealed record Report(double Temperature, double FeelsLike, int Code, bool IsDay, double Wind, int Humidity, List<Day> Days);

    /// <summary>"Clear", "Partly cloudy", … and which icon to draw: clear, partly, cloudy, fog, rain, snow, storm.</summary>
    public static (string Text, string Icon) Describe(int code) => code switch
    {
        0 => ("Clear", "clear"),
        1 => ("Mostly clear", "clear"),
        2 => ("Partly cloudy", "partly"),
        3 => ("Cloudy", "cloudy"),
        45 or 48 => ("Fog", "fog"),
        51 or 53 or 55 => ("Drizzle", "rain"),
        56 or 57 => ("Freezing drizzle", "rain"),
        61 => ("Light rain", "rain"),
        63 => ("Rain", "rain"),
        65 => ("Heavy rain", "rain"),
        66 or 67 => ("Freezing rain", "rain"),
        71 => ("Light snow", "snow"),
        73 => ("Snow", "snow"),
        75 => ("Heavy snow", "snow"),
        77 => ("Snow grains", "snow"),
        80 or 81 => ("Showers", "rain"),
        82 => ("Heavy showers", "rain"),
        85 or 86 => ("Snow showers", "snow"),
        95 => ("Thunderstorm", "storm"),
        96 or 99 => ("Thunderstorm with hail", "storm"),
        _ => ("—", "cloudy"),
    };

    public static string DefaultUnits => RegionInfo.CurrentRegion.IsMetric ? "C" : "F";

    public static async Task<List<Place>> FindAsync(string name, CancellationToken cancel = default)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=6&language=en&format=json&name=" + Uri.EscapeDataString(name.Trim());
        return ParsePlaces(await Http.GetStringAsync(url, cancel).ConfigureAwait(false));
    }

    public static List<Place> ParsePlaces(string json)
    {
        var list = new List<Place>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in results.EnumerateArray())
        {
            if (!r.TryGetProperty("latitude", out var lat) || !r.TryGetProperty("longitude", out var lon)) continue;
            string region = string.Join(", ", new[] { Text(r, "admin1"), Text(r, "country") }.Where(s => s.Length > 0));
            list.Add(new Place(Text(r, "name"), region, lat.GetDouble(), lon.GetDouble()));
        }
        return list;
    }

    /// <summary>Downloads the current weather and a 7-day forecast. The raw JSON is returned so it can be cached.</summary>
    public static Task<string> FetchAsync(double latitude, double longitude, string units, CancellationToken cancel = default)
    {
        string place = FormattableString.Invariant($"latitude={latitude:0.####}&longitude={longitude:0.####}");
        string url = "https://api.open-meteo.com/v1/forecast?" + place +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&forecast_days=7&timezone=auto" +
            (units == "C" ? "&wind_speed_unit=kmh" : "&temperature_unit=fahrenheit&wind_speed_unit=mph");
        return Http.GetStringAsync(url, cancel);
    }

    /// <summary>Reads a forecast response; throws <see cref="FormatException"/> if it doesn't look like one.</summary>
    public static Report Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement.GetProperty("current");
            var days = new List<Day>();
            if (doc.RootElement.TryGetProperty("daily", out var daily))
            {
                var dates = daily.GetProperty("time");
                var codes = daily.GetProperty("weather_code");
                var highs = daily.GetProperty("temperature_2m_max");
                var lows = daily.GetProperty("temperature_2m_min");
                daily.TryGetProperty("precipitation_probability_max", out var rain);
                for (int i = 0; i < dates.GetArrayLength(); i++)
                {
                    if (!DateOnly.TryParse(dates[i].GetString(), CultureInfo.InvariantCulture, out var date)) continue;
                    int? chance = rain.ValueKind == JsonValueKind.Array && i < rain.GetArrayLength() && rain[i].ValueKind == JsonValueKind.Number ? (int)Math.Round(rain[i].GetDouble()) : null;
                    days.Add(new Day(date, Number(codes[i]) is { } c ? (int)c : -1, Number(highs[i]) ?? double.NaN, Number(lows[i]) ?? double.NaN, chance));
                }
            }
            return new Report(
                Number(current.GetProperty("temperature_2m")) ?? double.NaN,
                current.TryGetProperty("apparent_temperature", out var feels) ? Number(feels) ?? double.NaN : double.NaN,
                current.TryGetProperty("weather_code", out var code) && Number(code) is { } n ? (int)n : -1,
                !current.TryGetProperty("is_day", out var isDay) || Number(isDay) != 0,
                current.TryGetProperty("wind_speed_10m", out var wind) ? Number(wind) ?? double.NaN : double.NaN,
                current.TryGetProperty("relative_humidity_2m", out var humidity) && Number(humidity) is { } h ? (int)Math.Round(h) : -1,
                days);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new FormatException("The weather service sent something unexpected.", ex);
        }
    }

    private static double? Number(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
