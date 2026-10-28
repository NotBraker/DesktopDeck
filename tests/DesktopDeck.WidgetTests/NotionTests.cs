using System.Globalization;
using System.Text.Json;
using DesktopDeck.Widgets;

public static class NotionTests
{
    private const string SchemaJson = """
        {"object":"database","title":[{"plain_text":"Launch "},{"plain_text":"plan"}],"properties":{
          "Name":{"id":"title","type":"title","title":{}},
          "Status":{"id":"s","type":"status","status":{
            "options":[{"id":"a","name":"Not started","color":"default"},{"id":"b","name":"In progress","color":"blue"},{"id":"c","name":"Shipped","color":"green"},{"id":"d","name":"Dropped","color":"gray"}],
            "groups":[{"id":"g1","name":"To-do","option_ids":["a"]},{"id":"g2","name":"In progress","option_ids":["b"]},{"id":"g3","name":"Complete","option_ids":["c","d"]}]}},
          "Created":{"id":"x","type":"created_time","created_time":{}},
          "Start":{"id":"y","type":"date","date":{}},
          "Due":{"id":"z","type":"date","date":{}}}}
        """;

    public static void DatabaseIdFromEveryKindOfLink()
    {
        const string dashed = "0123abcd-4567-89ab-cdef-0123456789ab";
        Expect.Equal(dashed, NotionLogic.ExtractId("0123abcd456789abcdef0123456789ab")!, "bare 32-character id");
        Expect.Equal(dashed, NotionLogic.ExtractId("0123ABCD-4567-89AB-CDEF-0123456789AB")!, "dashed id, any case");
        Expect.Equal(dashed, NotionLogic.ExtractId("https://www.notion.so/My-Workspace/Tasks-0123abcd456789abcdef0123456789ab?v=aaaabbbbccccddddeeeeffff00001111&pvs=4")!, "the page id, not the view id");
        Expect.Equal(dashed, NotionLogic.ExtractId("https://www.notion.so/0123abcd456789abcdef0123456789ab?v=aaaabbbbccccddddeeeeffff00001111")!, "link without a title");
        Expect.Equal(dashed, NotionLogic.ExtractId("  notion.so/Dead-Beef-0123abcd456789abcdef0123456789ab  ")!, "hex-looking words before the id don't confuse it");
        Expect.True(NotionLogic.ExtractId("https://www.notion.so/My-Workspace/Tasks") is null, "no id in the link");
        Expect.True(NotionLogic.ExtractId("0123abcd456789abcdef0123456789a") is null, "31 characters");
        Expect.True(NotionLogic.ExtractId("") is null && NotionLogic.ExtractId(null) is null, "empty");
    }

    public static void SchemaDetection()
    {
        var schema = NotionLogic.DetectSchema(SchemaJson);
        Expect.Equal("Launch plan", schema.DatabaseTitle, "database title");
        Expect.Equal("Name", schema.TitleProp, "title property");
        Expect.Equal("Status", schema.StatusProp, "status property");
        Expect.Equal("status", schema.StatusType, "status type");
        Expect.Equal("Due", schema.DateProp, "a date called Due is preferred over the first date");
        Expect.Equal("Shipped,Dropped", string.Join(",", schema.DoneNames), "the Complete group is done");
        Expect.Equal(4, schema.Options.Count, "options in database order");
        Expect.Equal("Start", NotionLogic.DetectSchema(SchemaJson, null, "start").DateProp, "your own date property wins, case-insensitive");
        Expect.Equal("Due", NotionLogic.DetectSchema(SchemaJson, null, "Missing").DateProp, "an unknown name falls back to detection");
    }

    public static void SelectAndCheckboxStatusDetection()
    {
        var select = NotionLogic.DetectSchema("""{"properties":{"Task":{"type":"title"},"Stage":{"type":"select","select":{"options":[{"name":"Todo","color":"red"},{"name":"Done ✅","color":"green"}]}},"Priority":{"type":"select","select":{"options":[]}}}}""");
        Expect.True(select.StatusProp == "Stage" && select.StatusType == "select", "a select called Stage is the status");
        Expect.Equal("Done ✅", string.Join(",", select.DoneNames), "done-looking option");
        var box = NotionLogic.DetectSchema("""{"properties":{"Task":{"type":"title"},"Done":{"type":"checkbox","checkbox":{}}}}""");
        Expect.True(box.StatusType == "checkbox" && box.StatusProp == "Done", "a Done checkbox works as status");
        var none = NotionLogic.DetectSchema("""{"properties":{"Task":{"type":"title"},"Notes":{"type":"rich_text"}}}""");
        Expect.True(none.StatusProp == "" && none.DateProp == "", "no status or date");
        Expect.Throws<FormatException>(() => NotionLogic.DetectSchema("""{"properties":{"Notes":{"type":"rich_text"}}}"""), "no title property");
        Expect.Throws<FormatException>(() => NotionLogic.DetectSchema("{}"), "no properties");
    }

