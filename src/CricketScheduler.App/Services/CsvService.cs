using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace CricketScheduler.App.Services;

public sealed class CsvService
{
    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        TrimOptions = TrimOptions.Trim
    };

    public async Task<List<T>> ReadAsync<T>(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, Config);
        return csv.GetRecords<T>().ToList();
    }

    public async Task WriteAsync<T>(string path, IEnumerable<T> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream);
        await using var csv = new CsvWriter(writer, Config);
        await csv.WriteRecordsAsync(rows);
    }
}
