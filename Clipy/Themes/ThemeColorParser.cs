namespace Clipy.Themes;

public static class ThemeColorParser
{
    public static Windows.UI.Color Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Windows.UI.Color.FromArgb(255, 0x0B, 0x0B, 0x0F);

        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 6)
            return Windows.UI.Color.FromArgb(
                255,
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));

        if (hex.Length == 8)
            return Windows.UI.Color.FromArgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));

        return Windows.UI.Color.FromArgb(255, 0x0B, 0x0B, 0x0F);
    }
}