    public static void QueryBodyHidesDoneAndSorts()
    {
        var schema = NotionLogic.DetectSchema(SchemaJson);
        using var body = JsonDocument.Parse(NotionLogic.BuildQuery(schema, true, "date-asc", 500));
        var root = body.RootElement;
        Expect.Equal(100, root.GetProperty("page_size").GetInt32(), "page size capped");
        var and = root.GetProperty("filter").GetProperty("and");
        Expect.Equal(2, and.GetArrayLength(), "one condition per done option");
        Expect.Equal("Shipped", and[0].GetProperty("status").GetProperty("does_not_equal").GetString()!, "status is not Shipped");
        Expect.Equal("Due", root.GetProperty("sorts")[0].GetProperty("property").GetString()!, "sorted by the date property");
        using var show = JsonDocument.Parse(NotionLogic.BuildQuery(schema, false, "edited", 5));
        Expect.True(!show.RootElement.TryGetProperty("filter", out _), "no filter when done items are shown");
        Expect.Equal("last_edited_time", show.RootElement.GetProperty("sorts")[0].GetProperty("timestamp").GetString()!, "edited order");
        var box = NotionLogic.DetectSchema("""{"properties":{"Task":{"type":"title"},"Done":{"type":"checkbox"}}}""");
        using var check = JsonDocument.Parse(NotionLogic.BuildQuery(box, true, "date-asc", 5));
        Expect.True(!check.RootElement.GetProperty("filter").GetProperty("checkbox").GetProperty("equals").GetBoolean(), "checkbox must be unchecked");
        Expect.Equal("last_edited_time", check.RootElement.GetProperty("sorts")[0].GetProperty("timestamp").GetString()!, "no date property falls back to edit order");
    }

    private const string Results = """
        {"object":"list","results":[
          {"id":"p1","url":"https://www.notion.so/Write-docs-1","archived":false,"last_edited_time":"2026-10-04T10:00:00.000Z","properties":{
            "Name":{"type":"title","title":[{"plain_text":"Write "},{"plain_text":"docs"}]},
            "Status":{"type":"status","status":{"name":"In progress","color":"blue"}},
            "Due":{"type":"date","date":{"start":"2026-10-06","end":null}}}},
          {"id":"p2","url":"https://www.notion.so/Ship-it-2","archived":false,"last_edited_time":"2026-10-03T10:00:00.000Z","properties":{
            "Name":{"type":"title","title":[{"plain_text":"Ship it"}]},
            "Status":{"type":"status","status":{"name":"Shipped","color":"green"}},
            "Due":{"type":"date","date":null}}},
          {"id":"p3","url":"https://www.notion.so/Idea-3","archived":false,"last_edited_time":"2026-10-05T10:00:00.000Z","properties":{
            "Name":{"type":"title","title":[]},"Status":{"type":"status","status":null},"Due":{"type":"date","date":{"start":"2026-10-01T09:30:00.000+00:00"}}}},
          {"id":"p4","url":"https://www.notion.so/Gone-4","archived":true,"properties":{"Name":{"type":"title","title":[{"plain_text":"Gone"}]}}}
        ]}
        """;

    public static void RowsAreParsed()
    {
        var schema = NotionLogic.DetectSchema(SchemaJson);
        var rows = NotionLogic.ParseRows(Results, schema);
        Expect.Equal(3, rows.Count, "archived pages are skipped");
        Expect.Equal("Write docs", rows[0].Title, "title joined");
        Expect.True(rows[0].Status == "In progress" && rows[0].StatusColor == "blue" && !rows[0].Done, "status and color");
        Expect.Equal("2026-10-06", rows[0].Date, "date");
        Expect.True(rows[1].Done && rows[1].Date == "", "shipped is done; a null date is empty");
        Expect.Equal("Untitled", rows[2].Title, "empty title");
        Expect.True(rows[2].Status == "" && !rows[2].Done, "a null status is empty");
        Expect.Throws<FormatException>(() => NotionLogic.ParseRows("""{"object":"error","status":404}""", schema), "an error object");
    }

    public static void VisibleRowsSortAndHideDone()
    {
        var schema = NotionLogic.DetectSchema(SchemaJson);
        var rows = NotionLogic.ParseRows(Results, schema);
        Expect.Equal("Untitled,Write docs", string.Join(",", NotionLogic.Visible(rows, true, "date-asc", 10).Select(r => r.Title)), "done hidden; earlier date first");
        Expect.Equal("Write docs,Untitled", string.Join(",", NotionLogic.Visible(rows, true, "date-desc", 10).Select(r => r.Title)), "latest date first");
        Expect.Equal("Untitled,Write docs,Ship it", string.Join(",", NotionLogic.Visible(rows, false, "edited", 10).Select(r => r.Title)), "recently edited first");
        Expect.Equal(1, NotionLogic.Visible(rows, false, "edited", 1).Count, "limited");
    }

