using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class NoteDiscoveryEngineTests
{
    [Fact] public void ShuffleBagShowsEveryNoteBeforeRepeating()
    {
        var engine = new NoteDiscoveryEngine(new Random(42)); engine.Update(["a", "b", "c"]);
        var first = engine.Next(null)!; var second = engine.Next(first)!; var third = engine.Next(second)!;
        Assert.Equal(3, new[] { first, second, third }.Distinct().Count());
    }

    [Fact] public void PreviousReturnsTheEarlierRandomNote()
    {
        var engine = new NoteDiscoveryEngine(new Random(42)); engine.Update(["a", "b", "c"]);
        var first = engine.Next(null)!; _ = engine.Next(first);
        Assert.Equal(first, engine.Previous());
    }

    [Fact] public void DailyChoiceIsStableForTheSameLibraryAndDate()
    {
        var engine = new NoteDiscoveryEngine(); engine.Update(["n3", "n1", "n2"]);
        Assert.Equal(engine.Daily(new(2026, 9, 14)), engine.Daily(new(2026, 9, 14)));
    }

    [Fact] public void RandomAvoidsTheSameBookWhenAnotherBookIsAvailable()
    {
        var engine = new NoteDiscoveryEngine(new Random(1));
        engine.Update([KeyValuePair.Create("a1", "book-a"), KeyValuePair.Create("a2", "book-a"), KeyValuePair.Create("b1", "book-b")]);
        var next = engine.Next("a1");
        Assert.Equal("b1", next);
    }
}
