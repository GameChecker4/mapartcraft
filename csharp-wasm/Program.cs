using System.Runtime.InteropServices.JavaScript;
using CsharpSolver;

namespace CsharpWasm;

public static partial class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Csharp Main!");
    }

    [JSExport]
    private static async Task HelloWorld([JSMarshalAs<JSType.Function<JSType.Number>>] Action<float> progressCallback)
    {
        Console.WriteLine("Hello from C# in wasm!");

        await Class1.HelloWorld(async progress =>
        {
            progressCallback(progress);
            await Task.Yield();
        });
    }
}