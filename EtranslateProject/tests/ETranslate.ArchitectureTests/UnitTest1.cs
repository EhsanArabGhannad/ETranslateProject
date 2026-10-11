using System.Xml.Linq;

namespace ETranslate.ArchitectureTests;

public sealed class ServiceBoundaryTests
{
    [Fact]
    public void Business_mutation_route_groups_must_include_subscription_enforcement()
    {
        var root = FindRepositoryRoot();
        var mutationFiles = 0;
        foreach (var service in new[] { "TranslationWorkflow", "Documents", "Trust" })
        {
            var directory = Path.Combine(root.FullName, "src", "Services", service, $"ETranslate.{service}.Api", "Endpoints");
            foreach (var file in Directory.GetFiles(directory, "*.cs"))
            {
                var source = File.ReadAllText(file);
                if (!new[] { ".MapPost(", ".MapPut(", ".MapPatch(", ".MapDelete(" }.Any(source.Contains)) continue;
                mutationFiles++;
                Assert.Contains("group.AddEndpointFilter<SubscriptionWriteFilter>()", source);
            }
        }
        Assert.True(mutationFiles >= 5);
    }

    [Fact]
    public void Services_DoNotReferenceOtherServiceProjectsDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var servicesRoot = Path.Combine(repositoryRoot.FullName, "src", "Services");
        var serviceProjects = Directory.GetFiles(servicesRoot, "*.csproj", SearchOption.AllDirectories);

        Assert.NotEmpty(serviceProjects);

        foreach (var projectPath in serviceProjects)
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var document = XDocument.Load(projectPath);
            var references = document.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(path => !string.IsNullOrWhiteSpace(path));

            foreach (var reference in references)
            {
                var referencedPath = Path.GetFullPath(reference!, projectDirectory);
                Assert.False(
                    referencedPath.StartsWith(servicesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    $"Service project '{projectPath}' directly references another service: '{referencedPath}'.");
            }
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ETranslate.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new DirectoryNotFoundException("Could not find ETranslate.slnx.");
    }
}