    public static void GroupsFollowOptionOrder()
    {
        var schema = NotionLogic.DetectSchema(SchemaJson);
        var groups = NotionLogic.Group(NotionLogic.ParseRows(Results, schema), schema);
        Expect.Equal("In progress,Shipped,No status", string.Join(",", groups.Select(g => g.Name)), "option order, empty groups dropped, unset last");
        Expect.Equal("blue", groups[0].Color, "group color");
    }

    public static void NewPageBody()
    {
        var schema = NotionLogic.DetectSchema(SchemaJson);
        using var body = JsonDocument.Parse(NotionLogic.BuildNewPage("db-id", schema, "Call \"Sam\"", "Not started", new DateTime(2026, 10, 4)));
        var root = body.RootElement;
        Expect.Equal("db-id", root.GetProperty("parent").GetProperty("database_id").GetString()!, "parent");
        var props = root.GetProperty("properties");
        Expect.Equal("Call \"Sam\"", props.GetProperty("Name").GetProperty("title")[0].GetProperty("text").GetProperty("content").GetString()!, "title text is escaped by the serializer");
        Expect.Equal("Not started", props.GetProperty("Status").GetProperty("status").GetProperty("name").GetString()!, "starting status");
        Expect.Equal("2026-10-04", props.GetProperty("Due").GetProperty("date").GetProperty("start").GetString()!, "today's date");
        using var plain = JsonDocument.Parse(NotionLogic.BuildNewPage("db-id", schema, "Plain", null, null));
        Expect.True(!plain.RootElement.GetProperty("properties").TryGetProperty("Status", out _) && !plain.RootElement.GetProperty("properties").TryGetProperty("Due", out _), "only the title by default");
        string longTitle = new('x', 5000);
        using var clipped = JsonDocument.Parse(NotionLogic.BuildNewPage("db-id", schema, longTitle, null, null));
        Expect.True(clipped.RootElement.GetProperty("properties").GetProperty("Name").GetProperty("title")[0].GetProperty("text").GetProperty("content").GetString()!.Length <= 1900, "title clipped to Notion's limit");
    }

    public static void DateLabels()
    {
        var now = new DateTime(2026, 10, 5, 14, 0, 0);
        var culture = CultureInfo.GetCultureInfo("en-US");
        Expect.Equal(("Today", false, true), NotionLogic.DateLabel("2026-10-05", now, culture), "today");
        Expect.Equal(("Tomorrow", false, false), NotionLogic.DateLabel("2026-10-06", now, culture), "tomorrow");
        Expect.Equal(("Yesterday", true, false), NotionLogic.DateLabel("2026-10-04", now, culture), "yesterday is overdue");
        Expect.Equal(("Fri 9", false, false), NotionLogic.DateLabel("2026-10-09", now, culture), "within a week");
        Expect.Equal(("Oct 20", false, false), NotionLogic.DateLabel("2026-10-20", now, culture), "later this year");
        Expect.Equal(("Jan 3, 2027", false, false), NotionLogic.DateLabel("2027-01-03", now, culture), "next year shows the year");
        Expect.Equal(("", false, false), NotionLogic.DateLabel("", now, culture), "no date");
        Expect.True(NotionLogic.DateLabel("2026-10-01", now, culture).Overdue, "past dates are overdue");
        Expect.True(NotionLogic.ParseDate("2026-10-05T23:30:00.000+00:00") is not null, "date with time and offset");
        Expect.True(NotionLogic.ParseDate("soon") is null, "not a date");
    }

    public static void ErrorWording()
    {
        Expect.True(NotionLogic.Problem(401, """{"code":"unauthorized","message":"API token is invalid."}""").Contains("token", StringComparison.Ordinal), "401 says token");
        Expect.True(NotionLogic.Problem(404, """{"code":"object_not_found","message":"Could not find database"}""").Contains("Connections", StringComparison.Ordinal), "404 says how to share the database");
        Expect.True(NotionLogic.Problem(400, """{"code":"validation_error","message":"Status is expected to be status."}""").Contains("Status is expected", StringComparison.Ordinal), "validation message shown");
        Expect.True(NotionLogic.Problem(429, null).Contains("rate", StringComparison.OrdinalIgnoreCase), "rate limit");
        Expect.Equal("#ff7369", NotionLogic.ColorHex("red"), "red");
        Expect.Equal("#9b9b9b", NotionLogic.ColorHex("something-new"), "unknown colors fall back");
    }

    public static void CacheRoundTrip()
    {
        var schema = NotionLogic.DetectSchema(SchemaJson);
        var data = new NotionLogic.Data { Schema = schema, Rows = NotionLogic.ParseRows(Results, schema) };
        var back = NotionLogic.FromCache(NotionLogic.ToCache(data));
        Expect.Equal(3, back.Rows.Count, "rows survive");
        Expect.True(back.Schema.StatusProp == "Status" && back.Schema.DoneNames.Count == 2 && back.Schema.Options.Count == 4, "schema survives");
        Expect.Equal(0, NotionLogic.FromCache("{{").Rows.Count, "bad cache is empty");
    }
}
