#region

using Mehrak.Bot.Shared.Extensions;
using Mehrak.Bot.Shared.Models;
using Mehrak.Domain.Command.Models;
using NetCord;
using NetCord.Rest;
using static Mehrak.Bot.Shared.Models.CommandText;

#endregion

namespace Mehrak.Bot.Tests.Shared.Extensions;

/// <summary>
/// Unit tests for CommandPresentationExtensions validating message conversion,
/// component handling, attachment processing, and text formatting.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CommandPresentationExtensionsTests
{
    #region ToFormattedString Tests

    [Test]
    public void ToFormattedString_PlainText_ReturnsUnformattedString()
    {
        // Arrange
        var text = new CommandText("Hello World", TextType.Plain);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("Hello World"));
    }

    [Test]
    public void ToFormattedString_Header1_AddsHashPrefix()
    {
        // Arrange
        var text = new CommandText("Title", TextType.Header1);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("# Title"));
    }

    [Test]
    public void ToFormattedString_Header2_AddsDoubleHashPrefix()
    {
        // Arrange
        var text = new CommandText("Subtitle", TextType.Header2);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("## Subtitle"));
    }

    [Test]
    public void ToFormattedString_Header3_AddsTripleHashPrefix()
    {
        // Arrange
        var text = new CommandText("Section", TextType.Header3);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("### Section"));
    }

    [Test]
    public void ToFormattedString_Footer_AddsFooterPrefix()
    {
        // Arrange
        var text = new CommandText("Footer text", TextType.Footer);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("-# Footer text"));
    }

    [Test]
    public void ToFormattedString_Bold_WrapsWithDoubleAsterisks()
    {
        // Arrange
        var text = new CommandText("Bold text", TextType.Bold);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("**Bold text**"));
    }

    [Test]
    public void ToFormattedString_Italic_WrapsWithSingleAsterisk()
    {
        // Arrange
        var text = new CommandText("Italic text", TextType.Italic);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("*Italic text*"));
    }

    [Test]
    public void ToFormattedString_BoldAndItalic_WrapsBothCorrectly()
    {
        // Arrange
        var text = new CommandText("Bold and Italic", TextType.Bold | TextType.Italic);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("***Bold and Italic***"));
    }

    [Test]
    public void ToFormattedString_Header1AndBold_CombinesFormatting()
    {
        // Arrange
        var text = new CommandText("Bold Title", TextType.Header1 | TextType.Bold);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("# **Bold Title**"));
    }

    [Test]
    public void ToFormattedString_Header2AndItalic_CombinesFormatting()
    {
        // Arrange
        var text = new CommandText("Italic Subtitle", TextType.Header2 | TextType.Italic);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("## *Italic Subtitle*"));
    }

    [Test]
    public void ToFormattedString_FooterAndBold_CombinesFormatting()
    {
        // Arrange
        var text = new CommandText("Bold Footer", TextType.Footer | TextType.Bold);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("-# **Bold Footer**"));
    }

    [Test]
    public void ToFormattedString_AllFormats_CombinesCorrectly()
    {
        // Arrange
        var text = new CommandText("Complex", TextType.Header1 | TextType.Bold | TextType.Italic);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("# ***Complex***"));
    }

    [Test]
    public void ToFormattedString_EmptyString_ReturnsFormattedEmpty()
    {
        // Arrange
        var text = new CommandText("", TextType.Bold);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("****"));
    }

    [Test]
    public void ToFormattedString_SpecialCharacters_PreservesContent()
    {
        // Arrange
        var text = new CommandText("Special: @#$%^&*()", TextType.Plain);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("Special: @#$%^&*()"));
    }

    [Test]
    public void ToFormattedString_UnicodeText_HandlesCorrectly()
    {
        // Arrange
        var text = new CommandText("测试 🎮 данные", TextType.Bold);

        // Act
        var result = text.ToFormattedString();

        // Assert
        Assert.That(result, Is.EqualTo("**测试 🎮 данные**"));
    }

    #endregion

    #region ToMessage - Non-Container Tests

    [Test]
    public void ToMessage_EmptyComponents_ReturnsMessageWithFlags()
    {
        // Arrange
        var data = new CommandPresentation(null, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Flags!.Value.HasFlag(MessageFlags.IsComponentsV2), Is.True);
    }

    [Test]
    public void ToMessage_SingleTextComponent_AddsTextDisplay()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandText("Test message", TextType.Plain)
        };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Components, Is.Not.Null);
        Assert.That(result.Components!.Count(), Is.GreaterThan(0));
        Assert.That(result.Components!.First(), Is.TypeOf<TextDisplayProperties>());
    }

    [Test]
    public void ToMessage_MultipleTextComponents_AddsAllTexts()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandText("First", TextType.Header1),
            new CommandText("Second", TextType.Plain),
            new CommandText("Third", TextType.Bold)
        };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Components, Is.Not.Null);
        var textDisplays = result.Components!.OfType<TextDisplayProperties>().ToList();
        Assert.That(textDisplays, Has.Count.EqualTo(3));
    }

    [Test]
    public void ToMessage_SingleAttachment_CreatesMediaGallery()
    {
        // Arrange
        var attachment = new CommandAttachment("test.png");
        var components = new List<IPresentationComponent> { attachment };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Components, Is.Not.Null);
        Assert.That(result.Components!.OfType<MediaGalleryProperties>(), Is.Not.Empty);
        Assert.That(result.Attachments, Is.Null.Or.Empty);
    }

    [Test]
    public void ToMessage_MultipleAttachments_AddsToSameGallery()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandAttachment("image1.png"),
            new CommandAttachment("image2.jpg"),
            new CommandAttachment("image3.gif")
        };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Components, Is.Not.Null);
        var galleries = result.Components!.OfType<MediaGalleryProperties>().ToList();
        Assert.That(galleries, Has.Count.EqualTo(1), "Should have exactly one gallery");
        Assert.That(result.Attachments, Is.Null.Or.Empty);
    }

    [Test]
    public void ToMessage_MixedTextAndAttachments_CreatesCorrectStructure()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandText("Title", TextType.Header1),
            new CommandAttachment("image.png"),
            new CommandText("Description", TextType.Plain),
            new CommandAttachment("image2.png")
        };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Components, Is.Not.Null);
        Assert.That(result.Components!.OfType<TextDisplayProperties>().Count(), Is.EqualTo(2));
        Assert.That(result.Components!.OfType<MediaGalleryProperties>().Count(), Is.EqualTo(2));
        Assert.That(result.Attachments, Is.Null.Or.Empty);
    }

    #endregion

    #region ToMessage - Container Tests

    [Test]
    public void ToMessage_ContainerWithSingleSection_CreatesContainerWithSection()
    {
        // Arrange
        var section = CreateSection(
            "section.png",
            [
                new CommandText("Section Title", TextType.Header2),
                new CommandText("Section Content", TextType.Plain)
            ]);
        var components = new List<IPresentationComponent> { section };
        var data = new CommandPresentation(components, true, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Components, Is.Not.Null);
        var container = result.Components!.OfType<ComponentContainerProperties>().FirstOrDefault();
        Assert.That(container, Is.Not.Null);
        Assert.That(container!.Components, Is.Not.Null);
        Assert.That(container.Components!.OfType<ComponentSectionProperties>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ToMessage_ContainerWithMultipleSections_AddsAllSections()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            CreateSection("section1.png", [new CommandText("Section 1", TextType.Header2)]),
            CreateSection("section2.png", [new CommandText("Section 2", TextType.Header2)]),
            CreateSection("section3.png", [new CommandText("Section 3", TextType.Header2)])
        };
        var data = new CommandPresentation(components, true, false);

        // Act
        var result = data.ToMessage();

        // Assert
        var container = result.Components!.OfType<ComponentContainerProperties>().First();
        Assert.That(container.Components!.OfType<ComponentSectionProperties>().Count(), Is.EqualTo(3));
    }

    [Test]
    public void ToMessage_ContainerWithAttachments_CreatesMediaGalleryInContainer()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandAttachment("image1.png"),
            new CommandAttachment("image2.png")
        };
        var data = new CommandPresentation(components, true, false);

        // Act
        var result = data.ToMessage();

        // Assert
        var container = result.Components!.OfType<ComponentContainerProperties>().First();
        Assert.That(container.Components!.OfType<MediaGalleryProperties>().Count(), Is.EqualTo(1));
        Assert.That(result.Attachments, Is.Null.Or.Empty);
    }

    [Test]
    public void ToMessage_ContainerWithTextComponent_AddsTextDisplayToContainer()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandText("Container Text", TextType.Bold)
        };
        var data = new CommandPresentation(components, true, false);

        // Act
        var result = data.ToMessage();

        // Assert
        var container = result.Components!.OfType<ComponentContainerProperties>().First();
        Assert.That(container.Components!.OfType<TextDisplayProperties>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ToMessage_ContainerWithMixedComponents_OrganizesCorrectly()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            CreateSection("section.png", [new CommandText("Section", TextType.Header2)]),
            new CommandText("Text", TextType.Plain),
            new CommandAttachment("image.png")
        };
        var data = new CommandPresentation(components, true, false);

        // Act
        var result = data.ToMessage();

        // Assert
        var container = result.Components!.OfType<ComponentContainerProperties>().First();
        Assert.That(container.Components!.OfType<ComponentSectionProperties>().Count(), Is.EqualTo(1));
        Assert.That(container.Components!.OfType<TextDisplayProperties>().Count(), Is.EqualTo(1));
        Assert.That(container.Components!.OfType<MediaGalleryProperties>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ToMessage_ContainerWithSectionAndMultipleAttachments_GroupsAttachmentsInGallery()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            CreateSection("section.png",
                [new CommandText("Title", TextType.Header1)]),
            new CommandAttachment("image1.png"),
            new CommandAttachment("image2.png"),
            new CommandAttachment("image3.png")
        };
        var data = new CommandPresentation(components, true, false);

        // Act
        var result = data.ToMessage();

        // Assert
        var container = result.Components!.OfType<ComponentContainerProperties>().First();
        var galleries = container.Components!.OfType<MediaGalleryProperties>().ToList();
        Assert.That(galleries, Has.Count.EqualTo(1), "Attachments should be grouped in one gallery");

        // Attachments are no longer added by ToMessage
        Assert.That(result.Attachments, Is.Null.Or.Empty);
    }

    #endregion

    #region Attachment Processing Tests

    [Test]
    public void ToMessage_AttachmentWithFileName_NotAddedToAttachments()
    {
        // Arrange
        var attachment = new CommandAttachment("test-image.png");
        var components = new List<IPresentationComponent> { attachment };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Attachments, Is.Null.Or.Empty, "CommandAttachments should not be added to message attachments");
    }

    [Test]
    public void ToMessage_MultipleAttachmentsWithDifferentNames_NotAddedToAttachments()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandAttachment("file1.png"),
            new CommandAttachment("file2.jpg"),
            new CommandAttachment("file3.gif")
        };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Attachments, Is.Null.Or.Empty);
    }

    [Test]
    public void ToMessage_SectionWithAttachment_DoesNotAddToAttachments()
    {
        // Arrange
        var section = CreateSection(
            "thumbnail.png",
            [new CommandText("Test", TextType.Plain)]);
        var components = new List<IPresentationComponent> { section };
        var data = new CommandPresentation(components, true, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Attachments, Is.Null.Or.Empty);
    }

    #endregion

    #region Edge Cases and Special Scenarios

    [Test]
    public void ToMessage_EmptyComponentsList_ReturnsValidMessage()
    {
        // Arrange
        var data = new CommandPresentation([], false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Flags!.Value.HasFlag(MessageFlags.IsComponentsV2), Is.True);
    }

    [Test]
    public void ToMessage_OnlyUnknownComponentType_HandlesGracefully()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new TestUnknownComponent()
        };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result, Is.Not.Null);
        // Unknown components should be ignored
    }

    [Test]
    public void ToMessage_ContainerTrue_CreatesComponentContainer()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandText("Test", TextType.Plain)
        };
        var data = new CommandPresentation(components, true, false);

        // Act
        var result = data.ToMessage();

        // Assert
        Assert.That(result.Components, Is.Not.Null);
        Assert.That(result.Components!.OfType<ComponentContainerProperties>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ToMessage_ContainerFalse_DoesNotCreateComponentContainer()
    {
        // Arrange
        var components = new List<IPresentationComponent>
        {
            new CommandText("Test", TextType.Plain)
        };
        var data = new CommandPresentation(components, false, false);

        // Act
        var result = data.ToMessage();

        // Assert
        var containers = result.Components?.OfType<ComponentContainerProperties>().ToList();
        Assert.That(containers, Is.Null.Or.Empty);
    }

    #endregion

    #region Helper Classes

    private static CommandSection CreateSection(string fileName, IEnumerable<CommandText> components) =>
        new(components, new CommandAttachment(fileName));

    /// <summary>
    /// Unknown component type for testing default case handling
    /// </summary>
    private class TestUnknownComponent : IPresentationComponent
    {
    }

    #endregion
}
