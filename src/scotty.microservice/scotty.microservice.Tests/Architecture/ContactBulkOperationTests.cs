using System.Text.RegularExpressions;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Architecture;

/// <summary>A bulk operation bypasses the change tracker, so the birthday projector and its guard
/// with it (spec, décision 4): none may target the contacts.</summary>
public class ContactBulkOperationTests
{
    [Fact]
    public void No_bulk_operation_touches_contacts_behind_the_projector()
    {
        // bin/Debug/<tfm> of the test project, which sits inside src/scotty.microservice/.
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        Assert.True(File.Exists(Path.Combine(root, "scotty.microservice.core.csproj")), $"Wrong root: {root}");

        var separator = Path.DirectorySeparatorChar;
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Tests") && !f.Contains($"{separator}obj{separator}") && !f.Contains($"{separator}bin{separator}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f),
                @"Contacts\s*(\.\s*\w+\s*\([^;]*\))*\s*\.\s*Execute(Delete|Update)"))
            .ToList();

        Assert.Empty(offenders);
    }
}
