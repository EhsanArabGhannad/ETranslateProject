using System.Xml.Linq;

namespace ETranslate.ArchitectureTests;

public sealed class ServiceBoundaryTests
{
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
