using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard;

internal struct LogType
{
    public string name = "???";
    public bool logCaller = false;
    public bool writeToFile = true;
    public ConsoleColor nameColor = ConsoleColor.Blue;
    public ConsoleColor textColor = ConsoleColor.White;

    public static readonly LogType Normal = new()
    {
        name = "Normal",
    };
    public static readonly LogType Debug = new()
    {
        name = "Debug",
        logCaller = true,
        writeToFile = true,
        nameColor = ConsoleColor.Gray,
        textColor = ConsoleColor.DarkGray
    };
    public static readonly LogType Trace = new()
    {
        name = "Trace",
        logCaller = true,
        writeToFile = false,
        nameColor = ConsoleColor.DarkGray,
        textColor = ConsoleColor.DarkGray
    };
    public static readonly LogType Warn = new()
    {
        name = "Warning",
        logCaller = true,
        writeToFile = true,
        nameColor = ConsoleColor.DarkYellow,
        textColor = ConsoleColor.Yellow
    };
    public static readonly LogType Fatal = new()
    {
        name = "Fatal",
        logCaller = true,
        writeToFile = true,
        nameColor = ConsoleColor.DarkRed,
        textColor = ConsoleColor.Red
    };

    public LogType() { }
}
