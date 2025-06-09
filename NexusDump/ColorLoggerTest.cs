using System;

namespace NexusDump;

class ColorLoggerTest
{
    static void TestLogs(string[] args)
    {
        Console.WriteLine("Testing Colored Logger:");
        Console.WriteLine("======================");

        ColoredLogger.LogHeader("This is a header message");
        ColoredLogger.LogInfo("This is an info message");
        ColoredLogger.LogSuccess("This is a success message");
        ColoredLogger.LogWarning("This is a warning message");
        ColoredLogger.LogError("This is an error message");
        ColoredLogger.LogDebug("This is a debug message");
        ColoredLogger.LogProgress("This is a progress message");
        ColoredLogger.LogDownload("This is a download message");
        ColoredLogger.LogApiLimit("This is an API limit message");
        ColoredLogger.LogRateLimit("This is a rate limit message");

        Console.WriteLine("\nColored logging test completed!");
    }
}
