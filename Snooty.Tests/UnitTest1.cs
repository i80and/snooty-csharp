namespace Snooty.Tests;

public class UnitTest1
{
    [Fact]
    public void Util_ColumnWidth_ReturnCorrect()
    {
        Assert.Equal(7, tinydocutils.Util.ColumnWidth("A t̆ab̆lĕ"));
    }

    [Fact]
    public void Util_Unicode_ReturnCorrect()
    {
        Assert.Equal("➤", tinydocutils.Util.UnicodeCode("U+27A4"));
        Assert.Equal("→", tinydocutils.Util.UnicodeCode("0x2192"));
        Assert.Equal("🦨", tinydocutils.Util.UnicodeCode("129448"));
        Assert.Equal("☮", tinydocutils.Util.UnicodeCode("&#x262E;"));
        Assert.Throws<ArgumentException>(() => {
            tinydocutils.Util.UnicodeCode("U+FFFFFFFFFFFFFFF");
        });
        Assert.Throws<ArgumentException>(() => {
            tinydocutils.Util.UnicodeCode("99z");
        });
        Assert.Throws<ArgumentException>(() => {
            tinydocutils.Util.UnicodeCode("");
        });
    }
}
