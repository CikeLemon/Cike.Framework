namespace Cike.EventBus;

public abstract record Event : IEvent, IBackgroundEvent
{
    private string _id;
    public DateTime _createTime;
    private bool _isBackground;

    public Event()
    {
        _id = Guid.NewGuid().ToString();
        _createTime = DateTime.Now;
    }

    public string GetEventId() => _id;

    public void SetEventId(string id) => _id = id;

    public DateTime GetCreationTime() => _createTime;

    public DateTime SetCreationTime(DateTime creationTime) => _createTime = creationTime;

    public virtual bool IsBackgroundThread()
    {
        return _isBackground;
    }

    public virtual void EnableBackgroundThread()
    {
        _isBackground = true;
    }
}

public abstract record Event<TResult> : Event, IEvent<TResult>
{
    public TResult? Result { get; set; }

    public Event() : base() { }
}