using AlacrityTerraria.UserInterface;
using Xunit;

public sealed class PluginSearchTextBufferTests
{
    [Fact]
    public void WordDeletionAndNavigation_UseTheSearchBufferWithoutNativeChatInput()
    {
        var buffer = new PluginSearchTextBuffer();
        Assert.True(buffer.Insert("British English"));

        buffer.MoveEnd(false);
        Assert.True(buffer.Backspace(byWord: true));
        Assert.Equal("British ", buffer.Text);

        buffer.MoveHome(false);
        Assert.True(buffer.Delete(byWord: true));
        Assert.Equal(" ", buffer.Text);
    }

    [Fact]
    public void SelectionAndInsertion_ReplaceOnlyTheSelectedSearchText()
    {
        var buffer = new PluginSearchTextBuffer();
        Assert.True(buffer.Insert("Portuguese Brazil"));
        buffer.MoveHome(false);
        buffer.MoveRight(byWord: true, extendSelection: true);
        Assert.True(buffer.Insert("Spanish"));

        Assert.Equal("Spanish Brazil", buffer.Text);
        Assert.Equal("Spanish".Length, buffer.Caret);
    }

    [Fact]
    public void SearchLength_IsBoundedAndSelectAllDeletesEverything()
    {
        var buffer = new PluginSearchTextBuffer();
        Assert.True(buffer.Insert(new string('a', 64)));
        Assert.Equal(48, buffer.Text.Length);

        buffer.SelectAll();
        Assert.True(buffer.Delete(byWord: false));
        Assert.Equal(string.Empty, buffer.Text);
    }

    [Fact]
    public void SelectionPresentation_TracksTheVisibleRangeAfterCaretMovement()
    {
        var buffer = new PluginSearchTextBuffer();
        Assert.True(buffer.Insert("Japanese"));
        buffer.MoveHome(false);
        buffer.MoveRight(byWord: false, extendSelection: true);
        buffer.MoveRight(byWord: false, extendSelection: true);

        Assert.True(buffer.TryGetSelection(out int start, out int end));
        Assert.Equal(0, start);
        Assert.Equal(2, end);
    }
}
