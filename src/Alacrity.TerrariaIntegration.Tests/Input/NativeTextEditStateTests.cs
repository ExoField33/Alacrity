using Xunit;

namespace AlacrityTerraria.Input;

public sealed class NativeTextEditStateTests
{
    [Fact]
    public void InsertReplacesTheCurrentSelection()
    {
        var state = new NativeTextEditState();
        const string text = "alpha beta";
        state.Synchronize(text);

        state.MoveLeft(text, byWord: true, extendSelection: false);
        state.MoveRight(text, byWord: false, extendSelection: true);
        string edited = state.Insert(text, "X");

        Assert.Equal("alpha Xeta", edited);
        Assert.Equal(7, state.Caret);
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void WordDeletionUsesTheSameCaretBoundariesAsNavigation()
    {
        var state = new NativeTextEditState();
        const string text = "alpha beta";
        state.Synchronize(text);

        state.MoveLeft(text, byWord: true, extendSelection: false);
        string edited = state.Backspace(text, byWord: true);

        Assert.Equal("beta", edited);
        Assert.Equal(0, state.Caret);
    }

    [Fact]
    public void MovementKeepsTerrariaTagsAndSurrogatePairsAtomic()
    {
        var state = new NativeTextEditState();
        const string text = "A[i:42]\U0001f600B";
        state.Synchronize(text);

        state.MoveLeft(text, byWord: false, extendSelection: false);
        Assert.Equal(text.Length - 1, state.Caret);
        state.MoveLeft(text, byWord: false, extendSelection: false);
        Assert.Equal(7, state.Caret);
        state.MoveLeft(text, byWord: false, extendSelection: false);
        Assert.Equal(1, state.Caret);
    }

    [Fact]
    public void PlayerChatFormattingShowsCaretWithoutChangingInput()
    {
        var state = new NativeTextEditState();
        const string text = "chat";
        state.Synchronize(text);
        state.MoveHome(extendSelection: false);
        state.MoveRight(text, byWord: false, extendSelection: true);
        state.Complete(text);

        string formatted = state.FormatForPlayerChat(text, drawCaret: true);

        Assert.Equal("chat", text);
        Assert.Equal("[c/80B8FF:c]|hat", formatted);
        Assert.Contains("|", formatted);
        Assert.Contains("[c/80B8FF:", formatted);
    }

    [Fact]
    public void LegacyMenuFormattingMovesTerrariasExistingTickerToTheCaret()
    {
        var state = new NativeTextEditState();
        const string text = "address";
        state.Synchronize(text);
        state.MoveHome(extendSelection: false);
        state.MoveRight(text, byWord: false, extendSelection: false);
        state.MoveRight(text, byWord: false, extendSelection: false);
        state.Complete(text);

        Assert.Equal("ad|dress", state.FormatNativeDisplayText("address|"));
        Assert.Equal("ad dress", state.FormatNativeDisplayText("address "));
        Assert.Equal("**|*****", state.FormatNativeDisplayText("*******|"));
        Assert.Equal("other|", state.FormatNativeDisplayText("other|"));
    }

    [Fact]
    public void FocusedPresentationRetainsCaretAcrossHostTextNormalization()
    {
        var state = new NativeTextEditState();
        state.Synchronize("active");
        state.MoveHome(extendSelection: false);
        state.Complete("active");

        Assert.True(state.TryGetFocusedPresentation("stale", out int caret, out int start, out int end));
        Assert.Equal(0, caret);
        Assert.Equal(0, start);
        Assert.Equal(0, end);
        Assert.Equal(0, state.GetCaret("active"));
    }

    [Fact]
    public void PresentationLookupExposesTheCurrentSelectionRange()
    {
        var state = new NativeTextEditState();
        const string text = "select";
        state.Synchronize(text);
        state.MoveHome(extendSelection: false);
        state.MoveRight(text, byWord: false, extendSelection: true);
        state.MoveRight(text, byWord: false, extendSelection: true);
        state.Complete(text);

        Assert.True(state.TryGetPresentation(text, out int caret, out int start, out int end));
        Assert.Equal(2, caret);
        Assert.Equal(0, start);
        Assert.Equal(2, end);
    }

    [Fact]
    public void ExternallyReplacedTextResetsTheEditorToTheNewEnd()
    {
        var state = new NativeTextEditState();
        state.Synchronize("first");
        state.MoveHome(extendSelection: false);
        state.Complete("first");

        state.Synchronize("second");

        Assert.Equal("second".Length, state.Caret);
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void CaretLookupOnlyUsesTheActiveTextField()
    {
        var state = new NativeTextEditState();
        const string active = "alpha";
        state.Synchronize(active);
        state.MoveHome(extendSelection: false);
        state.Complete(active);

        Assert.Equal(0, state.GetCaret(active));
        Assert.Equal("other".Length, state.GetCaret("other"));
    }

    [Fact]
    public void HostActionReplacementRetainsItsRequestedCaretAndSelection()
    {
        var state = new NativeTextEditState();

        state.Replace("history", requestedCaret: 3, requestedSelectionAnchor: 1);

        Assert.Equal(3, state.GetCaret("history"));
        Assert.True(state.HasSelection);
        Assert.Equal(1, state.SelectionAnchor);
    }
}
