using System;

namespace Clarion.SourceResolution
{
    /// <summary>
    /// Reproduces the hash the Clarion IDE uses to name its per-solution
    /// preferences file:
    /// <c>%APPDATA%\SoftVelocity\Clarion\&lt;ver&gt;\preferences\&lt;Sln&gt;.sln.&lt;hash&gt;.xml</c>.
    ///
    /// This is the .NET Framework 4.x 32-bit (x86) <c>string.GetHashCode()</c>
    /// computed over the LOWERCASED full .sln path (dual DJB hash, 1566083941
    /// combiner). Ported literally from the extension's
    /// <c>ClarionIdePreferences.computeSlnHash</c>.
    ///
    /// IMPORTANT: do NOT substitute the runtime's <c>string.GetHashCode()</c> —
    /// .NET Core / .NET 5+ (which runs the debugger) randomizes string hashes
    /// per process and uses a different algorithm, so it will not match the
    /// filenames the Framework-based IDE wrote. The fixture in the test project
    /// pins the known vector ("c:\\development\\ibsworking\\ap1.sln" ->
    /// "ecfee7f0") to guard against exactly that mistake.
    /// </summary>
    public static class SlnHash
    {
        /// <summary>
        /// Computes the 8-or-fewer hex-digit hash (no leading zeros, lowercase)
        /// for the given .sln path. The path is lowercased before hashing.
        /// </summary>
        public static string Compute(string slnPath)
        {
            if (slnPath == null) throw new ArgumentNullException(nameof(slnPath));

            string s = slnPath.ToLowerInvariant();

            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                int len = s.Length;
                int i = 0;
                while (len > 0)
                {
                    int c0 = s[i];
                    int c1 = len > 1 ? s[i + 1] : 0;
                    int pint0 = c0 | (c1 << 16);
                    hash1 = ((((hash1 << 5) + hash1) + (hash1 >> 27)) ^ pint0);

                    if (len <= 2)
                        break;

                    int c2 = s[i + 2];
                    int c3 = len > 3 ? s[i + 3] : 0;
                    int pint1 = c2 | (c3 << 16);
                    hash2 = ((((hash2 << 5) + hash2) + (hash2 >> 27)) ^ pint1);

                    i += 4;
                    len -= 4;
                }

                int result = hash1 + (hash2 * 1566083941);
                return ((uint)result).ToString("x");
            }
        }
    }
}
