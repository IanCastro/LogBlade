using System.Text;

internal static class MultilineEditText
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value) || (value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0))
        {
            return value;
        }

        StringBuilder output = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (current == '\r')
            {
                output.Append("\r\n");
                if (i + 1 < value.Length && value[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            if (current == '\n')
            {
                output.Append("\r\n");
                continue;
            }

            output.Append(current);
        }

        return output.ToString();
    }
}
