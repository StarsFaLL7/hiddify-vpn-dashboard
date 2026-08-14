using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Упаковка записей снимка подписки. Ответы провайдеров — сотни килобайт,
/// поэтому в БД они лежат сжатыми.
/// </summary>
public static class SnapshotContent
{
    public static byte[] Compress(List<JsonNode> entries)
    {
        var json = new JsonArray(entries.Select(e => e.DeepClone()).ToArray()).ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(bytes);
        return output.ToArray();
    }

    public static List<JsonNode> Decompress(byte[]? gzipped)
    {
        if (gzipped is null || gzipped.Length == 0)
            return [];

        using var input = new MemoryStream(gzipped);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        return JsonNode.Parse(reader.ReadToEnd()) is JsonArray array
            ? array.Where(e => e is not null).Select(e => e!.DeepClone()).ToList()
            : [];
    }
}
