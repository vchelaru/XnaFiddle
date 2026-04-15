using XnaFiddle.Intellisense;

namespace XnaFiddle.Tests.Intellisense;

public class XmlDocFormatterTests
{
    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", XmlDocFormatter.Format(""));
    }

    [Fact]
    public void Null_ReturnsEmpty()
    {
        Assert.Equal("", XmlDocFormatter.Format(null));
    }

    [Fact]
    public void MalformedXml_ReturnsEmpty()
    {
        var result = XmlDocFormatter.Format("<summary>unclosed");
        Assert.Equal("", result);
    }

    [Fact]
    public void Summary_SimpleText()
    {
        var result = XmlDocFormatter.Format("<summary>Hi</summary>");
        Assert.Equal("Hi", result);
    }

    [Fact]
    public void Summary_WithSeeCref_StripsPrefix_AndInlineCodes()
    {
        var result = XmlDocFormatter.Format(
            "<summary>With <see cref=\"T:System.String\"/> ref</summary>");
        Assert.Contains("`System.String`", result);
        Assert.DoesNotContain("T:System.String", result);
        Assert.StartsWith("With", result);
    }

    [Fact]
    public void Param_RenderedUnderParametersHeading()
    {
        var result = XmlDocFormatter.Format(
            "<param name=\"x\">desc</param>");
        Assert.Contains("**Parameters:**", result);
        Assert.Contains("- `x`: desc", result);
    }

    [Fact]
    public void Returns_RenderedUnderReturnsHeading()
    {
        var result = XmlDocFormatter.Format("<returns>The answer</returns>");
        Assert.Contains("**Returns:** The answer", result);
    }

    [Fact]
    public void MultipleSections_InExpectedOrder()
    {
        var xml =
            "<summary>Sum</summary>" +
            "<param name=\"a\">A</param>" +
            "<returns>Result</returns>" +
            "<remarks>Notes</remarks>";
        var result = XmlDocFormatter.Format(xml);

        int iSum = result.IndexOf("Sum");
        int iParams = result.IndexOf("**Parameters:**");
        int iReturns = result.IndexOf("**Returns:**");
        int iRemarks = result.IndexOf("**Remarks:**");

        Assert.True(iSum >= 0 && iParams > iSum, "summary before parameters");
        Assert.True(iReturns > iParams, "parameters before returns");
        Assert.True(iRemarks > iReturns, "returns before remarks");
    }

    [Fact]
    public void MemberWrappedXml_IsUnwrapped()
    {
        var wrapped = "<member name=\"T:Foo\"><summary>Inner</summary></member>";
        var bare = "<summary>Inner</summary>";
        Assert.Equal(XmlDocFormatter.Format(bare), XmlDocFormatter.Format(wrapped));
    }

    [Fact]
    public void CTag_RendersAsInlineCode()
    {
        var result = XmlDocFormatter.Format("<summary>Use <c>code</c> here</summary>");
        Assert.Contains("`code`", result);
    }
}
