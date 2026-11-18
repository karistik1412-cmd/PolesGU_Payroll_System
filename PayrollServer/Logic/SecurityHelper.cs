using System.Security.Cryptography;
using System.Text;

namespace PayrollServer.Logic
{
    public static class SecurityHelper
    {
        public static string ComputeHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                    builder.Append(bytes[i].ToString("x2")); // Строго нижний регистр!
                return builder.ToString();
            }
        }
    }
}