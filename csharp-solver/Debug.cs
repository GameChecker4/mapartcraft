using System;

namespace MapartSolving
{
    public static class Debug
    {
        public static ILogger? Logger { get; set; }

        public static void Log(string message)
        {
            Logger?.Log(message);
        }

        public static void LogWarning(string message)
        {
            Logger?.LogWarning(message);
        }

        public static void LogError(string message)
        {
            Logger?.LogError(message);
        }

        public static void LogException(Exception e)
        {
            Logger?.LogException(e);
        }

        public static void Assert(bool condition)
        {
            Logger?.Assert(condition);
        }
    }
}