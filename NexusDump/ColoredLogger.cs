using System;

namespace NexusDump
{
    /// <summary>
    /// Colored logger for better user experience with visual feedback
    /// </summary>
    public static class ColoredLogger
    {
        public static void LogInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"ℹ️  {message}");
            Console.ResetColor();
        }

        public static void LogSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ {message}");
            Console.ResetColor();
        }

        public static void LogWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️  {message}");
            Console.ResetColor();
        }

        public static void LogError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ {message}");
            Console.ResetColor();
        }

        public static void LogDebug(string message)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"🐛 {message}");
            Console.ResetColor();
        }

        public static void LogProgress(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🔄 {message}");
            Console.ResetColor();
        }

        public static void LogDownload(string message)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"⬇️  {message}");
            Console.ResetColor();
        }

        public static void LogApiLimit(string message)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"📊 {message}");
            Console.ResetColor();
        }

        public static void LogHeader(string message)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public static void LogRateLimit(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"⏱️  {message}");
            Console.ResetColor();
        }
    }
}
