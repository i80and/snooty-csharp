namespace Snooty.Tests;

public class UnitTest1
{
    [Fact]
    public void Util_ColumnWidth_ReturnCorrect()
    {
        Assert.Equal(7, tinydocutils.Util.ColumnWidth("A t̆ab̆lĕ"));
    }
}
