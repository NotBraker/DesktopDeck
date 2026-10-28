namespace DesktopDeck.Models;

/// <summary>A Google Tasks list (what the "Sync with Google Tasks" picker offers). Saved in settings.json (no secrets).</summary>
public sealed class GoogleTaskListChoice
{
    /// <summary>The list id, or "@default" for the account's main list ("My Tasks").</summary>
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>One task as Google Tasks has it (a tasks resource), reduced to what Desktop Deck uses.</summary>
public sealed record GoogleTask
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>status == "completed".</summary>
    public bool Completed { get; init; }
    /// <summary>When Google says the task last changed (the "updated" field).</summary>
    public DateTimeOffset Updated { get; init; }
    /// <summary>When the task was completed, if it was.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
    public string ETag { get; init; } = "";
    /// <summary>The task was deleted (only returned when asked for with showDeleted).</summary>
    public bool Deleted { get; init; }
    /// <summary>Completed tasks that were cleared in Google's own app.</summary>
    public bool Hidden { get; init; }
    /// <summary>The task's parent task id; Desktop Deck shows sub-tasks as ordinary tasks.</summary>
    public string Parent { get; init; } = "";
}

/// <summary>One Gmail message, reduced to metadata (never the body). Read-only: nothing here is ever sent back to Google.</summary>
public sealed record GmailMessage
{
    public string Id { get; init; } = "";
    public string ThreadId { get; init; } = "";
    /// <summary>The From header decoded: the display name, or the address when there is no name.</summary>
    public string SenderName { get; init; } = "";
    public string SenderEmail { get; init; } = "";
    public string Subject { get; init; } = "";
    /// <summary>When the message arrived (Gmail's internalDate, else the Date header); null if neither was readable.</summary>
    public DateTimeOffset? Date { get; init; }
    public string Snippet { get; init; } = "";
    public bool Unread { get; init; }
    /// <summary>Gmail's label ids (INBOX, UNREAD, CATEGORY_PROMOTIONS, Label_12 …).</summary>
    public List<string> LabelIds { get; init; } = new();
}

/// <summary>An unread count plus Gmail's own label name list (for label colors).</summary>
public sealed record GmailLabel(string Id, string Name);

/// <summary>A Google Classroom course the account is a student in.</summary>
public sealed record ClassroomCourse
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Section { get; init; } = "";
    public string Link { get; init; } = "";
}

/// <summary>The student's own state for one assignment.</summary>
public enum SubmissionState
{
    /// <summary>The state could not be read (the submissions permission is missing or the call failed).</summary>
    Unknown,
    /// <summary>Nothing handed in yet (NEW, CREATED or RECLAIMED_BY_STUDENT).</summary>
    Open,
    TurnedIn,
    /// <summary>The teacher returned it (graded or not).</summary>
    Returned,
}

/// <summary>One Classroom assignment or question, with its due moment in local time.</summary>
public sealed record ClassroomWork
{
    public string Id { get; init; } = "";
    public string CourseId { get; init; } = "";
    public string CourseName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>The assignment's page on classroom.google.com.</summary>
    public string Link { get; init; } = "";
    /// <summary>Local due date (and time, if Classroom gave one); null when there is no due date.</summary>
    public DateTime? Due { get; init; }
    /// <summary>True when Classroom gave a time of day (otherwise only the date counts).</summary>
    public bool HasDueTime { get; init; }
    public double? MaxPoints { get; init; }
    public SubmissionState State { get; init; }
    /// <summary>Handed in after the due moment.</summary>
    public bool Late { get; init; }
    /// <summary>ASSIGNMENT, SHORT_ANSWER_QUESTION or MULTIPLE_CHOICE_QUESTION.</summary>
    public string WorkType { get; init; } = "";
}
