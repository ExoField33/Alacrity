using System;
using System.Collections.Generic;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class TypedCommandTests
{
    [Fact]
    public void TypedCommandBindsQuotedRequiredAndOptionalArgumentsThroughAlias()
    {
        using var host = new FakePluginHost();
        PluginHostContext context = host.Create(CreateManifest());
        var command = context.Commands.Define("announce", "Announces a message").Alias("say");
        PluginTypedCommandParameter<string> message = command.RequiredString("message", "Message to announce");
        PluginTypedCommandParameter<int> count = command.RequiredInt32("count", 1, 10, "Repeat count");
        PluginTypedCommandParameter<bool> loud = command.OptionalBoolean("loud", false, "Use loud formatting");
        PluginTypedCommandParameter<string> style = command.OptionalChoice("style", "Normal", new[] { "Normal", "Alert" }, "Display style");
        string? result = null;
        command.Register(arguments =>
        {
            result = arguments.Get(message) + ":" + arguments.Get(count) + ":" + arguments.Get(loud) + ":" + arguments.Get(style);
        });

        Assert.True(PluginCommandTokenizer.TryTokenize("say \"two words\" 3 on alert", out var tokens, out _));
        Assert.Equal(PluginCommandDispatchResult.Handled, host.DispatchCommand(tokens[0], CopyArguments(tokens)));
        Assert.Equal("two words:3:True:Alert", result);

        Assert.Equal(PluginCommandDispatchResult.Handled, host.DispatchCommand("announce", new[] { "plain", "2" }));
        Assert.Equal("plain:2:False:Normal", result);
        Assert.True(host.Commands.IsRegistered("announce"));
        Assert.True(host.Commands.IsRegistered("say"));
    }

    [Fact]
    public void TypedCommandReportsConversionAndValidationErrorsWithoutInvokingHandler()
    {
        using var host = new FakePluginHost();
        PluginHostContext context = host.Create(CreateManifest());
        int invocationCount = 0;
        var command = context.Commands.Define("configure", "Configures typed values");
        PluginTypedCommandParameter<int> count = command.RequiredInt32("count", 1, 3);
        PluginTypedCommandParameter<float> scale = command.RequiredSingle("scale", 0.25f, 2f);
        PluginTypedCommandParameter<bool> enabled = command.RequiredBoolean("enabled");
        PluginTypedCommandParameter<TypedMode> mode = command.RequiredEnum<TypedMode>("mode");
        PluginTypedCommandParameter<string> color = command.RequiredChoice("color", new[] { "Red", "Blue" });
        command.Register(arguments =>
        {
            invocationCount++;
            Assert.Equal(2, arguments.Get(count));
            Assert.Equal(1.5f, arguments.Get(scale));
            Assert.True(arguments.Get(enabled));
            Assert.Equal(TypedMode.Fast, arguments.Get(mode));
            Assert.Equal("Blue", arguments.Get(color));
        });

        AssertReply(host, "configure", new[] { "missing", "1.5", "true", "Fast", "Blue" }, "expected an integer");
        AssertReply(host, "configure", new[] { "5", "1.5", "true", "Fast", "Blue" }, "must be between 1 and 3");
        AssertReply(host, "configure", new[] { "2", "NaN", "true", "Fast", "Blue" }, "expected a finite number");
        AssertReply(host, "configure", new[] { "2", "1.5", "perhaps", "Fast", "Blue" }, "expected true/false");
        AssertReply(host, "configure", new[] { "2", "1.5", "true", "Unknown", "Blue" }, "expected one of");
        AssertReply(host, "configure", new[] { "2", "1.5", "true", "Fast", "Green" }, "expected one of");
        AssertReply(host, "configure", new[] { "2", "1.5", "true", "Fast" }, "Missing required argument");
        AssertReply(host, "configure", new[] { "2", "1.5", "true", "Fast", "Blue", "extra" }, "Too many arguments");
        Assert.Equal(0, invocationCount);

        Assert.Equal(
            PluginCommandDispatchResult.Handled,
            host.DispatchCommand("configure", new[] { "2", "1.5", "yes", "fast", "blue" }));
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void TypedCommandValidatesDefinitionsDuringInitialization()
    {
        using var host = new FakePluginHost();
        PluginHostContext context = host.Create(CreateManifest());

        Assert.Throws<InvalidOperationException>(() =>
        {
            var command = context.Commands.Define("layout", "Invalid optional order");
            command.OptionalString("optional", "value");
            command.RequiredString("required");
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            var command = context.Commands.Define("duplicate", "Duplicate parameter");
            command.RequiredString("value");
            command.RequiredString("VALUE");
        });
        Assert.Throws<ArgumentOutOfRangeException>(delegate
        {
            context.Commands.Define("range", "Invalid range").RequiredInt32("value", 3, 1);
        });
        Assert.Throws<ArgumentException>(delegate
        {
            context.Commands.Define("choice", "Invalid choice").OptionalChoice("mode", "missing", new[] { "known" });
        });
        Assert.Throws<ArgumentException>(delegate
        {
            context.Commands.Define("aliases", "Invalid aliases").Alias("same").Alias("SAME").Register(_ => { });
        });
        Assert.Throws<ArgumentException>(delegate
        {
            new PluginCommandDescriptor(
                "metadata",
                "Invalid metadata",
                null,
                new[]
                {
                    new PluginCommandParameterDescriptor("optional", PluginCommandValueKind.String, false),
                    new PluginCommandParameterDescriptor("required", PluginCommandValueKind.String, true)
                });
        });
    }

    [Fact]
    public void TypedCommandMetadataAndTokenizerExposeCompletionInformation()
    {
        var descriptor = new PluginCommandDescriptor(
            "paint",
            "Paints a panel",
            new[] { "colour" },
            new[]
            {
                new PluginCommandParameterDescriptor("label", PluginCommandValueKind.String, true, "Panel label"),
                new PluginCommandParameterDescriptor("color", PluginCommandValueKind.Choice, false, "Color", "Blue", choices: new[] { "Red", "Blue" })
            });

        Assert.Equal(new[] { "colour" }, descriptor.Aliases);
        Assert.Equal(2, descriptor.Parameters.Count);
        Assert.False(descriptor.Parameters[1].IsRequired);
        Assert.Equal(new[] { "Red", "Blue" }, descriptor.Parameters[1].Choices);
        Assert.True(PluginCommandTokenizer.TryTokenize("paint \"two words\" 'three words'", out var tokens, out var error));
        Assert.Null(error);
        Assert.Equal(new[] { "paint", "two words", "three words" }, tokens);
        Assert.False(PluginCommandTokenizer.TryTokenize("paint \"unfinished", out _, out error));
        Assert.Equal("Unclosed quoted command argument.", error);
    }

    [Fact]
    public void TokenizerPreservesWindowsPathsAndOnlyEscapesActiveQuotesOrBackslashes()
    {
        Assert.True(PluginCommandTokenizer.TryTokenize(
            "open \"C:\\Games\\Terraria\" \"C:\\\\Temp\" \"say \\\"hello\\\"\" 'it\\'s fine' \\",
            out var tokens,
            out var error));
        Assert.Null(error);
        Assert.Equal(new[] { "open", "C:\\Games\\Terraria", "C:\\Temp", "say \"hello\"", "it's fine", "\\" }, tokens);

        Assert.True(PluginCommandTokenizer.TryTokenize("empty \"\"", out tokens, out error));
        Assert.Equal(new[] { "empty", string.Empty }, tokens);
    }

    [Fact]
    public void TypedCommandRejectsDefaultsOutsideTheInputSemanticDomain()
    {
        using var host = new FakePluginHost();
        PluginHostContext context = host.Create(CreateManifest());

        Assert.Throws<ArgumentException>(() => context.Commands.Define("float", "Invalid default").OptionalSingle("value", float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.Commands.Define("bounds", "Invalid bounds").RequiredSingle("value", float.NegativeInfinity, 1f));
        Assert.Throws<ArgumentException>(() => context.Commands.Define("mode", "Invalid enum").OptionalEnum("mode", (TypedMode)99));
        Assert.Throws<ArgumentException>(() => context.Commands.Define("validated", "Invalid validator").OptionalString("value", "bad", validator: _ => "rejected"));
    }

    [Fact]
    public void CommandMetadataAndInvocationArgumentsDefensivelyCopyCallerCollections()
    {
        var aliases = new[] { "old" };
        var choices = new[] { "one", "two" };
        var descriptor = new PluginCommandDescriptor("copy", "Copies metadata", aliases, new[]
        {
            new PluginCommandParameterDescriptor("choice", PluginCommandValueKind.Choice, false, choices: choices)
        });

        aliases[0] = "mutated";
        choices[0] = "mutated";
        Assert.Equal("old", descriptor.Aliases[0]);
        Assert.Equal("one", descriptor.Parameters[0].Choices[0]);
        Assert.False(descriptor.Aliases is string[]);
        Assert.False(descriptor.Parameters[0].Choices is string[]);

        var supplied = new[] { "first" };
        var invocation = new PluginCommandInvocation(supplied);
        supplied[0] = "mutated";
        Assert.Equal("first", invocation.Arguments[0]);
        Assert.False(invocation.Arguments is string[]);
    }

    [Fact]
    public void TypedCommandUsesTheExistingScopedHostAndAttributesFailures()
    {
        using var host = new FakePluginHost();
        PluginHostContext context = host.Create(CreateManifest());
        IPluginCommandService staleCommands = context.Commands;
        context.Commands.Define("explode", "Throws for isolation coverage")
            .Register(_ => throw new InvalidOperationException("expected test failure"));

        Assert.Equal(PluginCommandDispatchResult.HandledWithFailure, host.DispatchCommand("explode", Array.Empty<string>()));
        Assert.Contains(host.Diagnostics, entry => entry.Contains("typed.command.tests", StringComparison.Ordinal) && entry.Contains("explode", StringComparison.Ordinal));

        context.Resources.Dispose();
        Assert.Equal(PluginCommandDispatchResult.NotFound, host.DispatchCommand("explode", Array.Empty<string>()));
        Assert.Throws<ObjectDisposedException>(() => staleCommands.Define("stale", "Must not register").Register(_ => { }));
    }

    [Fact]
    public void TypedCommandAppliesCustomValidationBeforeHandlerInvocation()
    {
        using var host = new FakePluginHost();
        PluginHostContext context = host.Create(CreateManifest());
        bool invoked = false;
        var command = context.Commands.Define("name", "Validates a name");
        command.RequiredString("value", validator: value => value.StartsWith("A", StringComparison.Ordinal) ? null : "must begin with A");
        command.Register(_ => invoked = true);

        AssertReply(host, "name", new[] { "invalid" }, "must begin with A");
        Assert.False(invoked);
        Assert.Equal(PluginCommandDispatchResult.Handled, host.DispatchCommand("name", new[] { "Allowed" }));
        Assert.True(invoked);
    }

    private static void AssertReply(FakePluginHost host, string id, string[] arguments, string expectedFragment)
    {
        string? reply = null;
        Assert.Equal(PluginCommandDispatchResult.Handled, host.DispatchCommand(id, arguments, value => reply = value));
        Assert.NotNull(reply);
        Assert.Contains(expectedFragment, reply!, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] CopyArguments(IReadOnlyList<string> tokens)
    {
        var arguments = new string[tokens.Count - 1];
        for (int index = 0; index < arguments.Length; index++)
        {
            arguments[index] = tokens[index + 1];
        }

        return arguments;
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("typed.command.tests"),
            "Typed command tests",
            new Version(1, 0),
            "Tests",
            "Command-host test plugin",
            new[] { "1.4.5.6" });
    }

    private enum TypedMode
    {
        Slow,
        Fast
    }
}
