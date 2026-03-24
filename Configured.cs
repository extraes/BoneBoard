namespace BoneBoard;

public class Configured<TValue>
{
    public TValue Value { get; private set; }
    private string binder;

    private Func<TValue> getter;
    private Func<string> binderRetriever;
    
    public Configured(Func<TValue> getter, Func<string> reloadWhenChanged)
    {
        this.getter = getter;
        this.binderRetriever = reloadWhenChanged;

        this.binder = reloadWhenChanged();
        Value = getter();
        Config.ConfigChanged += ConfigChanged;
    }

    private void ConfigChanged()
    {
        string newBinder = binderRetriever();
        if (newBinder == binder)
        {
            return;
        }

        Logger.Put($"Config changed, creating a new {typeof(TValue).FullName} -- binder went from '{binder}' to '{newBinder}'", LogType.Trace);
        Value = getter();
    }
}