using Clarion.SourceResolution;
using Xunit;

namespace Clarion.SourceResolution.Tests
{
    public class SlnHashTests
    {
        // The canonical vector carried over from the extension's TS port. If
        // this ever fails on net8, someone likely swapped the literal algorithm
        // for the runtime's randomized string.GetHashCode() — see SlnHash docs.
        [Fact]
        public void Compute_KnownVector_MatchesFrameworkHash()
        {
            Assert.Equal("ecfee7f0", SlnHash.Compute(@"c:\development\ibsworking\ap1.sln"));
        }

        [Fact]
        public void Compute_IsCaseInsensitive_ViaLowercasing()
        {
            // The IDE hashes the lowercased path, so case must not change the result.
            Assert.Equal(
                SlnHash.Compute(@"c:\development\ibsworking\ap1.sln"),
                SlnHash.Compute(@"C:\Development\IBSWorking\AP1.SLN"));
        }
    }
}
