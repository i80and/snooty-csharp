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
        Assert.Throws<ArgumentException>(() =>
        {
            tinydocutils.Util.UnicodeCode("U+FFFFFFFFFFFFFFF");
        });
        Assert.Throws<ArgumentException>(() =>
        {
            tinydocutils.Util.UnicodeCode("99z");
        });
        Assert.Throws<ArgumentException>(() =>
        {
            tinydocutils.Util.UnicodeCode("");
        });
    }

    [Fact]
    public void Parser_Parse()
    {
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

        Assert.Equal("""
        <Document>
        <FieldList>
        <Field><FieldName><Text>template</Text></FieldName><FieldBody><Paragraph><Text>product-landing</Text></Paragraph></FieldBody></Field>
        <Field><FieldName><Text>hidefeedback</Text></FieldName><FieldBody><Paragraph><Text>header</Text></Paragraph></FieldBody></Field>
        <Field><FieldName><Text>noprevnext</Text></FieldName><FieldBody></FieldBody></Field>
        </FieldList>
        <Section><Title><Text>What is MongoDB?</Text></Title><SystemMessage></SystemMessage>
        <Paragraph><Text>This is a test. </Text><SubstitutionReference><Text>arrow</Text></SubstitutionReference><Text> Use the </Text><Strong><Text>Select your language</Text></Strong><Text> drop-down menu in the list.</Text></Paragraph>
        <BulletList>
        <ListItem>
        <BulletList><ListItem><Paragraph><Text>Introduction</Text></Paragraph><Paragraph><Text>An introduction to things.</Text></Paragraph></ListItem>
        <ListItem><Paragraph><Text>Developers</Text></Paragraph></ListItem><ListItem><Paragraph><Text>Administrators</Text></Paragraph></ListItem>
        <ListItem><Paragraph><Text>Reference</Text></Paragraph></ListItem>
        </BulletList>
        </ListItem>
        </BulletList>
        </Section>
        </Document>
        """.Replace("\n", ""), document.ToString());
    }
}
