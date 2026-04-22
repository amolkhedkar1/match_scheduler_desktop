using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.IO;

namespace CricketScheduler.App.Services;

public sealed class CsvService
{
    /// <summary>
    /// Lenient CsvHelper config: ignores missing fields (new columns added to DTO
    /// but not yet in an older CSV file) and ignores extra header columns.
    /// </summary>
    private static CsvConfiguration ReadConfig => new(CultureInfo.InvariantCulture)
    {
        MissingFieldFound   = null,   // silently default-construct missing fields
        HeaderValidated     = null,   // don't throw when a DTO property has no matching column
        BadDataFound        = null,   // skip malformed rows rather than crashing
        TrimOptions         = TrimOptions.Trim,
    };

    private static CsvConfiguration WriteConfig => new(CultureInfo.InvariantCulture)
    {
        TrimOptions = TrimOptions.Trim,
    };

    public async Task<List<T>> ReadAsync<T>(string path)
    {
        if (!File.Exists(path)) return [];

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, ReadConfig);
        return csv.GetRecords<T>().ToList();
    }

    public async Task WriteAsync<T>(string path, IEnumerable<T> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream);
        await using var csv = new CsvWriter(writer, WriteConfig);
        await csv.WriteRecordsAsync(rows);
    }
}
