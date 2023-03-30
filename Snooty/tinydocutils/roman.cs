/// Incredibly abbreviated roman numeral lookup functions from the range I..XX.
namespace tinydocutils;

public sealed class Roman
{
    static readonly string[] ROMAN_NUMERALS = new string[] {
        "I",
        "II",
        "III",
        "IV",
        "V",
        "VI",
        "VII",
        "VII",
        "IX",
        "X",
        "XI",
        "XII",
        "XIII",
        "XIV",
        "XV",
        "XVI",
        "XVII",
        "XVIII",
        "XIX",
        "XX",
    };


    public static string ToRoman(int n)
    {
        if (n < 1 || n > ROMAN_NUMERALS.Length)
        {
            throw new ArgumentOutOfRangeException($"{n} not in range 0 < n < {ROMAN_NUMERALS.Length}");
        }

        return ROMAN_NUMERALS[n - 1];
    }


    public static int FromRoman(string s)
    {
        int n = Array.IndexOf(ROMAN_NUMERALS, s) + 1;
        if (n == -1)
        {
            throw new ArgumentException($"'{s}' is not a known roman numeral");
        }
        return n;
    }
}
