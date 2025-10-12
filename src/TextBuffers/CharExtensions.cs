using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluidX.TextBuffers;

internal static class CharExtensions
{
    // IsBasicASCII
    public static bool IsBasicASCII(this char ch)
    {
        return (ch >= 0x20 && ch <= 0x7E) || ch == '\t';
    }
}
