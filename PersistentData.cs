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
    private const string PD_PATH = "./persistentData.json";

    public Dictionary<ulong, TimeSpan> frogRoleTimes = new();

    static PersistentData()
    {
        Console.WriteLine("Initializing persistent data storage");
        if (!File.Exists(PD_PATH))
        {
            File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(new PersistentData())); // mmm triple parenthesis, v nice
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => WritePersistentData();

        ReadPersistentData();
        WritePersistentData();
    }

    public static void OutputRawJSON()
    {
        Console.WriteLine(File.ReadAllText(PD_PATH));
    }

    [MemberNotNull(nameof(values))]
    public static void ReadPersistentData()
    {
        string configText = File.ReadAllText(PD_PATH);
        values = JsonConvert.DeserializeObject<PersistentData>(configText) ?? new PersistentData();
        Logger.Put($"Read {values.frogRoleTimes.Count} frog role times from disk.", LogType.Debug);

    }

    public static void WritePersistentData()
    {
        Logger.Put($"Writing {values.frogRoleTimes.Count} frog role times to disk.", LogType.Debug);
        File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(values));
        PersistentDataChanged?.Invoke();
    }
}
