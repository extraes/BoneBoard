using DSharpPlus.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tomlet;
using Tomlet.Attributes;

namespace BoneBoard;

internal class PersistentData
{
    public static event Action? PersistentDataChanged;
    public static PersistentData values;
    private const string PD_PATH = "./persistentData.toml";
    public string logPath = "./logs/";

    public Dictionary<ulong, TimeSpan> frogRoleTimes = new();

    static PersistentData()
    {
        Console.WriteLine("Initializing persistent data storage");
        if (!File.Exists(PD_PATH))
        {
            File.WriteAllText(PD_PATH, TomletMain.TomlStringFrom(new PersistentData())); // mmm triple parenthesis, v nice
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => WritePersistentData();

        ReadPersistentData();
        WritePersistentData();
    }

    public static void OutputRawTOML()
    {
        Console.WriteLine(File.ReadAllText(PD_PATH));
    }

    [MemberNotNull(nameof(values))]
    public static void ReadPersistentData()
    {
        string configText = File.ReadAllText(PD_PATH);
        values = TomletMain.To<PersistentData>(configText);
    }

    public static void WritePersistentData()
    {
        File.WriteAllText(PD_PATH, TomletMain.TomlStringFrom(values));
        PersistentDataChanged?.Invoke();
    }
}
