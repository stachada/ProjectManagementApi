using BenchmarkDotNet.Running;
using Ordinis.Benchmarks.Infrastructure;

namespace Ordinis.Benchmarks;

/// <summary>
/// Entry point. Runs BenchmarkDotNet by default, or seeds the shared benchmark / load-test
/// database with the <c>seed</c> command.
/// </summary>
/// <remarks>
/// <code>
/// dotnet run -c Release --project tests/Ordinis.Benchmarks                       # interactive picker
/// dotnet run -c Release --project tests/Ordinis.Benchmarks -- --filter "*Mapping*"
/// dotnet run -c Release --project tests/Ordinis.Benchmarks -- seed               # prepare DB for k6
/// </code>
/// See <c>docs/BENCHMARKS.md</c>.
/// </remarks>
public static class BenchmarkEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("seed", StringComparison.OrdinalIgnoreCase))
        {
            return await SeedAsync();
        }

        BenchmarkSwitcher.FromAssembly(typeof(BenchmarkEntryPoint).Assembly).Run(args);
        return 0;
    }

    private static async Task<int> SeedAsync()
    {
        string connectionString = await BenchmarkDatabase.GetConnectionStringAsync();
        SeededDataset dataset = await BenchmarkDatabase.PrepareAsync(connectionString);

        Console.WriteLine();
        Console.WriteLine($"Benchmark dataset ready: {dataset.TaskCount:N0} tasks in project {dataset.ProjectId}.");
        Console.WriteLine();
        Console.WriteLine("Point the API at it with:");
        Console.WriteLine($"  ConnectionStrings__DefaultConnection={connectionString}");
        Console.WriteLine();
        Console.WriteLine("Log in with:");
        Console.WriteLine($"  organizationSlug: {SeededDataset.OrganizationSlug}");
        Console.WriteLine($"  email:            {SeededDataset.AdminEmail}");
        Console.WriteLine($"  password:         {SeededDataset.AdminPassword}");
        return 0;
    }
}
