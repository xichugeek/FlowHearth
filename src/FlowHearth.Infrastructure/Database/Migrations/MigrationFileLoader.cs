using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlowHearth.Infrastructure.Database.Migrations;

public sealed partial class MigrationFileLoader : IMigrationFileLoader
{
    public IReadOnlyList<MigrationDefinition> Load(string migrationsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsPath);

        var fullPath = Path.GetFullPath(migrationsPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Migration directory does not exist: {fullPath}");
        }

        var definitions = Directory
            .EnumerateFiles(fullPath, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(LoadFile)
            .OrderBy(migration => migration.Id, StringComparer.Ordinal)
            .ToArray();

        var duplicateId = definitions
            .GroupBy(migration => migration.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicateId is not null)
        {
            throw new MigrationException(
                $"Duplicate migration id detected: {duplicateId}");
        }

        return definitions;
    }

    private static MigrationDefinition LoadFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (!MigrationFileNameRegex().IsMatch(fileName))
        {
            throw new MigrationException(
                $"Invalid migration filename '{fileName}'. " +
                "Expected NNNN_lowercase_description.sql.");
        }

        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length == 0)
        {
            throw new MigrationException($"Migration file '{fileName}' is empty.");
        }

        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var sql = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(RemoveUtf8Bom(bytes));

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new MigrationException(
                $"Migration file '{fileName}' contains no executable content.");
        }

        return new MigrationDefinition(
            Path.GetFileNameWithoutExtension(fileName),
            Path.GetFullPath(filePath),
            checksum,
            sql);
    }

    private static byte[] RemoveUtf8Bom(byte[] bytes)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        return bytes.AsSpan().StartsWith(preamble)
            ? bytes[preamble.Length..]
            : bytes;
    }

    [GeneratedRegex("^[0-9]{4}_[a-z0-9]+(?:_[a-z0-9]+)*\\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationFileNameRegex();
}
