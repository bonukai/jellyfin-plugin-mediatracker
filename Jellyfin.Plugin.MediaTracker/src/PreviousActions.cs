
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Entities;

public class PreviousAction
{
    private const double minimumProgressChangeToForceUpdate = 0.01;
    private const long minimumNumberOfSecondsBetweenProgressUpdate = 30;
    public PreviousAction(string action, float progress)
    {
        this.action = action;
        this.progress = progress;
        this.date = DateTime.Now;
    }

    public string action { get; }
    public float progress { get; }
    public DateTime date { get; }

    public bool IsWithinTimeLimit()
    {
        return DateTime.Now.Subtract(this.date).TotalSeconds < minimumNumberOfSecondsBetweenProgressUpdate;
    }

    public bool ShouldSkip(string action, float progress)
    {
        // Update if action changed
        if (this.action != action)
        {
            return false;
        }

        // Skip if action and progress is the same
        if (this.progress == progress)
        {
            return true;
        }

        // Skip if progress changed significantly
        if (Math.Abs(this.progress - progress) >= minimumProgressChangeToForceUpdate)
        {
            return false;
        }


        if (DateTime.Now.Subtract(this.date).TotalSeconds >= minimumNumberOfSecondsBetweenProgressUpdate)
        {
            return false;
        }

        return true;
    }
}

public class PreviousActions
{
    public PreviousActions()
    {
        progressDictionary = new Dictionary<Tuple<Guid, Guid>, PreviousAction>();
        markedAsSeenHistory = new Dictionary<Tuple<Guid, Guid>, DateTime>();
    }

    public void Cleanup()
    {
        progressDictionary = progressDictionary.Where(item => item.Value.IsWithinTimeLimit()).ToDictionary(p => p.Key, p => p.Value);

        markedAsSeenHistory = markedAsSeenHistory.Where(item => DateTime.Now.Subtract(item.Value).TotalHours < 12).ToDictionary(p => p.Key, p => p.Value);
    }

    public bool ShouldSkipAction(User user, Video video, float progress, string action)
    {
        var id = Tuple.Create(user.Id, video.Id);

        PreviousAction? res;

        if (progressDictionary.TryGetValue(id, out res))
        {
            if (res.ShouldSkip(action, progress))
            {
                return true;
            }
        }

        progressDictionary.Remove(id);
        progressDictionary.Add(id, new PreviousAction(action, progress));

        return false;
    }

    public bool CanMarkAsSeen(User user, Video video)
    {
        var id = Tuple.Create(user.Id, video.Id);

        DateTime res;

        if (markedAsSeenHistory.TryGetValue(id, out res))
        {
            return false;
        }

        markedAsSeenHistory.Add(id, DateTime.Now);

        return true;
    }

    private Dictionary<Tuple<System.Guid, System.Guid>, DateTime> markedAsSeenHistory;
    private Dictionary<Tuple<System.Guid, System.Guid>, PreviousAction> progressDictionary;
}
