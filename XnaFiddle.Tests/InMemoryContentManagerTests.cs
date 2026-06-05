using FlatRedBall.AnimationChain.Content;
using XnaFiddle;

namespace XnaFiddle.Tests;

public class InMemoryContentManagerTests
{
    [Fact]
    public void AddFile_WithContentPrefixedPath_AddsBaseNameAliases()
    {
        InMemoryContentManager.ClearFiles();
        try
        {
            InMemoryContentManager.AddFile("Content/AnimatedSpritesheet.png", [1, 2, 3]);

            Assert.Contains("AnimatedSpritesheet.png", InMemoryContentManager.Files.Keys);
            Assert.Contains("AnimatedSpritesheet", InMemoryContentManager.Files.Keys);
        }
        finally
        {
            InMemoryContentManager.ClearFiles();
        }
    }

    [Fact]
    public void RemoveFile_WithLeadingSlash_RemovesPreviouslyAddedFile()
    {
        InMemoryContentManager.ClearFiles();
        try
        {
            InMemoryContentManager.AddFile("AnimatedSpritesheet.png", [1, 2, 3]);

            InMemoryContentManager.RemoveFile("/AnimatedSpritesheet.png");

            Assert.DoesNotContain("AnimatedSpritesheet.png", InMemoryContentManager.Files.Keys);
            Assert.DoesNotContain("AnimatedSpritesheet", InMemoryContentManager.Files.Keys);
        }
        finally
        {
            InMemoryContentManager.ClearFiles();
        }
    }

    [Fact]
    public void AddFile_WithUrlEscapedName_AddsDecodedAlias()
    {
        InMemoryContentManager.ClearFiles();
        try
        {
            InMemoryContentManager.AddFile("ChatGPT%20Image.png", [1, 2, 3]);

            Assert.Contains("ChatGPT Image.png", InMemoryContentManager.Files.Keys);
            Assert.Contains("ChatGPT Image", InMemoryContentManager.Files.Keys);
            Assert.Equal(3, InMemoryContentManager.Files["ChatGPT Image.png"].Length);
        }
        finally
        {
            InMemoryContentManager.ClearFiles();
        }
    }

    /// <summary>
    /// Regression test for GL_INVALID_OPERATION (0x0502): the real-world asd.achx file
    /// produced by the FlatRedBall Animation Editor contains frames with negative
    /// LeftCoordinate values (e.g. -1). Passing a Rectangle with X=-1 to SpriteBatch.Draw
    /// triggers a WebGL error. This test confirms the raw parse exposes the problem so we
    /// know the sanitization in InMemoryContentManager.Load is necessary.
    /// </summary>
    [Fact]
    public void AsdAchx_ParsedFrames_ContainNegativeCoordinateThatRequiresSanitization()
    {
        // This is the actual content of the asd.achx file supplied by the user.
        const string achxXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <AnimationChainArraySave xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <FileRelativeTextures>true</FileRelativeTextures>
              <TimeMeasurementUnit>Second</TimeMeasurementUnit>
              <CoordinateType>Pixel</CoordinateType>
              <AnimationChain>
                <Name>NewAnimation</Name>
                <Frame>
                  <TextureName>AnimatedSpritesheet.png</TextureName>
                  <FrameLength>0.65</FrameLength>
                  <LeftCoordinate>-1</LeftCoordinate>
                  <RightCoordinate>15</RightCoordinate>
                  <TopCoordinate>0</TopCoordinate>
                  <BottomCoordinate>32</BottomCoordinate>
                </Frame>
                <Frame>
                  <TextureName>AnimatedSpritesheet.png</TextureName>
                  <FrameLength>0.8</FrameLength>
                  <LeftCoordinate>33</LeftCoordinate>
                  <RightCoordinate>53</RightCoordinate>
                  <TopCoordinate>58</TopCoordinate>
                  <BottomCoordinate>90</BottomCoordinate>
                </Frame>
              </AnimationChain>
            </AnimationChainArraySave>
            """;

        var save = AnimationChainListSave.FromString(achxXml);

        Assert.Single(save.AnimationChains);
        AnimationChainSave chain = save.AnimationChains[0];
        Assert.Equal("NewAnimation", chain.Name);

        AnimationFrameSave frame0 = chain.Frames[0];
        // Confirm the negative coordinate is present in the raw file — this is the bug.
        Assert.Equal(-1f, frame0.LeftCoordinate);
    }

    /// <summary>
    /// Verifies that the asd.achx contains multiple named animation chains, so callers
    /// can choose which chain to play by name rather than there being only one.
    /// </summary>
    [Fact]
    public void AsdAchx_ParsedFile_HasMultipleNamedChains()
    {
        // Minimal excerpt confirming two named chains parse correctly.
        const string achxXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <AnimationChainArraySave xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <FileRelativeTextures>true</FileRelativeTextures>
              <TimeMeasurementUnit>Second</TimeMeasurementUnit>
              <CoordinateType>Pixel</CoordinateType>
              <AnimationChain>
                <Name>NewAnimation</Name>
                <Frame>
                  <TextureName>AnimatedSpritesheet.png</TextureName>
                  <FrameLength>0.65</FrameLength>
                  <LeftCoordinate>0</LeftCoordinate>
                  <RightCoordinate>16</RightCoordinate>
                  <TopCoordinate>0</TopCoordinate>
                  <BottomCoordinate>32</BottomCoordinate>
                </Frame>
              </AnimationChain>
              <AnimationChain>
                <Name>NewAnimation2</Name>
                <Frame>
                  <TextureName>AnimatedSpritesheet.png</TextureName>
                  <FrameLength>0.1</FrameLength>
                  <LeftCoordinate>96</LeftCoordinate>
                  <RightCoordinate>112</RightCoordinate>
                  <TopCoordinate>32</TopCoordinate>
                  <BottomCoordinate>64</BottomCoordinate>
                </Frame>
              </AnimationChain>
            </AnimationChainArraySave>
            """;

        var save = AnimationChainListSave.FromString(achxXml);

        Assert.Equal(2, save.AnimationChains.Count);
        Assert.Equal("NewAnimation", save.AnimationChains[0].Name);
        Assert.Equal("NewAnimation2", save.AnimationChains[1].Name);
    }
}
