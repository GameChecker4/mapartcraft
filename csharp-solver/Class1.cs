namespace CsharpSolver;

public static class Class1
{
    public static async Task HelloWorld(Func<float, Task> yieldCallback)
    {
        Console.WriteLine("Hello from CsharpSolver!");

        const int seconds = 10;
        const int yieldsPerSecond = 10;

        int timesYielded = 0;
        const int maxYields = seconds * yieldsPerSecond;

        Console.WriteLine($"CsharpSolver busy for {seconds} seconds with Yield {yieldsPerSecond} times per second");
        for (int sec = 0; sec < seconds; sec++)
        {
            Console.WriteLine($"Sec {sec}");
            for (int i = 0; i < yieldsPerSecond; i++)
            {
                await yieldCallback((float) timesYielded++ / maxYields);
                Thread.Sleep(1000 /  yieldsPerSecond);
            }
        }
        Console.WriteLine("Goodbye from CsharpSolver!");
    }
}