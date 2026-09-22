namespace IRSpeedyVPN.Common
{
    internal static class PersianDigits
    {
        public static string Format(string value)
        {
            var chars = (value ?? "").ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] >= '0' && chars[i] <= '9') chars[i] = (char)('۰' + chars[i] - '0');
            return new string(chars);
        }
    }
}
