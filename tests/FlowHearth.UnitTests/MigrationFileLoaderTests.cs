using System.Text;
using FlowHearth.Infrastructure.Database.Migrations;

namespace FlowHearth.UnitTests;

public sealed class MigrationFileLoaderTests
{
    [Fact]
    public void LoadReturnsOrdinallySortedMigrationsWithSha256Checksums()
    {
        using var directory = new TemporaryMigrationDirectory();
        directory.Write("0002_second_step.sql", "SELECT 2;\n");
        directory.Write("0001_first_step.sql", "SELECT 1;\n");

        var migrations = new MigrationFileLoader().Load(directory.Path);

        Assert.Equal(2, migrations.Count);
        Assert.Equal("0001_first_step", migrations[0].Id);
        Assert.Equal("0002_second_step", migrations[1].Id);
        Assert.All(migrations, migration => Assert.Equal(64, migration.Checksum.Length));
        Assert.Equal("SELECT 1;\n", migrations[0].Sql);
    }

    [Fact]
    public void LoadRejectsInvalidFilename()
    {
        using var directory = new TemporaryMigrationDirectory();
        directory.Write("01_Invalid.sql", "SELECT 1;");

        var exception = Assert.Throws<MigrationException>(
            () => new MigrationFileLoader().Load(directory.Path));

        Assert.Contains("Invalid migration filename", exception.Message);
    }

    [Fact]
    public void LoadRejectsEmptyMigration()
    {
        using var directory = new TemporaryMigrationDirectory();
        directory.Write("0001_empty.sql", string.Empty);

        var exception = Assert.Throws<MigrationException>(
            () => new MigrationFileLoader().Load(directory.Path));

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryMigrationDirectory : IDisposable
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new(false);

        public TemporaryMigrationDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FlowHearthTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string filename, string content)
        {
            File.WriteAllText(
                System.IO.Path.Combine(Path, filename),
                content,
                Utf8WithoutBom);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }

            GC.SuppressFinalize(this);
        }
    }
}
