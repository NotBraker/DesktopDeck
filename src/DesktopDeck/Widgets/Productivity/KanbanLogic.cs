using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace DesktopDeck.Widgets;
public static class KanbanLogic
{
    public sealed class Card { public string Id { get; set; } = ""; public string Text { get; set; } = ""; public int Column { get; set; } public string Tag { get; set; } = ""; public DateTime? Completed { get; set; } }
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static List<Card> Parse(string? json, int columns) { try { var cards = JsonSerializer.Deserialize<List<Card>>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json) ?? new(); cards = cards.Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Text)).Take(300).ToList(); foreach (var card in cards) { card.Id = string.IsNullOrEmpty(card.Id) ? Guid.NewGuid().ToString("N") : card.Id; card.Text = card.Text[..Math.Min(300, card.Text.Length)]; card.Column = Math.Clamp(card.Column, 0, Math.Max(0, columns - 1)); card.Tag ??= ""; } return cards; } catch (JsonException) { return new(); } }
    public static string Serialize(List<Card> cards) => JsonSerializer.Serialize(cards, Json);
    public static string[] Columns(IEnumerable<string> lines) { var names = lines.Select(x => x.Trim()).Where(x => x.Length > 0).Take(6).Select(x => x[..Math.Min(40, x.Length)]).ToArray(); return names.Length == 0 ? ["To do", "Doing", "Done"] : names; }
    public static void Move(Card card, int column, int count, DateTime now) { int next = Math.Clamp(column, 0, count - 1); if (next == card.Column) return; card.Column = next; card.Completed = next == count - 1 ? now : null; }
    public static bool Visible(Card card, int lastColumn, int days, DateTime now) => days <= 0 || card.Column != lastColumn || card.Completed is null || card.Completed >= now.AddDays(-days);
}
