using System.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using VisionCore.Domain;
using VisionCore.Application;
using VisionCore.Infrastructure;
using Xunit;

namespace VisionCore.Tests.Architecture;

public class CleanArchitectureTests
{
    [Fact]
    public void Domain_Should_Not_Depend_On_Other_Layers()
    {
        var typeList = Types.InAssembly(typeof(DomainAssemblyMarker).Assembly);

        var res1 = typeList.ShouldNot().HaveDependencyOn("VisionCore.Application").GetResult();
        res1.IsSuccessful.Should().BeTrue();

        var res2 = typeList.ShouldNot().HaveDependencyOn("VisionCore.Infrastructure").GetResult();
        res2.IsSuccessful.Should().BeTrue();

        var res3 = typeList.ShouldNot().HaveDependencyOn("VisionCore.Console").GetResult();
        res3.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure_Or_Console()
    {
        var typeList = Types.InAssembly(typeof(ApplicationAssemblyMarker).Assembly);

        var res1 = typeList.ShouldNot().HaveDependencyOn("VisionCore.Infrastructure").GetResult();
        res1.IsSuccessful.Should().BeTrue();

        var res2 = typeList.ShouldNot().HaveDependencyOn("VisionCore.Console").GetResult();
        res2.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_Console()
    {
        var result = Types.InAssembly(typeof(InfrastructureAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("VisionCore.Console")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_Types_Should_Be_Sealed_Or_Record()
    {
        var assembly = typeof(DomainAssemblyMarker).Assembly;
        var nonCompliant = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsSealed)
            .Where(t => t.GetMethod("PrintMembers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) == null)
            .ToList();

        nonCompliant.Should().BeEmpty();
    }

    [Fact]
    public void Domain_Should_Not_Reference_ILogger()
    {
        var result = Types.InAssembly(typeof(DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Extensions.Logging")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
