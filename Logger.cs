using CircularBuffer;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace BoneBoard;

internal static class Logger
{
    
    const string PUT_DATE_FORMAT = "hh:mm:sstt"; // example: "12:33:05AM"
    const string FILE_DATE_FORMAT = "yyyy-MM-dd h_m_stt"; // example: "2022-10-23 12_01_47AM"
    readonly static int maxPutDateLength = PUT_DATE_FORMAT.Length;
    internal static readonly CircularBuffer<string> logStatements = new(64);
    static readonly StreamWriter logFile;

    static Logger()
    {
        int maxFiles = Config.values.maxLogFiles;
        if (!Directory.Exists(Config.values.logPath)) Directory.CreateDirectory(Config.values.logPath);
        string filePath = Path.Combine(Config.values.logPath, DateTime.Now.ToString(FILE_DATE_FORMAT) + ".log");

        Console.WriteLine("Initializing logger");
        logFile = File.AppendText(filePath);
        Put("Created stream writer - logger now active, writing to " + filePath);

        // delete oldest log files over max log file count
        // https://stackoverflow.com/questions/20486559/get-a-list-of-files-in-a-directory-in-descending-order-by-creation-date-using-c#20486570
        DirectoryInfo dir = new(Config.values.logPath);
        FileInfo[] files = dir.GetFiles().OrderByDescending(f => f.LastWriteTime).ToArray();
        foreach (FileInfo file in files.Skip(maxFiles))
        {
            Put($"Deleting old log {file.Name}", LogType.Debug);
            file.Delete();
        }
        if (files.Length > maxFiles) Put($"Deleted {files.Length - maxFiles} old log file{(files.Length - maxFiles == 1 ? "" : "s")}.");
    }


    public static void Error(string str) => Put(str, LogType.Fatal, false);
    public static void Error(string str, Exception ex) => Put(str + "\n\t" + ex.ToString(), LogType.Fatal, false);

    public static void Warn(string str) => Put(str, LogType.Warn, false);
    public static void Warn(string str, Exception ex) => Put(str + "\n\t" + ex.ToString(), LogType.Warn, false);

    public static void Put(string str) => Put(str, LogType.Normal);

    public static void Put(string str, LogType reason, bool cleanMultiline = true)
    {
        if (string.IsNullOrWhiteSpace(str)) str = "<N/A>";
        else if (cleanMultiline) str = str.Replace("\r\n", " // ").Replace("\n", " // ");

        string starter = $"[{GetPutTime()}] ";
        string reasonStr = reason.logCaller ? $"{reason.name} @ {GetCaller()}" : reason.name;
        string fileString = $"{starter}{reasonStr} -> {str}";

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(starter);
        Console.ForegroundColor = reason.nameColor;
        Console.Write(reasonStr);
        Console.ForegroundColor = reason.textColor;
        Console.Write(" -> ");
        Console.WriteLine(str);

        Console.ResetColor();
        if (reason.writeToFile)
        {
            logFile.WriteLine($"{starter}{reasonStr} -> {str}");
            logFile.Flush();
        }
        logStatements.PushBack(fileString);
    }

    public static string EnsureShorterThan(string str, int maxLen)
    {
        if (str.Length < maxLen) return str;

        return str[..(maxLen - 3)] + "...";
    }

    static string GetPutTime() => DateTime.Now.ToString(PUT_DATE_FORMAT).PadLeft(maxPutDateLength);

    static string GetCaller()
    {
        StackTrace trace = new(2, true);
        foreach (StackFrame frame in trace.GetFrames())
        {
            MethodBase? method = frame.GetMethod();
            Type? decType = method?.DeclaringType;
            if (method is null || decType is null) continue;

            //if (decType != typeof(Logger) && decType != typeof(DiscordLogger) && IsTypeLoggable(decType))
            //{
            //    return decType.Name + '.' + method.Name;
            //}
        }
        return "???";
    }

    static bool IsTypeLoggable(Type t)
    {
        // all this to avoid seeing "<MainAsync>d__1.MoveNext"
        return t.GetCustomAttribute(typeof(CompilerGeneratedAttribute)) is null && t.Assembly == typeof(Logger).Assembly;
    }
}