using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapartSolving;

namespace CsharpWasm;

public static partial class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Csharp Main!");
    }

    [JSExport]
    private static async Task<string> HelloWorld(
        string solverParamsJson,
        [JSMarshalAs<JSType.Function<JSType.Number>>] Action<float> progressCallback)
    {
        try
        {
            var solverParams = JsonSerializer.Deserialize<SolverParams>(solverParamsJson, AppJsonSerializerContext.Default.SolverParams);
            if (solverParams == null)
                throw new Exception($"{nameof(solverParamsJson)} can not be deserialized");

            MapartSolver.Multithreaded = false;
            var solver = new MapartSolver();
            var yieldTimer = Stopwatch.StartNew();
            var minYieldTimeout = TimeSpan.FromMilliseconds(200);
            solver.SetYieldCallback(async () =>
            {
                if (yieldTimer.Elapsed >= minYieldTimeout)
                {
                    yieldTimer.Restart();
                    progressCallback.Invoke(1f - (float) solver.Status.RemainingEdges / solver.Status.InitialEdges);
                    await Task.Yield();
                }
            });
            var graph = await solver.Solve(GridUtils.Cast(GridUtils.FromNestedArray(solverParams.Directions), d => (Direction) d), CancellationToken.None);
            var heightMap = graph.ComputeHeights();

            var result = new SolverResult(GridUtils.ToNestedArray(heightMap));
            return JsonSerializer.Serialize(result, AppJsonSerializerContext.Default.SolverResult);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private record SolverParams(int[][] Directions);

    private record SolverResult(int[][] HeightMap);

    [JsonSerializable(typeof(SolverParams))]
    [JsonSerializable(typeof(SolverResult))]
    private partial class AppJsonSerializerContext : JsonSerializerContext;
}