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

    [Fact]
    public void Parser_Parse() {
        var document = tinydocutils.Document.New("foo.rst", new tinydocutils.OptionParser());
        var parser = new tinydocutils.Parser();
        parser.Parse(
        """
        :template: product-landing
        :hidefeedback: header
        :noprevnext:

        ================
        What is MongoDB?
        ================

        .. |arrow| unicode:: U+27A4

        This is a test. |arrow| Use the **Select your language** drop-down menu in the list.

        * - Introduction

            An introduction to things.
          - Developers
          - Administrators
          - Reference
        """, document);
    }
}
